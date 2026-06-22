using System;
using System.IO;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    [Serializable]
    internal sealed class PsdUiToolkitNineSliceConfigData
    {
        public const int DefaultBorderInset = 2;
        public const int DefaultPixelThreshold = 10;
        public const int DefaultMinCenterCols = 10;
        public const int DefaultMinCenterRows = 10;
        public const int DefaultMinSameZone = 15;

        public int borderInset = 2;
        public int pixelThreshold = 10;
        public int minCenterCols = 10;
        public int minCenterRows = 10;
        public int minSameZone = 15;

        public void Sanitize()
        {
            borderInset = Mathf.Max(0, borderInset);
            pixelThreshold = Mathf.Clamp(pixelThreshold, 0, 255);
            minCenterCols = Mathf.Clamp(minCenterCols, 1, 4096);
            minCenterRows = Mathf.Clamp(minCenterRows, 1, 4096);
            minSameZone = Mathf.Clamp(minSameZone, 1, 4096);
        }
    }

    [Serializable]
    internal sealed class PsdUiToolkitDedupConfigData
    {
        public const float DefaultMaeThreshold = 0.04f;
        public const float MinMaeThreshold = 0.001f;
        public const float MaxMaeThreshold = 0.5f;
        public const int DefaultFingerprintSize = 8;
        public const int MinFingerprintSize = 4;
        public const int MaxFingerprintSize = 32;

        public float maeThreshold = 0.04f;
        public int fingerprintSize = 8;

        public void Sanitize()
        {
            maeThreshold = Mathf.Clamp(maeThreshold, MinMaeThreshold, MaxMaeThreshold);
            fingerprintSize = Mathf.Clamp(fingerprintSize, MinFingerprintSize, MaxFingerprintSize);
        }
    }

    [Serializable]
    internal sealed class PsdUiToolkitCommonDirectoriesData
    {
        public string[] paths = Array.Empty<string>();
    }

    internal static class PsdUiToolkitImageExportConfig
    {
        private static PsdUiToolkitNineSliceConfigData _nineSliceCache;
        private static PsdUiToolkitDedupConfigData _dedupCache;
        private static PsdUiToolkitCommonDirectoriesData _commonDirectoriesCache;

        public static string EditorConfigDirectory => Path.Combine(Application.dataPath, "PsdToUIToolKit", "EditorConfig");

        public static string NineSliceConfigPath => Path.Combine(EditorConfigDirectory, "PSD_NineSliceConfig.json");

        public static string DedupConfigPath => Path.Combine(EditorConfigDirectory, "PSD_DedupConfig.json");

        public static string CommonDirectoriesConfigPath => Path.Combine(EditorConfigDirectory, "PSD_CommonDirectories.json");

        public static PsdUiToolkitNineSliceConfigData LoadNineSlice(bool forceReload = false)
        {
            if (_nineSliceCache != null && !forceReload)
                return _nineSliceCache;

            _nineSliceCache = LoadJsonOrDefault(NineSliceConfigPath, new PsdUiToolkitNineSliceConfigData());
            _nineSliceCache.Sanitize();
            return _nineSliceCache;
        }

        public static PsdUiToolkitDedupConfigData LoadDedup(bool forceReload = false)
        {
            if (_dedupCache != null && !forceReload)
                return _dedupCache;

            _dedupCache = LoadJsonOrDefault(DedupConfigPath, new PsdUiToolkitDedupConfigData());
            _dedupCache.Sanitize();
            return _dedupCache;
        }

        public static void SaveNineSlice(PsdUiToolkitNineSliceConfigData data)
        {
            data ??= new PsdUiToolkitNineSliceConfigData();
            data.Sanitize();
            SaveJson(NineSliceConfigPath, data);
            _nineSliceCache = data;
        }

        public static void SaveDedup(PsdUiToolkitDedupConfigData data)
        {
            data ??= new PsdUiToolkitDedupConfigData();
            data.Sanitize();
            SaveJson(DedupConfigPath, data);
            _dedupCache = data;
        }

        public static PsdUiToolkitCommonDirectoriesData LoadCommonDirectories(bool forceReload = false)
        {
            if (_commonDirectoriesCache != null && !forceReload)
                return _commonDirectoriesCache;

            _commonDirectoriesCache = LoadJsonOrDefault(CommonDirectoriesConfigPath, new PsdUiToolkitCommonDirectoriesData());
            return _commonDirectoriesCache;
        }

        public static void SaveCommonDirectories(PsdUiToolkitCommonDirectoriesData data)
        {
            data ??= new PsdUiToolkitCommonDirectoriesData();
            SaveJson(CommonDirectoriesConfigPath, data);
            _commonDirectoriesCache = data;
        }

        private static T LoadJsonOrDefault<T>(string path, T fallback) where T : class
        {
            if (!File.Exists(path))
                return fallback;

            try
            {
                T data = JsonUtility.FromJson<T>(File.ReadAllText(path));
                return data ?? fallback;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkitImageExportConfig] Failed to load {Path.GetFileName(path)}: {ex.Message}");
                return fallback;
            }
        }

        private static void SaveJson<T>(string path, T data) where T : class
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PsdUiToolkitImageExportConfig] Failed to save {Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }
}

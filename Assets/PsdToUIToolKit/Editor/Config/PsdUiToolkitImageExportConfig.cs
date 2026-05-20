using System;
using System.IO;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    [Serializable]
    internal sealed class PsdUiToolkitNineSliceConfigData
    {
        public int borderInset = 2;
        public int pixelThreshold = 10;
        public int minCenterCols = 10;
        public int minCenterRows = 10;
        public int minSameZone = 15;
    }

    [Serializable]
    internal sealed class PsdUiToolkitDedupConfigData
    {
        public float maeThreshold = 0.04f;
        public int fingerprintSize = 8;
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
            return _nineSliceCache;
        }

        public static PsdUiToolkitDedupConfigData LoadDedup(bool forceReload = false)
        {
            if (_dedupCache != null && !forceReload)
                return _dedupCache;

            _dedupCache = LoadJsonOrDefault(DedupConfigPath, new PsdUiToolkitDedupConfigData());
            return _dedupCache;
        }

        public static PsdUiToolkitCommonDirectoriesData LoadCommonDirectories(bool forceReload = false)
        {
            if (_commonDirectoriesCache != null && !forceReload)
                return _commonDirectoriesCache;

            _commonDirectoriesCache = LoadJsonOrDefault(CommonDirectoriesConfigPath, new PsdUiToolkitCommonDirectoriesData());
            return _commonDirectoriesCache;
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
    }
}
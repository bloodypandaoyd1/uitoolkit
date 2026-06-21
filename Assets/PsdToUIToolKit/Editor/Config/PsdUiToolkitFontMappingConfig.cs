using System;
using System.Collections.Generic;
using System.IO;
using PsdTools.Layers;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using Object = UnityEngine.Object;

namespace PsdTools.UIToolKit
{
    [Serializable]
    internal sealed class PsdUiToolkitFontMappingEntry
    {
        public string psdFontName = string.Empty;
        public string fontAssetPath = string.Empty;

        public void Sanitize()
        {
            psdFontName = (psdFontName ?? string.Empty).Trim();
            fontAssetPath = fontAssetPath ?? string.Empty;
        }
    }

    [Serializable]
    internal sealed class PsdUiToolkitFontMappingData
    {
        public PsdUiToolkitFontMappingEntry[] entries = Array.Empty<PsdUiToolkitFontMappingEntry>();
    }

    internal sealed class PsdUiToolkitFontMappingLookup
    {
        private readonly Dictionary<string, PsdUiToolkitFontMappingEntry> _entries;
        private readonly HashSet<string> _warnedInvalidMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PsdUiToolkitFontMappingLookup(PsdUiToolkitFontMappingData data)
        {
            _entries = new Dictionary<string, PsdUiToolkitFontMappingEntry>(StringComparer.OrdinalIgnoreCase);
            if (data?.entries == null)
                return;

            foreach (PsdUiToolkitFontMappingEntry entry in data.entries)
            {
                if (entry == null)
                    continue;
                entry.Sanitize();
                if (!string.IsNullOrEmpty(entry.psdFontName) && !_entries.ContainsKey(entry.psdFontName))
                    _entries.Add(entry.psdFontName, entry);
            }
        }

        public string ResolveStyleUri(string psdFontName)
        {
            string key = (psdFontName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key) || !_entries.TryGetValue(key, out PsdUiToolkitFontMappingEntry entry))
                return string.Empty;
            if (string.IsNullOrEmpty(entry.fontAssetPath))
                return string.Empty;

            Object asset = PsdUiToolkitFontMappingConfig.LoadSupportedFontAsset(entry.fontAssetPath);
            if (asset != null)
                return PsdUiToolkitAssetPathUtility.BuildProjectDatabaseUri(asset);

            if (_warnedInvalidMappings.Add(key))
            {
                Debug.LogWarning(
                    $"[PsdUiToolkit Font Mapping] '{key}' points to an invalid Font/FontAsset: {entry.fontAssetPath}. " +
                    "The UI Toolkit default font will be used.");
            }

            return string.Empty;
        }
    }

    internal static class PsdUiToolkitFontMappingConfig
    {
        private static PsdUiToolkitFontMappingData _cache;

        public static string ConfigPath => Path.Combine(
            Application.dataPath,
            "PsdToUIToolKit",
            "EditorConfig",
            "PSD_FontMapping.json");

        public static PsdUiToolkitFontMappingData Load(bool forceReload = false)
        {
            if (_cache != null && !forceReload)
                return _cache;

            if (!File.Exists(ConfigPath))
            {
                _cache = new PsdUiToolkitFontMappingData();
                return _cache;
            }

            try
            {
                _cache = JsonUtility.FromJson<PsdUiToolkitFontMappingData>(File.ReadAllText(ConfigPath))
                    ?? new PsdUiToolkitFontMappingData();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkit Font Mapping] Failed to load config: {ex.Message}");
                _cache = new PsdUiToolkitFontMappingData();
            }

            Sanitize(_cache);
            return _cache;
        }

        public static void Save(PsdUiToolkitFontMappingData data)
        {
            data ??= new PsdUiToolkitFontMappingData();
            Sanitize(data);

            try
            {
                string directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(ConfigPath, JsonUtility.ToJson(data, true));
                _cache = data;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PsdUiToolkit Font Mapping] Failed to save config: {ex.Message}");
            }
        }

        public static PsdUiToolkitFontMappingLookup PrepareForExport(PsdImage psd)
        {
            PsdUiToolkitFontMappingData data = Load(true);
            List<PsdUiToolkitFontMappingEntry> entries = new List<PsdUiToolkitFontMappingEntry>(data.entries ?? Array.Empty<PsdUiToolkitFontMappingEntry>());
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PsdUiToolkitFontMappingEntry entry in entries)
            {
                entry?.Sanitize();
                if (!string.IsNullOrEmpty(entry?.psdFontName))
                    known.Add(entry.psdFontName);
            }

            List<string> missing = new List<string>();
            List<Layer> layers = new List<Layer>();
            PsdUiToolkitConfigStore.CollectLayers(psd?.Root, layers);
            foreach (Layer layer in layers)
            {
                if (layer.Kind != LayerKind.Type)
                    continue;

                string fontName = (((TypeLayer)layer).PsdFontName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(fontName) || !known.Add(fontName))
                    continue;

                entries.Add(new PsdUiToolkitFontMappingEntry { psdFontName = fontName });
                missing.Add(fontName);
            }

            if (missing.Count > 0)
            {
                data.entries = entries.ToArray();
                Save(data);
                Debug.LogWarning(
                    "[PsdUiToolkit Font Mapping] Unmapped PSD fonts were added to the UI Toolkit font mapping config. " +
                    $"The default font is used until assets are assigned: {string.Join(", ", missing)}");
            }

            return new PsdUiToolkitFontMappingLookup(data);
        }

        public static Object LoadSupportedFontAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            Font font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
            if (font != null)
                return font;

            return AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
        }

        public static bool IsSupportedFontAsset(Object asset)
        {
            return asset == null || asset is Font || asset is FontAsset;
        }

        private static void Sanitize(PsdUiToolkitFontMappingData data)
        {
            data.entries ??= Array.Empty<PsdUiToolkitFontMappingEntry>();
            foreach (PsdUiToolkitFontMappingEntry entry in data.entries)
                entry?.Sanitize();
        }
    }
}

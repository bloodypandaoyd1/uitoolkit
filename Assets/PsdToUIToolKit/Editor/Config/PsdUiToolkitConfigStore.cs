using System;
using System.Collections.Generic;
using System.IO;
using PsdTools.Layers;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitConfigStore
    {
        public static string GetExportConfigPath(string psdPath)
        {
            if (string.IsNullOrEmpty(psdPath))
                return string.Empty;

            string configDir = Path.Combine(Application.dataPath, "PsdToUIToolKit", "PSDConfig");
            return Path.Combine(configDir, Path.GetFileNameWithoutExtension(psdPath) + "_uitoolkit_export_config.json");
        }

        public static PsdUiToolkitExportConfigData Load(string psdPath)
        {
            string path = GetExportConfigPath(psdPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new PsdUiToolkitExportConfigData();

            try
            {
                string json = File.ReadAllText(path);
                PsdUiToolkitExportConfigData data = JsonUtility.FromJson<PsdUiToolkitExportConfigData>(json);
                return data ?? new PsdUiToolkitExportConfigData();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkit] Failed to load export config: {ex.Message}");
                return new PsdUiToolkitExportConfigData();
            }
        }

        public static void Save(string psdPath, PsdUiToolkitExportConfigData data)
        {
            string path = GetExportConfigPath(psdPath);
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (data == null)
                    data = new PsdUiToolkitExportConfigData();
                if (data.layers == null)
                    data.layers = Array.Empty<PsdUiToolkitLayerConfig>();

                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PsdUiToolkit] Failed to save export config: {ex.Message}");
            }
        }

        public static PsdUiToolkitExportConfigData LoadAndSync(string psdPath, PsdImage psd, bool saveAfterSync = true)
        {
            PsdUiToolkitExportConfigData data = Load(psdPath);
            data = Synchronize(psd, data);
            if (saveAfterSync)
                Save(psdPath, data);
            return data;
        }

        public static PsdUiToolkitExportConfigData Synchronize(PsdImage psd, PsdUiToolkitExportConfigData data)
        {
            if (psd == null)
                return data ?? new PsdUiToolkitExportConfigData();

            data ??= new PsdUiToolkitExportConfigData();
            Dictionary<int, PsdUiToolkitLayerConfig> existing = BuildLookup(data);
            List<PsdUiToolkitLayerConfig> ordered = new List<PsdUiToolkitLayerConfig>();
            List<Layer> layers = new List<Layer>();
            CollectLayers(psd.Root, layers);

            foreach (Layer layer in layers)
            {
                if (!layer.LayerId.HasValue)
                    continue;

                int layerId = layer.LayerId.Value;
                if (!existing.TryGetValue(layerId, out PsdUiToolkitLayerConfig entry))
                    entry = PsdUiToolkitLayerConfig.CreateDefault(layer);

                if (string.IsNullOrEmpty(entry.name))
                    entry.name = layer.Name ?? string.Empty;

                ordered.Add(entry);
            }

            data.layers = ordered.ToArray();
            return data;
        }

        public static void ApplyToPsd(PsdImage psd, PsdUiToolkitExportConfigData data)
        {
            if (psd == null || data == null)
                return;

            Dictionary<int, PsdUiToolkitLayerConfig> lookup = BuildLookup(data);
            List<Layer> layers = new List<Layer>();
            CollectLayers(psd.Root, layers);

            foreach (Layer layer in layers)
            {
                if (!layer.LayerId.HasValue)
                    continue;
                if (!lookup.TryGetValue(layer.LayerId.Value, out PsdUiToolkitLayerConfig entry))
                    continue;

                if (!string.IsNullOrEmpty(entry.name) && entry.name != layer.Name)
                    layer.Name = entry.name;

                layer.Visible = entry.visible;
            }
        }

        public static Dictionary<int, PsdUiToolkitLayerConfig> BuildLookup(PsdUiToolkitExportConfigData data)
        {
            Dictionary<int, PsdUiToolkitLayerConfig> lookup = new Dictionary<int, PsdUiToolkitLayerConfig>();
            if (data?.layers == null)
                return lookup;

            foreach (PsdUiToolkitLayerConfig entry in data.layers)
            {
                if (entry == null)
                    continue;
                lookup[entry.id] = entry;
            }

            return lookup;
        }

        public static void CollectLayers(Layer root, List<Layer> buffer)
        {
            if (root == null || buffer == null)
                return;

            foreach (Layer child in root.Children)
            {
                buffer.Add(child);
                CollectLayers(child, buffer);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using PsdTools.Layers;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitConfigStore
    {
        [Serializable]
        private sealed class VersionHeader
        {
            public int configVersion;
        }

        [Serializable]
        private sealed class LegacyConfigData
        {
            public int configVersion;
            public LegacyLayerConfig[] layers = Array.Empty<LegacyLayerConfig>();
            public LegacyVirtualGroupConfig[] virtualGroups = Array.Empty<LegacyVirtualGroupConfig>();
        }

        [Serializable]
        private sealed class LegacyLayerConfig
        {
            public int id;
            public string name = "";
            public bool exported = true;
            public bool visible = true;
            public bool merge;
            public bool sliceImage = true;
            public bool participateLocalDedup = true;
            public bool participateCommonDedup = true;
            public bool useCustomNineSliceParams;
            public int nineSliceBorderInset = 2;
            public int nineSlicePixelThreshold = 10;
            public int nineSliceMinCenterCols = 10;
            public int nineSliceMinCenterRows = 10;
            public int nineSliceMinSameZone = 15;
            public bool participateInAutoLayout = true;
            public PsdUiToolkitContainerLayout childrenLayout;
            public PsdUiToolkitItemRole itemRole;
            public PsdUiToolkitMainAxisDistribution mainAxisDistribution;
            public PsdUiToolkitCrossAxisAlignment crossAxisAlignment;
        }

        [Serializable]
        private sealed class LegacyVirtualGroupConfig
        {
            public string id = "";
            public string name = "";
            public int parentLayerId = -1;
            public int[] memberLayerIds = Array.Empty<int>();
            public PsdUiToolkitContainerLayout layout = PsdUiToolkitContainerLayout.Row;
            public PsdUiToolkitMainAxisDistribution mainAxisDistribution;
            public PsdUiToolkitCrossAxisAlignment crossAxisAlignment;
        }

        public static string GetExportConfigPath(string psdPath)
        {
            if (string.IsNullOrEmpty(psdPath))
                return string.Empty;

            string configDir = Path.Combine(Application.dataPath, "PsdToUIToolKit", "PSDConfig");
            return Path.Combine(
                configDir,
                Path.GetFileNameWithoutExtension(psdPath) + "_uitoolkit_export_config.json");
        }

        public static PsdUiToolkitExportConfigData Load(string psdPath)
        {
            string path = GetExportConfigPath(psdPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return MigrateToCurrentVersion(new PsdUiToolkitExportConfigData());

            try
            {
                return DeserializeAndMigrate(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkit] Failed to load export config: {ex.Message}");
                return MigrateToCurrentVersion(new PsdUiToolkitExportConfigData());
            }
        }

        internal static PsdUiToolkitExportConfigData DeserializeAndMigrate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return MigrateToCurrentVersion(new PsdUiToolkitExportConfigData());

            VersionHeader header = JsonUtility.FromJson<VersionHeader>(json) ?? new VersionHeader();
            if (header.configVersion >= PsdUiToolkitExportConfigData.CurrentConfigVersion)
            {
                PsdUiToolkitExportConfigData current =
                    JsonUtility.FromJson<PsdUiToolkitExportConfigData>(json);
                return MigrateToCurrentVersion(current);
            }

            LegacyConfigData legacy = JsonUtility.FromJson<LegacyConfigData>(json)
                ?? new LegacyConfigData();
            return ConvertLegacy(legacy);
        }

        internal static string Serialize(PsdUiToolkitExportConfigData data, bool prettyPrint = true)
        {
            return JsonUtility.ToJson(MigrateToCurrentVersion(data), prettyPrint);
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
                File.WriteAllText(path, Serialize(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PsdUiToolkit] Failed to save export config: {ex.Message}");
            }
        }

        public static PsdUiToolkitExportConfigData LoadAndSync(
            string psdPath,
            PsdImage psd,
            bool saveAfterSync = true)
        {
            PsdUiToolkitExportConfigData data = Synchronize(psd, Load(psdPath));
            if (saveAfterSync)
                Save(psdPath, data);
            return data;
        }

        public static PsdUiToolkitExportConfigData Synchronize(
            PsdImage psd,
            PsdUiToolkitExportConfigData data)
        {
            data = MigrateToCurrentVersion(data);
            if (psd == null)
                return data;

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
                entry.Sanitize();
                if (string.IsNullOrEmpty(entry.name))
                    entry.name = layer.Name ?? string.Empty;
                ordered.Add(entry);
            }

            data.layers = ordered.ToArray();
            return data;
        }

        public static PsdUiToolkitExportConfigData MigrateToCurrentVersion(
            PsdUiToolkitExportConfigData data)
        {
            data ??= new PsdUiToolkitExportConfigData();
            data.layers ??= Array.Empty<PsdUiToolkitLayerConfig>();
            data.virtualGroups ??= Array.Empty<PsdUiToolkitVirtualGroupConfig>();
            data.buttons ??= Array.Empty<PsdUiToolkitButtonSemanticConfig>();
            data.componentDefinitions ??= Array.Empty<PsdUiToolkitComponentDefinitionConfig>();
            data.componentInstances ??= Array.Empty<PsdUiToolkitComponentInstanceConfig>();

            data.configVersion = PsdUiToolkitExportConfigData.CurrentConfigVersion;
            for (int i = 0; i < data.layers.Length; i++)
                data.layers[i]?.Sanitize();
            for (int i = 0; i < data.virtualGroups.Length; i++)
                data.virtualGroups[i]?.Sanitize();
            for (int i = 0; i < data.buttons.Length; i++)
                data.buttons[i]?.Sanitize();
            for (int i = 0; i < data.componentDefinitions.Length; i++)
                data.componentDefinitions[i]?.Sanitize();
            for (int i = 0; i < data.componentInstances.Length; i++)
                data.componentInstances[i]?.Sanitize();
            return data;
        }

        private static PsdUiToolkitExportConfigData ConvertLegacy(LegacyConfigData legacy)
        {
            legacy ??= new LegacyConfigData();
            LegacyLayerConfig[] oldLayers = legacy.layers ?? Array.Empty<LegacyLayerConfig>();
            PsdUiToolkitLayerConfig[] layers = new PsdUiToolkitLayerConfig[oldLayers.Length];
            for (int i = 0; i < oldLayers.Length; i++)
            {
                LegacyLayerConfig old = oldLayers[i];
                if (old == null)
                    continue;

                bool beforeV2 = legacy.configVersion < 2;
                bool beforeV3 = legacy.configVersion < 3;
                layers[i] = new PsdUiToolkitLayerConfig
                {
                    id = old.id,
                    name = old.name ?? string.Empty,
                    exported = old.exported,
                    visible = old.visible,
                    merge = old.merge,
                    sliceImage = old.sliceImage,
                    participateLocalDedup = old.participateLocalDedup,
                    participateCommonDedup = old.participateCommonDedup,
                    useCustomNineSliceParams = old.useCustomNineSliceParams,
                    nineSliceBorderInset = old.nineSliceBorderInset,
                    nineSlicePixelThreshold = old.nineSlicePixelThreshold,
                    nineSliceMinCenterCols = old.nineSliceMinCenterCols,
                    nineSliceMinCenterRows = old.nineSliceMinCenterRows,
                    nineSliceMinSameZone = old.nineSliceMinSameZone,
                    childrenLayout = beforeV2
                        ? PsdUiToolkitContainerLayout.Absolute
                        : NormalizeLegacyLayout(old.childrenLayout),
                    itemRole = beforeV2
                        ? (old.participateInAutoLayout
                            ? PsdUiToolkitItemRole.FollowParent
                            : PsdUiToolkitItemRole.KeepAbsolute)
                        : old.itemRole,
                    mainAxisDistribution = beforeV3
                        ? PsdUiToolkitMainAxisDistribution.PreservePsd
                        : old.mainAxisDistribution,
                    crossAxisAlignment = beforeV3
                        ? PsdUiToolkitCrossAxisAlignment.PreservePsd
                        : old.crossAxisAlignment,
                    wrapMode = PsdUiToolkitWrapMode.NoWrap,
                    multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd,
                };
            }

            LegacyVirtualGroupConfig[] oldGroups = legacy.configVersion < 2
                ? Array.Empty<LegacyVirtualGroupConfig>()
                : legacy.virtualGroups ?? Array.Empty<LegacyVirtualGroupConfig>();
            PsdUiToolkitVirtualGroupConfig[] groups =
                new PsdUiToolkitVirtualGroupConfig[oldGroups.Length];
            for (int i = 0; i < oldGroups.Length; i++)
            {
                LegacyVirtualGroupConfig old = oldGroups[i];
                if (old == null)
                    continue;

                int[] oldMembers = old.memberLayerIds ?? Array.Empty<int>();
                PsdUiToolkitNodeReference[] members =
                    new PsdUiToolkitNodeReference[oldMembers.Length];
                for (int memberIndex = 0; memberIndex < oldMembers.Length; memberIndex++)
                    members[memberIndex] = PsdUiToolkitNodeReference.Layer(oldMembers[memberIndex]);

                groups[i] = new PsdUiToolkitVirtualGroupConfig
                {
                    id = old.id ?? string.Empty,
                    name = old.name ?? string.Empty,
                    hostParentLayerId = old.parentLayerId,
                    members = members,
                    layout = old.layout == PsdUiToolkitContainerLayout.Column
                        ? PsdUiToolkitContainerLayout.Column
                        : PsdUiToolkitContainerLayout.Row,
                    wrapMode = PsdUiToolkitWrapMode.NoWrap,
                    multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd,
                    mainAxisDistribution = legacy.configVersion < 3
                        ? PsdUiToolkitMainAxisDistribution.PreservePsd
                        : old.mainAxisDistribution,
                    crossAxisAlignment = legacy.configVersion < 3
                        ? PsdUiToolkitCrossAxisAlignment.PreservePsd
                        : old.crossAxisAlignment,
                };
            }

            return MigrateToCurrentVersion(new PsdUiToolkitExportConfigData
            {
                configVersion = PsdUiToolkitExportConfigData.CurrentConfigVersion,
                layers = layers,
                virtualGroups = groups,
            });
        }

        private static PsdUiToolkitContainerLayout NormalizeLegacyLayout(
            PsdUiToolkitContainerLayout layout)
        {
            return layout == PsdUiToolkitContainerLayout.Row
                || layout == PsdUiToolkitContainerLayout.Column
                || layout == PsdUiToolkitContainerLayout.Absolute
                    ? layout
                    : PsdUiToolkitContainerLayout.Absolute;
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
                if (!layer.LayerId.HasValue
                    || !lookup.TryGetValue(layer.LayerId.Value, out PsdUiToolkitLayerConfig entry))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.name) && entry.name != layer.Name)
                    layer.Name = entry.name;
                layer.Visible = entry.visible;
            }
        }

        public static Dictionary<int, PsdUiToolkitLayerConfig> BuildLookup(
            PsdUiToolkitExportConfigData data)
        {
            Dictionary<int, PsdUiToolkitLayerConfig> lookup =
                new Dictionary<int, PsdUiToolkitLayerConfig>();
            if (data?.layers == null)
                return lookup;

            foreach (PsdUiToolkitLayerConfig entry in data.layers)
            {
                if (entry == null)
                    continue;
                entry.Sanitize();
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

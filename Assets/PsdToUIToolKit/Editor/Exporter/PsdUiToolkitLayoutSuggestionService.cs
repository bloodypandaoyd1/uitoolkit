using System;
using System.Collections.Generic;
using PsdTools.Layers;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal sealed class PsdUiToolkitLayoutSuggestion
    {
        public int TargetLayerId = -1;
        public int ParentLayerId = -1;
        public int[] MemberLayerIds = Array.Empty<int>();
        public PsdUiToolkitContainerLayout Layout;
        public string Summary = "";

        public bool IsVirtualGroup => TargetLayerId < 0 && MemberLayerIds.Length >= 2;
    }

    internal static class PsdUiToolkitLayoutSuggestionService
    {
        public static List<PsdUiToolkitLayoutSuggestion> Analyze(
            PsdImage psd,
            PsdUiToolkitExportConfigData sourceConfig,
            string rootName)
        {
            List<PsdUiToolkitLayoutSuggestion> suggestions = new List<PsdUiToolkitLayoutSuggestion>();
            if (psd == null)
                return suggestions;

            PsdUiToolkitExportConfigData analysisConfig = CloneConfig(sourceConfig);
            analysisConfig.autoLayout = PsdUiToolkitAutoLayoutGlobalConfig.Default;
            analysisConfig.autoLayout.enabled = true;
            analysisConfig.autoLayout.rebuildLayoutTree = true;
            analysisConfig.autoLayout.detectionMode = PsdUiToolkitAutoLayoutMode.Conservative;
            analysisConfig.autoLayout.allowVirtualContainers = true;
            analysisConfig.autoLayout.detectBackgroundContainers = true;
            for (int i = 0; i < analysisConfig.layers.Length; i++)
            {
                if (analysisConfig.layers[i] != null)
                    analysisConfig.layers[i].participateInAutoLayout = true;
            }

            PsdUiToolkitLayerConfigMap analysisMap = new PsdUiToolkitLayerConfigMap(analysisConfig);
            PsdUiToolkitLayoutTree tree = PsdUiToolkitLayoutTreeRebuilder.AnalyzeForInspector(psd, analysisMap, rootName);
            Dictionary<int, int> parentByLayerId = BuildParentLookup(psd);
            Dictionary<int, PsdUiToolkitLayerConfig> configByLayerId = PsdUiToolkitConfigStore.BuildLookup(analysisConfig);
            HashSet<int> groupedLayerIds = BuildGroupedLayerIds(sourceConfig);
            CollectSuggestions(tree.Children, parentByLayerId, configByLayerId, groupedLayerIds, suggestions);
            return suggestions;
        }

        private static HashSet<int> BuildGroupedLayerIds(PsdUiToolkitExportConfigData config)
        {
            HashSet<int> result = new HashSet<int>();
            PsdUiToolkitVirtualGroupConfig[] groups = config?.virtualGroups ?? Array.Empty<PsdUiToolkitVirtualGroupConfig>();
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i]?.memberLayerIds == null)
                    continue;
                for (int j = 0; j < groups[i].memberLayerIds.Length; j++)
                    result.Add(groups[i].memberLayerIds[j]);
            }

            return result;
        }

        private static PsdUiToolkitExportConfigData CloneConfig(PsdUiToolkitExportConfigData source)
        {
            if (source == null)
                return PsdUiToolkitConfigStore.MigrateToCurrentVersion(new PsdUiToolkitExportConfigData());

            string json = JsonUtility.ToJson(source);
            PsdUiToolkitExportConfigData clone = JsonUtility.FromJson<PsdUiToolkitExportConfigData>(json);
            return PsdUiToolkitConfigStore.MigrateToCurrentVersion(clone);
        }

        private static Dictionary<int, int> BuildParentLookup(PsdImage psd)
        {
            Dictionary<int, int> lookup = new Dictionary<int, int>();
            CollectParents(psd.Children, -1, lookup);
            return lookup;
        }

        private static void CollectParents(
            IEnumerable<Layer> children,
            int parentLayerId,
            Dictionary<int, int> lookup)
        {
            foreach (Layer child in children)
            {
                if (child?.LayerId == null)
                    continue;

                lookup[child.LayerId.Value] = parentLayerId;
                CollectParents(child.Children, child.LayerId.Value, lookup);
            }
        }

        private static void CollectSuggestions(
            List<PsdUiToolkitLayoutNode> nodes,
            Dictionary<int, int> parentByLayerId,
            Dictionary<int, PsdUiToolkitLayerConfig> configByLayerId,
            HashSet<int> groupedLayerIds,
            List<PsdUiToolkitLayoutSuggestion> suggestions)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                PsdUiToolkitLayoutNode node = nodes[i];
                PsdUiToolkitContainerLayout suggestedLayout = ToContainerLayout(node.LayoutType);
                if (node.SourceLayer?.LayerId != null
                    && node.SourceLayer.IsGroup
                    && suggestedLayout != PsdUiToolkitContainerLayout.Unspecified
                    && (!configByLayerId.TryGetValue(node.SourceLayer.LayerId.Value, out PsdUiToolkitLayerConfig config)
                        || (!config.merge && config.childrenLayout == PsdUiToolkitContainerLayout.Unspecified)))
                {
                    suggestions.Add(new PsdUiToolkitLayoutSuggestion
                    {
                        TargetLayerId = node.SourceLayer.LayerId.Value,
                        ParentLayerId = parentByLayerId.TryGetValue(node.SourceLayer.LayerId.Value, out int parentId) ? parentId : -1,
                        Layout = suggestedLayout,
                        Summary = node.AnalysisSummary,
                    });
                }
                else if (node.IsSynthetic && suggestedLayout != PsdUiToolkitContainerLayout.Unspecified)
                {
                    TryAddVirtualSuggestion(node, suggestedLayout, parentByLayerId, groupedLayerIds, suggestions);
                }

                CollectSuggestions(node.Children, parentByLayerId, configByLayerId, groupedLayerIds, suggestions);
            }
        }

        private static void TryAddVirtualSuggestion(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitContainerLayout layout,
            Dictionary<int, int> parentByLayerId,
            HashSet<int> groupedLayerIds,
            List<PsdUiToolkitLayoutSuggestion> suggestions)
        {
            List<int> memberIds = new List<int>();
            int parentLayerId = int.MinValue;
            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = node.Children[i];
                if (child.IsSynthetic || child.SourceLayer?.LayerId == null)
                    return;

                int layerId = child.SourceLayer.LayerId.Value;
                if (groupedLayerIds.Contains(layerId))
                    return;
                if (!parentByLayerId.TryGetValue(layerId, out int resolvedParentId))
                    return;
                if (parentLayerId == int.MinValue)
                    parentLayerId = resolvedParentId;
                else if (parentLayerId != resolvedParentId)
                    return;

                memberIds.Add(layerId);
            }

            if (memberIds.Count < 2)
                return;

            suggestions.Add(new PsdUiToolkitLayoutSuggestion
            {
                ParentLayerId = parentLayerId == int.MinValue ? -1 : parentLayerId,
                MemberLayerIds = memberIds.ToArray(),
                Layout = layout,
                Summary = node.AnalysisSummary,
            });
        }

        private static PsdUiToolkitContainerLayout ToContainerLayout(PsdUiToolkitLayoutType layoutType)
        {
            switch (layoutType)
            {
                case PsdUiToolkitLayoutType.Row:
                    return PsdUiToolkitContainerLayout.Row;
                case PsdUiToolkitLayoutType.Column:
                    return PsdUiToolkitContainerLayout.Column;
                default:
                    return PsdUiToolkitContainerLayout.Unspecified;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitManualLayoutBuilder
    {
        public static PsdUiToolkitLayoutTree Build(
            PsdImage psd,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            string rootName)
        {
            if (rasterResult == null)
                throw new ArgumentNullException(nameof(rasterResult));

            return BuildInternal(psd, configMap, rasterResult, rootName, false);
        }

        public static PsdUiToolkitLayoutTree BuildForInspector(
            PsdImage psd,
            PsdUiToolkitLayerConfigMap configMap,
            string rootName)
        {
            return BuildInternal(psd, configMap, null, rootName, true);
        }

        private static PsdUiToolkitLayoutTree BuildInternal(
            PsdImage psd,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            string rootName,
            bool inspectorMode)
        {
            if (psd == null)
                throw new ArgumentNullException(nameof(psd));
            if (configMap == null)
                throw new ArgumentNullException(nameof(configMap));

            List<string> warnings = new List<string>();
            List<PsdUiToolkitLayoutDiagnostic> diagnostics =
                new List<PsdUiToolkitLayoutDiagnostic>();
            HashSet<PsdUiToolkitVirtualGroupConfig> visitedGroups = new HashSet<PsdUiToolkitVirtualGroupConfig>();
            List<PsdUiToolkitLayoutNode> children = BuildChildren(
                psd.Children,
                -1,
                configMap,
                rasterResult,
                inspectorMode,
                warnings,
                diagnostics,
                visitedGroups);

            PsdUiToolkitVirtualGroupConfig[] configuredGroups = configMap.GetVirtualGroups();
            for (int i = 0; i < configuredGroups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = configuredGroups[i];
                if (group != null && !visitedGroups.Contains(group))
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "MissingVirtualGroupParent",
                        $"Layout group '{GetGroupName(group)}' has a missing or non-renderable parent and was ignored.",
                        virtualGroupId: group.id);
                }
            }

            return new PsdUiToolkitLayoutTree(
                rootName,
                psd.Width,
                psd.Height,
                true,
                children,
                warnings,
                diagnostics);
        }

        private static List<PsdUiToolkitLayoutNode> BuildChildren(
            IEnumerable<Layer> sourceChildren,
            int parentLayerId,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            bool inspectorMode,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            HashSet<PsdUiToolkitVirtualGroupConfig> visitedGroups)
        {
            List<PsdUiToolkitLayoutNode> children = new List<PsdUiToolkitLayoutNode>();
            int originalIndex = 0;
            foreach (Layer child in sourceChildren)
            {
                PsdUiToolkitLayoutNode node = BuildLayerNode(
                    child,
                    originalIndex++,
                    configMap,
                    rasterResult,
                    inspectorMode,
                    warnings,
                    diagnostics,
                    visitedGroups);
                if (node != null)
                    children.Add(node);
            }

            ApplyVirtualGroups(
                children,
                parentLayerId,
                configMap,
                warnings,
                diagnostics,
                visitedGroups);
            return children;
        }

        private static PsdUiToolkitLayoutNode BuildLayerNode(
            Layer layer,
            int originalIndex,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            bool inspectorMode,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            HashSet<PsdUiToolkitVirtualGroupConfig> visitedGroups)
        {
            if (layer?.LayerId == null)
                return null;
            if (!configMap.IsExported(layer))
                return null;
            if (rasterResult != null && rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
                return null;

            PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(layer);
            bool renderAsLeaf = ShouldRenderAsLeaf(layer, configMap, rasterResult, inspectorMode);
            List<PsdUiToolkitLayoutNode> children = renderAsLeaf
                ? new List<PsdUiToolkitLayoutNode>()
                : BuildChildren(
                    layer.Children,
                    layer.LayerId.Value,
                    configMap,
                    rasterResult,
                    inspectorMode,
                    warnings,
                    diagnostics,
                    visitedGroups);

            PsdUiToolkitContainerLayout layoutIntent = configMap.GetChildrenLayout(layer);
            PsdUiToolkitLayoutType layoutType = ResolveLayoutType(layoutIntent, renderAsLeaf, children.Count);
            if (layoutType == PsdUiToolkitLayoutType.Row || layoutType == PsdUiToolkitLayoutType.Column)
            {
                OrderChildrenForFlow(children, layoutType);
                AddOverlapWarning(
                    children,
                    layoutType,
                    layer.Name,
                    warnings,
                    diagnostics,
                    layer.LayerId ?? -1,
                    null);
            }

            string summary = layoutIntent == PsdUiToolkitContainerLayout.Unspecified
                ? "No layout intent selected; keep absolute export."
                : $"User layout intent: {layoutIntent}.";

            return new PsdUiToolkitLayoutNode(
                layer,
                bounds,
                renderAsLeaf,
                layoutType,
                layoutType == PsdUiToolkitLayoutType.Absolute ? 0f : 1f,
                summary,
                originalIndex,
                children,
                itemRole: configMap.GetItemRole(layer),
                mainAxisDistribution: configMap.GetMainAxisDistribution(layer),
                crossAxisAlignment: configMap.GetCrossAxisAlignment(layer));
        }

        private static bool ShouldRenderAsLeaf(
            Layer layer,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            bool inspectorMode)
        {
            if (layer == null || !layer.IsGroup)
                return true;

            if (!inspectorMode && rasterResult != null && layer.LayerId.HasValue)
            {
                int layerId = layer.LayerId.Value;
                return rasterResult.AssetsByLayerId.ContainsKey(layerId)
                    || rasterResult.CompositeLeafLayerIds.Contains(layerId);
            }

            return configMap.IsMergeExport(layer);
        }

        private static PsdUiToolkitLayoutType ResolveLayoutType(
            PsdUiToolkitContainerLayout layout,
            bool renderAsLeaf,
            int childCount)
        {
            if (renderAsLeaf || childCount == 0)
                return PsdUiToolkitLayoutType.Absolute;

            switch (layout)
            {
                case PsdUiToolkitContainerLayout.Row:
                    return PsdUiToolkitLayoutType.Row;
                case PsdUiToolkitContainerLayout.Column:
                    return PsdUiToolkitLayoutType.Column;
                default:
                    return PsdUiToolkitLayoutType.Absolute;
            }
        }

        private static void ApplyVirtualGroups(
            List<PsdUiToolkitLayoutNode> siblings,
            int parentLayerId,
            PsdUiToolkitLayerConfigMap configMap,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            HashSet<PsdUiToolkitVirtualGroupConfig> visitedGroups)
        {
            PsdUiToolkitVirtualGroupConfig[] groups = configMap.GetVirtualGroups();
            HashSet<int> claimedLayerIds = new HashSet<int>();
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[groupIndex];
                if (group == null || group.parentLayerId != parentLayerId)
                    continue;

                visitedGroups.Add(group);
                group.Sanitize();
                if (group.memberLayerIds.Length < 2)
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "TooFewVirtualGroupMembers",
                        $"Layout group '{GetGroupName(group)}' needs at least two members and was ignored.",
                        virtualGroupId: group.id);
                    continue;
                }

                Dictionary<int, PsdUiToolkitLayoutNode> availableById = BuildLayerNodeLookup(siblings);
                List<PsdUiToolkitLayoutNode> members = new List<PsdUiToolkitLayoutNode>();
                bool invalid = false;
                for (int i = 0; i < group.memberLayerIds.Length; i++)
                {
                    int memberId = group.memberLayerIds[i];
                    if (claimedLayerIds.Contains(memberId) || !availableById.TryGetValue(memberId, out PsdUiToolkitLayoutNode member))
                    {
                        invalid = true;
                        break;
                    }

                    members.Add(member);
                }

                if (invalid)
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "InvalidVirtualGroupMembers",
                        $"Layout group '{GetGroupName(group)}' contains missing, moved, or reused members and was ignored.",
                        virtualGroupId: group.id);
                    continue;
                }

                PsdUiToolkitLayoutType layoutType = group.layout == PsdUiToolkitContainerLayout.Column
                    ? PsdUiToolkitLayoutType.Column
                    : PsdUiToolkitLayoutType.Row;
                OrderChildrenForFlow(members, layoutType);
                AddOverlapWarning(
                    members,
                    layoutType,
                    GetGroupName(group),
                    warnings,
                    diagnostics,
                    -1,
                    group.id);

                int insertionIndex = siblings.Count;
                int lastMemberIndex = -1;
                int originalIndex = int.MaxValue;
                for (int i = 0; i < members.Count; i++)
                {
                    int siblingIndex = siblings.IndexOf(members[i]);
                    insertionIndex = Math.Min(insertionIndex, siblingIndex);
                    lastMemberIndex = Math.Max(lastMemberIndex, siblingIndex);
                    originalIndex = Math.Min(originalIndex, members[i].OriginalIndex);
                }
                if (lastMemberIndex - insertionIndex + 1 != members.Count)
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "NonContiguousVirtualGroupMembers",
                        $"Layout group '{GetGroupName(group)}' contains non-contiguous PSD siblings; their relative draw order may change.",
                        virtualGroupId: group.id);
                }

                for (int i = 0; i < members.Count; i++)
                {
                    siblings.Remove(members[i]);
                    if (members[i].SourceLayer?.LayerId != null)
                        claimedLayerIds.Add(members[i].SourceLayer.LayerId.Value);
                }

                PsdUiToolkitLayoutNode virtualNode = new PsdUiToolkitLayoutNode(
                    null,
                    ComputeUnionBounds(members),
                    false,
                    layoutType,
                    1f,
                    $"User-created {layoutType} layout group.",
                    originalIndex == int.MaxValue ? groupIndex : originalIndex,
                    members,
                    GetGroupName(group),
                    true,
                    "User-created virtual layout group.",
                    PsdUiToolkitItemRole.FollowParent,
                    group.id,
                    group.mainAxisDistribution,
                    group.crossAxisAlignment);

                siblings.Insert(Math.Max(0, Math.Min(insertionIndex, siblings.Count)), virtualNode);
            }
        }

        private static Dictionary<int, PsdUiToolkitLayoutNode> BuildLayerNodeLookup(List<PsdUiToolkitLayoutNode> nodes)
        {
            Dictionary<int, PsdUiToolkitLayoutNode> lookup = new Dictionary<int, PsdUiToolkitLayoutNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                Layer layer = nodes[i]?.SourceLayer;
                if (layer?.LayerId != null)
                    lookup[layer.LayerId.Value] = nodes[i];
            }

            return lookup;
        }

        private static string GetGroupName(PsdUiToolkitVirtualGroupConfig group)
        {
            if (!string.IsNullOrWhiteSpace(group?.name))
                return group.name.Trim();
            if (!string.IsNullOrWhiteSpace(group?.id))
                return $"Layout_{group.id.Substring(0, Math.Min(8, group.id.Length))}";
            return "Layout_Group";
        }

        private static PsdUiToolkitLayerBounds ComputeUnionBounds(List<PsdUiToolkitLayoutNode> nodes)
        {
            int minLeft = int.MaxValue;
            int minTop = int.MaxValue;
            int maxRight = int.MinValue;
            int maxBottom = int.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                PsdUiToolkitLayerBounds bounds = nodes[i].Bounds;
                minLeft = Math.Min(minLeft, bounds.Left);
                minTop = Math.Min(minTop, bounds.Top);
                maxRight = Math.Max(maxRight, bounds.Left + bounds.Width);
                maxBottom = Math.Max(maxBottom, bounds.Top + bounds.Height);
            }

            if (minLeft == int.MaxValue)
                return new PsdUiToolkitLayerBounds(0, 0, 0, 0);

            return new PsdUiToolkitLayerBounds(
                minLeft,
                minTop,
                Math.Max(0, maxRight - minLeft),
                Math.Max(0, maxBottom - minTop));
        }

        private static void OrderChildrenForFlow(
            List<PsdUiToolkitLayoutNode> children,
            PsdUiToolkitLayoutType layoutType)
        {
            if (children.Count <= 1)
                return;

            List<PsdUiToolkitLayoutNode> backgrounds = new List<PsdUiToolkitLayoutNode>();
            List<PsdUiToolkitLayoutNode> flow = new List<PsdUiToolkitLayoutNode>();
            List<PsdUiToolkitLayoutNode> overlays = new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < children.Count; i++)
            {
                switch (children[i].ItemRole)
                {
                    case PsdUiToolkitItemRole.Background:
                        backgrounds.Add(children[i]);
                        break;
                    case PsdUiToolkitItemRole.KeepAbsolute:
                        overlays.Add(children[i]);
                        break;
                    default:
                        flow.Add(children[i]);
                        break;
                }
            }

            backgrounds.Sort(CompareByOriginalIndex);
            overlays.Sort(CompareByOriginalIndex);
            flow.Sort(layoutType == PsdUiToolkitLayoutType.Column
                ? (Comparison<PsdUiToolkitLayoutNode>)CompareByTopThenLeft
                : CompareByLeftThenTop);

            children.Clear();
            children.AddRange(backgrounds);
            children.AddRange(flow);
            children.AddRange(overlays);
        }

        private static void AddOverlapWarning(
            List<PsdUiToolkitLayoutNode> children,
            PsdUiToolkitLayoutType layoutType,
            string containerName,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            int layerId,
            string virtualGroupId)
        {
            PsdUiToolkitLayoutNode previous = null;
            for (int i = 0; i < children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = children[i];
                if (child.ItemRole != PsdUiToolkitItemRole.FollowParent)
                    continue;

                if (previous != null)
                {
                    int gap = layoutType == PsdUiToolkitLayoutType.Row
                        ? child.Bounds.Left - (previous.Bounds.Left + previous.Bounds.Width)
                        : child.Bounds.Top - (previous.Bounds.Top + previous.Bounds.Height);
                    if (gap < 0)
                    {
                        AddWarning(
                            warnings,
                            diagnostics,
                            "OverlappingFlowItems",
                            $"Layout '{containerName}' contains overlapping flow items; the generated gap was clamped to 0.",
                            layerId,
                            virtualGroupId);
                        return;
                    }
                }

                previous = child;
            }
        }

        private static void AddWarning(
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            string code,
            string message,
            int layerId = -1,
            string virtualGroupId = null)
        {
            warnings.Add(message);
            diagnostics.Add(new PsdUiToolkitLayoutDiagnostic(
                code,
                message,
                layerId,
                virtualGroupId));
        }

        private static int CompareByOriginalIndex(PsdUiToolkitLayoutNode left, PsdUiToolkitLayoutNode right)
        {
            return left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private static int CompareByLeftThenTop(PsdUiToolkitLayoutNode left, PsdUiToolkitLayoutNode right)
        {
            int compare = left.Bounds.Left.CompareTo(right.Bounds.Left);
            return compare != 0 ? compare : left.Bounds.Top.CompareTo(right.Bounds.Top);
        }

        private static int CompareByTopThenLeft(PsdUiToolkitLayoutNode left, PsdUiToolkitLayoutNode right)
        {
            int compare = left.Bounds.Top.CompareTo(right.Bounds.Top);
            return compare != 0 ? compare : left.Bounds.Left.CompareTo(right.Bounds.Left);
        }
    }
}

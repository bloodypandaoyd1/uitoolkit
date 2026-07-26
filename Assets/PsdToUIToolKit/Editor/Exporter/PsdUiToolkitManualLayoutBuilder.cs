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
            List<PsdUiToolkitLayoutDiagnostic> diagnostics = new List<PsdUiToolkitLayoutDiagnostic>();
            HashSet<string> visitedGroupIds = new HashSet<string>(StringComparer.Ordinal);
            List<PsdUiToolkitLayoutNode> children = BuildChildren(
                psd.Children,
                -1,
                configMap,
                rasterResult,
                inspectorMode,
                warnings,
                diagnostics,
                visitedGroupIds);
            ValidateSemanticReferences(
                children,
                configMap,
                warnings,
                diagnostics);

            PsdUiToolkitVirtualGroupConfig[] groups = configMap.GetVirtualGroups();
            for (int i = 0; i < groups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[i];
                if (group != null
                    && !string.IsNullOrEmpty(group.id)
                    && !visitedGroupIds.Contains(group.id))
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "MissingVirtualGroupParent",
                        $"Layout group '{GetGroupName(group)}' has a missing or non-renderable PSD host and was ignored.",
                        virtualGroupId: group.id);
                }
            }

            return new PsdUiToolkitLayoutTree(
                rootName,
                psd.Width,
                psd.Height,
                children,
                warnings,
                diagnostics);
        }

        private static void ValidateSemanticReferences(
            List<PsdUiToolkitLayoutNode> roots,
            PsdUiToolkitLayerConfigMap configMap,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics)
        {
            Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitLayoutNode> nodes =
                new Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitLayoutNode>();
            CollectNodes(roots, nodes);
            PsdUiToolkitButtonSemanticConfig[] buttons = configMap.GetButtons();
            for (int i = 0; i < buttons.Length; i++)
            {
                PsdUiToolkitButtonSemanticConfig button = buttons[i];
                if (button == null || !button.owner.IsValid)
                    continue;
                bool valid = nodes.TryGetValue(
                        button.owner,
                        out PsdUiToolkitLayoutNode owner)
                    && button.TryGetState(
                        PsdUiToolkitButtonVisualState.Normal,
                        out PsdUiToolkitNodeReference normal)
                    && IsDescendant(owner, normal);
                if (!valid)
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "InvalidButtonNormalState",
                        "A Button semantic has no valid Normal descendant and will export as a regular container.",
                        button.owner.kind == PsdUiToolkitNodeReferenceKind.Layer
                            ? button.owner.layerId
                            : -1,
                        button.owner.kind
                            == PsdUiToolkitNodeReferenceKind.VirtualGroup
                                ? button.owner.virtualGroupId
                                : null);
                }
            }

        }

        private static void CollectNodes(
            IEnumerable<PsdUiToolkitLayoutNode> source,
            Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitLayoutNode> nodes)
        {
            if (source == null)
                return;
            foreach (PsdUiToolkitLayoutNode node in source)
            {
                if (node == null)
                    continue;
                if (node.Reference.IsValid && !nodes.ContainsKey(node.Reference))
                    nodes.Add(node.Reference, node);
                CollectNodes(node.Children, nodes);
            }
        }

        private static bool IsDescendant(
            PsdUiToolkitLayoutNode root,
            PsdUiToolkitNodeReference reference)
        {
            if (root == null)
                return false;
            for (int i = 0; i < root.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = root.Children[i];
                if (child.Reference.Equals(reference)
                    || IsDescendant(child, reference))
                {
                    return true;
                }
            }
            return false;
        }

        private static List<PsdUiToolkitLayoutNode> BuildChildren(
            IEnumerable<Layer> sourceChildren,
            int parentLayerId,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            bool inspectorMode,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            HashSet<string> visitedGroupIds)
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
                    visitedGroupIds);
                if (node != null)
                    children.Add(node);
            }

            return ApplyVirtualGroups(
                children,
                parentLayerId,
                configMap,
                warnings,
                diagnostics,
                visitedGroupIds);
        }

        private static PsdUiToolkitLayoutNode BuildLayerNode(
            Layer layer,
            int originalIndex,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            bool inspectorMode,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            HashSet<string> visitedGroupIds)
        {
            if (layer?.LayerId == null || !configMap.IsExported(layer))
                return null;
            if (rasterResult != null
                && rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
            {
                return null;
            }

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
                    visitedGroupIds);

            PsdUiToolkitLayoutType layoutType = ResolveLayoutType(
                configMap.GetChildrenLayout(layer),
                renderAsLeaf,
                children.Count);
            PsdUiToolkitWrapMode wrapMode = configMap.GetWrapMode(layer);
            if (layoutType == PsdUiToolkitLayoutType.Row
                || layoutType == PsdUiToolkitLayoutType.Column)
            {
                OrderChildrenForFlow(children, layoutType, wrapMode);
                AddOverlapWarning(
                    children,
                    layoutType,
                    wrapMode,
                    layer.Name,
                    warnings,
                    diagnostics,
                    layer.LayerId ?? -1,
                    null);
            }

            return new PsdUiToolkitLayoutNode(
                layer,
                bounds,
                renderAsLeaf,
                layoutType,
                originalIndex,
                children,
                itemRole: configMap.GetItemRole(layer),
                mainAxisDistribution: configMap.GetMainAxisDistribution(layer),
                crossAxisAlignment: configMap.GetCrossAxisAlignment(layer),
                wrapMode: wrapMode,
                multiLineDistribution: configMap.GetMultiLineDistribution(layer));
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
            if (layout == PsdUiToolkitContainerLayout.Row)
                return PsdUiToolkitLayoutType.Row;
            if (layout == PsdUiToolkitContainerLayout.Column)
                return PsdUiToolkitLayoutType.Column;
            return PsdUiToolkitLayoutType.Absolute;
        }

        private static List<PsdUiToolkitLayoutNode> ApplyVirtualGroups(
            List<PsdUiToolkitLayoutNode> siblings,
            int hostParentLayerId,
            PsdUiToolkitLayerConfigMap configMap,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            HashSet<string> visitedGroupIds)
        {
            PsdUiToolkitVirtualGroupConfig[] configured = configMap.GetVirtualGroups();
            List<PsdUiToolkitVirtualGroupConfig> hostGroups = new List<PsdUiToolkitVirtualGroupConfig>();
            Dictionary<string, PsdUiToolkitVirtualGroupConfig> groupById =
                new Dictionary<string, PsdUiToolkitVirtualGroupConfig>(StringComparer.Ordinal);
            for (int i = 0; i < configured.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = configured[i];
                if (group == null || group.hostParentLayerId != hostParentLayerId)
                    continue;

                group.Sanitize();
                if (string.IsNullOrEmpty(group.id) || groupById.ContainsKey(group.id))
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "DuplicateVirtualGroupId",
                        $"Layout group '{GetGroupName(group)}' has an empty or duplicate ID and was ignored.",
                        virtualGroupId: group.id);
                    continue;
                }

                groupById.Add(group.id, group);
                hostGroups.Add(group);
                visitedGroupIds.Add(group.id);
            }

            if (hostGroups.Count == 0)
                return siblings;

            Dictionary<int, PsdUiToolkitLayoutNode> layerById =
                new Dictionary<int, PsdUiToolkitLayoutNode>();
            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i]?.SourceLayer?.LayerId != null)
                    layerById[siblings[i].SourceLayer.LayerId.Value] = siblings[i];
            }

            Dictionary<string, string> ownerByMemberKey =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int groupIndex = 0; groupIndex < hostGroups.Count; groupIndex++)
            {
                PsdUiToolkitVirtualGroupConfig group = hostGroups[groupIndex];
                for (int memberIndex = 0; memberIndex < group.members.Length; memberIndex++)
                {
                    PsdUiToolkitNodeReference member = group.members[memberIndex];
                    string key = member.StableKey;
                    if (!ownerByMemberKey.ContainsKey(key))
                    {
                        ownerByMemberKey[key] = group.id;
                    }
                    else
                    {
                        AddWarning(
                            warnings,
                            diagnostics,
                            "DuplicateVirtualGroupOwnership",
                            $"Member '{key}' is referenced by more than one layout group; the first owner was kept.",
                            virtualGroupId: group.id);
                    }
                }
            }

            HashSet<int> claimedLayers = new HashSet<int>();
            HashSet<string> builtGroups = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            List<PsdUiToolkitLayoutNode> rootGroups = new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < hostGroups.Count; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = hostGroups[i];
                if (ownerByMemberKey.ContainsKey(
                    PsdUiToolkitNodeReference.VirtualGroup(group.id).StableKey))
                {
                    continue;
                }

                PsdUiToolkitLayoutNode root = BuildVirtualGroupNode(
                    group,
                    groupById,
                    layerById,
                    ownerByMemberKey,
                    claimedLayers,
                    builtGroups,
                    visiting,
                    warnings,
                    diagnostics,
                    i);
                if (root != null)
                    rootGroups.Add(root);
            }

            // A cycle has no natural root. Build every remaining group as a root and
            // ignore the edge that closes the cycle so no PSD layer disappears.
            for (int i = 0; i < hostGroups.Count; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = hostGroups[i];
                if (builtGroups.Contains(group.id))
                    continue;
                PsdUiToolkitLayoutNode root = BuildVirtualGroupNode(
                    group,
                    groupById,
                    layerById,
                    ownerByMemberKey,
                    claimedLayers,
                    builtGroups,
                    visiting,
                    warnings,
                    diagnostics,
                    i);
                if (root != null)
                    rootGroups.Add(root);
            }

            List<PsdUiToolkitLayoutNode> result = new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < siblings.Count; i++)
            {
                int id = siblings[i]?.SourceLayer?.LayerId ?? -1;
                if (!claimedLayers.Contains(id))
                    result.Add(siblings[i]);
            }
            result.AddRange(rootGroups);
            result.Sort(CompareByOriginalIndex);
            return result;
        }

        private static PsdUiToolkitLayoutNode BuildVirtualGroupNode(
            PsdUiToolkitVirtualGroupConfig group,
            Dictionary<string, PsdUiToolkitVirtualGroupConfig> groupById,
            Dictionary<int, PsdUiToolkitLayoutNode> layerById,
            Dictionary<string, string> ownerByMemberKey,
            HashSet<int> claimedLayers,
            HashSet<string> builtGroups,
            HashSet<string> visiting,
            List<string> warnings,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics,
            int fallbackIndex)
        {
            if (group == null || builtGroups.Contains(group.id))
                return null;
            if (!visiting.Add(group.id))
            {
                AddWarning(
                    warnings,
                    diagnostics,
                    "CyclicVirtualGroup",
                    $"Layout group '{GetGroupName(group)}' closes a virtual-group cycle; that edge was ignored.",
                    virtualGroupId: group.id);
                return null;
            }

            List<PsdUiToolkitLayoutNode> members = new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < group.members.Length; i++)
            {
                PsdUiToolkitNodeReference member = group.members[i];
                if (!ownerByMemberKey.TryGetValue(member.StableKey, out string owner)
                    || !string.Equals(owner, group.id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (member.kind == PsdUiToolkitNodeReferenceKind.Layer)
                {
                    if (!layerById.TryGetValue(member.layerId, out PsdUiToolkitLayoutNode layerNode))
                    {
                        AddWarning(
                            warnings,
                            diagnostics,
                            "MissingVirtualGroupMember",
                            $"Layout group '{GetGroupName(group)}' references a missing or moved PSD layer.",
                            virtualGroupId: group.id);
                        continue;
                    }
                    if (claimedLayers.Add(member.layerId))
                        members.Add(layerNode);
                    continue;
                }

                if (!groupById.TryGetValue(member.virtualGroupId, out PsdUiToolkitVirtualGroupConfig child)
                    || string.Equals(child.id, group.id, StringComparison.Ordinal))
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "MissingVirtualGroupMember",
                        $"Layout group '{GetGroupName(group)}' references a missing or invalid child group.",
                        virtualGroupId: group.id);
                    continue;
                }

                PsdUiToolkitLayoutNode childNode = BuildVirtualGroupNode(
                    child,
                    groupById,
                    layerById,
                    ownerByMemberKey,
                    claimedLayers,
                    builtGroups,
                    visiting,
                    warnings,
                    diagnostics,
                    fallbackIndex);
                if (childNode != null)
                    members.Add(childNode);
            }

            visiting.Remove(group.id);
            builtGroups.Add(group.id);
            if (members.Count == 0)
            {
                AddWarning(
                    warnings,
                    diagnostics,
                    "EmptyVirtualGroup",
                    $"Layout group '{GetGroupName(group)}' has no valid members and was ignored.",
                    virtualGroupId: group.id);
                return null;
            }
            if (members.Count == 1)
            {
                AddWarning(
                    warnings,
                    diagnostics,
                    "SingleMemberVirtualGroup",
                    $"Layout group '{GetGroupName(group)}' contains only one valid member.",
                    virtualGroupId: group.id);
            }
            HashSet<int> originalIndices = new HashSet<int>();
            for (int i = 0; i < members.Count; i++)
                CollectOriginalSiblingIndices(members[i], originalIndices);
            if (originalIndices.Count > 1)
            {
                int minimumIndex = int.MaxValue;
                int maximumIndex = int.MinValue;
                foreach (int index in originalIndices)
                {
                    minimumIndex = Math.Min(minimumIndex, index);
                    maximumIndex = Math.Max(maximumIndex, index);
                }
                if (maximumIndex - minimumIndex + 1 > originalIndices.Count)
                {
                    AddWarning(
                        warnings,
                        diagnostics,
                        "NonContiguousVirtualGroupMembers",
                        $"Layout group '{GetGroupName(group)}' contains non-contiguous PSD siblings; export remains valid but its stacking order deserves review.",
                        virtualGroupId: group.id);
                }
            }

            PsdUiToolkitLayoutType layoutType =
                group.layout == PsdUiToolkitContainerLayout.Column
                    ? PsdUiToolkitLayoutType.Column
                    : PsdUiToolkitLayoutType.Row;
            OrderChildrenForFlow(members, layoutType, group.wrapMode);
            AddOverlapWarning(
                members,
                layoutType,
                group.wrapMode,
                GetGroupName(group),
                warnings,
                diagnostics,
                -1,
                group.id);

            int originalIndex = fallbackIndex;
            for (int i = 0; i < members.Count; i++)
                originalIndex = i == 0 ? members[i].OriginalIndex : Math.Min(originalIndex, members[i].OriginalIndex);

            return new PsdUiToolkitLayoutNode(
                null,
                ComputeUnionBounds(members),
                false,
                layoutType,
                originalIndex,
                members,
                GetGroupName(group),
                true,
                PsdUiToolkitItemRole.FollowParent,
                group.id,
                group.mainAxisDistribution,
                group.crossAxisAlignment,
                group.wrapMode,
                group.multiLineDistribution);
        }

        private static void CollectOriginalSiblingIndices(
            PsdUiToolkitLayoutNode node,
            HashSet<int> indices)
        {
            if (node == null || indices == null)
                return;
            if (!node.IsSynthetic || string.IsNullOrEmpty(node.VirtualGroupId))
            {
                indices.Add(node.OriginalIndex);
                return;
            }
            for (int i = 0; i < node.Children.Count; i++)
                CollectOriginalSiblingIndices(node.Children[i], indices);
        }

        private static string GetGroupName(PsdUiToolkitVirtualGroupConfig group)
        {
            if (!string.IsNullOrWhiteSpace(group?.name))
                return group.name.Trim();
            if (!string.IsNullOrWhiteSpace(group?.id))
                return $"Layout_{group.id.Substring(0, Math.Min(8, group.id.Length))}";
            return "Layout_Group";
        }

        private static PsdUiToolkitLayerBounds ComputeUnionBounds(
            List<PsdUiToolkitLayoutNode> nodes)
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
            PsdUiToolkitLayoutType layoutType,
            PsdUiToolkitWrapMode wrapMode)
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
            if (wrapMode == PsdUiToolkitWrapMode.Wrap)
            {
                flow.Sort(layoutType == PsdUiToolkitLayoutType.Row
                    ? (Comparison<PsdUiToolkitLayoutNode>)CompareByTopThenLeft
                    : CompareByLeftThenTop);
            }
            else
            {
                flow.Sort(layoutType == PsdUiToolkitLayoutType.Column
                    ? (Comparison<PsdUiToolkitLayoutNode>)CompareByTopThenLeft
                    : CompareByLeftThenTop);
            }

            children.Clear();
            children.AddRange(backgrounds);
            children.AddRange(flow);
            children.AddRange(overlays);
        }

        private static void AddOverlapWarning(
            List<PsdUiToolkitLayoutNode> children,
            PsdUiToolkitLayoutType layoutType,
            PsdUiToolkitWrapMode wrapMode,
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
                    bool sameLine = wrapMode != PsdUiToolkitWrapMode.Wrap
                        || (layoutType == PsdUiToolkitLayoutType.Row
                            ? RangesOverlap(
                                previous.Bounds.Top,
                                previous.Bounds.Top + previous.Bounds.Height,
                                child.Bounds.Top,
                                child.Bounds.Top + child.Bounds.Height)
                            : RangesOverlap(
                                previous.Bounds.Left,
                                previous.Bounds.Left + previous.Bounds.Width,
                                child.Bounds.Left,
                                child.Bounds.Left + child.Bounds.Width));
                    if (sameLine)
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
                }

                previous = child;
            }
        }

        private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
        {
            return Math.Min(firstEnd, secondEnd) > Math.Max(firstStart, secondStart);
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

        private static int CompareByOriginalIndex(
            PsdUiToolkitLayoutNode left,
            PsdUiToolkitLayoutNode right)
        {
            return left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private static int CompareByLeftThenTop(
            PsdUiToolkitLayoutNode left,
            PsdUiToolkitLayoutNode right)
        {
            int compare = left.Bounds.Left.CompareTo(right.Bounds.Left);
            return compare != 0 ? compare : left.Bounds.Top.CompareTo(right.Bounds.Top);
        }

        private static int CompareByTopThenLeft(
            PsdUiToolkitLayoutNode left,
            PsdUiToolkitLayoutNode right)
        {
            int compare = left.Bounds.Top.CompareTo(right.Bounds.Top);
            return compare != 0 ? compare : left.Bounds.Left.CompareTo(right.Bounds.Left);
        }
    }
}

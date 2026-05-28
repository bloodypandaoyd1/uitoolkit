using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    internal sealed class PsdUiToolkitLayoutNode
    {
        public PsdUiToolkitLayoutNode(
            Layer sourceLayer,
            PsdUiToolkitLayerBounds bounds,
            bool renderAsLeaf,
            PsdUiToolkitLayoutType layoutType,
            float confidence,
            string analysisSummary,
            int originalIndex,
            List<PsdUiToolkitLayoutNode> children,
            string displayName = null,
            bool isSynthetic = false,
            string rebuildReason = null)
        {
            SourceLayer = sourceLayer;
            Bounds = bounds;
            RenderAsLeaf = renderAsLeaf;
            LayoutType = layoutType;
            Confidence = confidence;
            AnalysisSummary = analysisSummary ?? string.Empty;
            OriginalIndex = originalIndex;
            Children = children ?? new List<PsdUiToolkitLayoutNode>();
            DisplayName = string.IsNullOrEmpty(displayName)
                ? (sourceLayer?.Name ?? string.Empty)
                : displayName;
            IsSynthetic = isSynthetic;
            RebuildReason = rebuildReason ?? string.Empty;
        }

        public Layer SourceLayer { get; }
        public PsdUiToolkitLayerBounds Bounds { get; }
        public bool RenderAsLeaf { get; }
        public PsdUiToolkitLayoutType LayoutType { get; }
        public float Confidence { get; }
        public string AnalysisSummary { get; }
        public int OriginalIndex { get; }
        public List<PsdUiToolkitLayoutNode> Children { get; }
        public string DisplayName { get; }
        public bool IsSynthetic { get; }
        public string RebuildReason { get; }
    }

    internal sealed class PsdUiToolkitLayoutTree
    {
        public PsdUiToolkitLayoutTree(string rootName, int width, int height, bool autoLayoutEnabled, List<PsdUiToolkitLayoutNode> children)
        {
            RootName = rootName ?? string.Empty;
            Width = width;
            Height = height;
            AutoLayoutEnabled = autoLayoutEnabled;
            Children = children ?? new List<PsdUiToolkitLayoutNode>();
        }

        public string RootName { get; }
        public int Width { get; }
        public int Height { get; }
        public bool AutoLayoutEnabled { get; }
        public List<PsdUiToolkitLayoutNode> Children { get; }
    }

    internal static class PsdUiToolkitAutoLayoutAnalyzer
    {
        private const float DetectionFloor = 0.55f;
        private const float AmbiguityGap = 0.08f;

        private readonly struct LayoutAnalysisResult
        {
            public LayoutAnalysisResult(PsdUiToolkitLayoutType layoutType, float confidence, string summary)
            {
                LayoutType = layoutType;
                Confidence = confidence;
                Summary = summary ?? string.Empty;
            }

            public PsdUiToolkitLayoutType LayoutType { get; }
            public float Confidence { get; }
            public string Summary { get; }
        }

        private readonly struct LayoutScore
        {
            public LayoutScore(PsdUiToolkitLayoutType layoutType, float confidence, string summary)
            {
                LayoutType = layoutType;
                Confidence = Clamp01(confidence);
                Summary = summary ?? string.Empty;
            }

            public PsdUiToolkitLayoutType LayoutType { get; }
            public float Confidence { get; }
            public string Summary { get; }
            public bool IsCandidate => LayoutType != PsdUiToolkitLayoutType.Absolute && Confidence > 0f;
        }

        public static PsdUiToolkitLayoutTree Analyze(PsdImage psd, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult, string rootName)
        {
            if (rasterResult == null)
                throw new ArgumentNullException(nameof(rasterResult));

            return AnalyzeInternal(psd, configMap, rasterResult, rootName, false);
        }

        public static PsdUiToolkitLayoutTree AnalyzeForInspector(PsdImage psd, PsdUiToolkitLayerConfigMap configMap, string rootName)
        {
            return AnalyzeInternal(psd, configMap, null, rootName, true);
        }

        public static bool TryFindNode(PsdUiToolkitLayoutTree tree, int layerId, out PsdUiToolkitLayoutNode node, out PsdUiToolkitLayoutNode parent)
        {
            node = null;
            parent = null;
            if (tree == null)
                return false;

            for (int i = 0; i < tree.Children.Count; i++)
            {
                if (TryFindNodeRecursive(tree.Children[i], null, layerId, out node, out parent))
                    return true;
            }

            return false;
        }

        private static PsdUiToolkitLayoutTree AnalyzeInternal(
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

            List<PsdUiToolkitLayoutNode> children = new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < psd.Children.Count; i++)
            {
                Layer child = psd.Children[i];
                PsdUiToolkitLayoutNode node = BuildNode(child, configMap, rasterResult, i, inspectorMode);
                if (node != null)
                    children.Add(node);
            }

            return new PsdUiToolkitLayoutTree(rootName, psd.Width, psd.Height, configMap.IsAutoLayoutEnabled(), children);
        }

        private static PsdUiToolkitLayoutNode BuildNode(Layer layer, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult, int originalIndex, bool inspectorMode)
        {
            if (layer == null || layer.LayerId == null)
                return null;
            if (!configMap.IsExported(layer))
                return null;
            if (rasterResult != null && rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
                return null;

            PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(layer);
            bool renderAsLeaf = ShouldRenderAsLeaf(layer, configMap, rasterResult, inspectorMode);
            PsdUiToolkitLayerConfig config = configMap.Get(layer);
            List<PsdUiToolkitLayoutNode> childNodes = new List<PsdUiToolkitLayoutNode>();
            if (!renderAsLeaf)
            {
                for (int i = 0; i < layer.Children.Count; i++)
                {
                    Layer child = layer.Children[i];
                    PsdUiToolkitLayoutNode childNode = BuildNode(child, configMap, rasterResult, i, inspectorMode);
                    if (childNode != null)
                        childNodes.Add(childNode);
                }
            }

            LayoutAnalysisResult analysis = AnalyzeNodeLayout(layer, bounds, renderAsLeaf, childNodes, configMap, config);
            if (!renderAsLeaf && analysis.LayoutType != PsdUiToolkitLayoutType.Absolute && analysis.LayoutType != PsdUiToolkitLayoutType.Overlay)
                ReorderChildrenForLayout(childNodes, bounds, analysis.LayoutType, configMap);

            return new PsdUiToolkitLayoutNode(layer, bounds, renderAsLeaf, analysis.LayoutType, analysis.Confidence, analysis.Summary, originalIndex, childNodes);
        }

        private static bool ShouldRenderAsLeaf(Layer layer, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult, bool inspectorMode)
        {
            if (layer == null)
                return true;
            if (!layer.IsGroup)
                return true;

            if (!inspectorMode && rasterResult != null && layer.LayerId.HasValue)
            {
                int layerId = layer.LayerId.Value;
                return rasterResult.AssetsByLayerId.ContainsKey(layerId)
                    || rasterResult.CompositeLeafLayerIds.Contains(layerId);
            }

            return configMap.IsMergeExport(layer) || configMap.UseCustomImage(layer);
        }

        private static bool TryFindNodeRecursive(PsdUiToolkitLayoutNode current, PsdUiToolkitLayoutNode parentNode, int layerId, out PsdUiToolkitLayoutNode node, out PsdUiToolkitLayoutNode parent)
        {
            node = null;
            parent = null;
            if (current?.SourceLayer?.LayerId == layerId)
            {
                node = current;
                parent = parentNode;
                return true;
            }

            if (current == null)
                return false;

            for (int i = 0; i < current.Children.Count; i++)
            {
                if (TryFindNodeRecursive(current.Children[i], current, layerId, out node, out parent))
                    return true;
            }

            return false;
        }

        private static LayoutAnalysisResult AnalyzeNodeLayout(
            Layer layer,
            PsdUiToolkitLayerBounds bounds,
            bool renderAsLeaf,
            List<PsdUiToolkitLayoutNode> childNodes,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitLayerConfig config)
        {
            if (config == null)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "No layer config available; keep absolute export.");
            if (config.forcedLayoutType != PsdUiToolkitLayoutType.Auto)
                return new LayoutAnalysisResult(config.forcedLayoutType, 1f, $"Layout type forced to {config.forcedLayoutType} by layer override.");
            if (config.semanticRole == PsdUiToolkitSemanticRole.Overlay && !renderAsLeaf)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Overlay, 1f, "Overlay role forced on this container by layer override.");
            if (!configMap.IsAutoLayoutEnabled())
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "Auto-layout disabled in PSD settings; keep absolute export.");
            if (!configMap.ParticipateInAutoLayout(layer))
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "Layer opted out of auto-layout participation.");
            if (config.keepAbsoluteInsideParent || renderAsLeaf)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "Leaf or absolute override keeps this node on absolute positioning.");

            List<PsdUiToolkitLayoutNode> flowCandidates = GetFlowCandidates(childNodes, bounds, configMap);
            if (flowCandidates.Count < 2)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "Not enough flow candidates under this container to infer layout.");

            PsdUiToolkitAutoLayoutGlobalConfig autoLayout = configMap.GetAutoLayoutConfig();
            LayoutScore rowScore = ScoreRow(flowCandidates, autoLayout);
            LayoutScore columnScore = ScoreColumn(flowCandidates, autoLayout);
            LayoutScore gridScore = ScoreGrid(flowCandidates, autoLayout, config);
            LayoutAnalysisResult best = SelectBestLayout(rowScore, columnScore, gridScore, config.forceContainer);
            if (best.LayoutType != PsdUiToolkitLayoutType.Absolute)
                return best;

            return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "No high-confidence row, column, or grid pattern was detected; keep absolute export.");
        }

        private static LayoutAnalysisResult SelectBestLayout(LayoutScore rowScore, LayoutScore columnScore, LayoutScore gridScore, bool forceContainer)
        {
            List<LayoutScore> candidates = new List<LayoutScore>(3);
            if (rowScore.IsCandidate)
                candidates.Add(rowScore);
            if (columnScore.IsCandidate)
                candidates.Add(columnScore);
            if (gridScore.IsCandidate)
                candidates.Add(gridScore);

            if (candidates.Count == 0)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, string.Empty);

            candidates.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
            LayoutScore best = candidates[0];
            float second = candidates.Count > 1 ? candidates[1].Confidence : 0f;
            if (!forceContainer && best.Confidence < DetectionFloor)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, $"Best layout candidate was {best.LayoutType} at confidence {best.Confidence:0.##}, below the detection floor.");
            if (!forceContainer && second > 0f && best.Confidence - second < AmbiguityGap)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, $"Layout candidates were ambiguous ({best.LayoutType} {best.Confidence:0.##} vs {candidates[1].LayoutType} {second:0.##}).");

            return new LayoutAnalysisResult(best.LayoutType, best.Confidence, best.Summary);
        }

        private static List<PsdUiToolkitLayoutNode> GetFlowCandidates(List<PsdUiToolkitLayoutNode> childNodes, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            List<PsdUiToolkitLayoutNode> flowCandidates = new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < childNodes.Count; i++)
            {
                PsdUiToolkitLayoutNode child = childNodes[i];
                if (ShouldIncludeInFlow(child, parentBounds, configMap))
                    flowCandidates.Add(child);
            }

            return flowCandidates;
        }

        private static bool ShouldIncludeInFlow(PsdUiToolkitLayoutNode childNode, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            Layer childLayer = childNode?.SourceLayer;
            if (childLayer == null)
                return false;

            PsdUiToolkitLayerConfig childConfig = configMap.Get(childLayer);
            if (!configMap.ParticipateInAutoLayout(childLayer))
                return false;
            if (!childConfig.includeInFlow || childConfig.keepAbsoluteInsideParent)
                return false;
            if (childConfig.forceBackground || childConfig.semanticRole == PsdUiToolkitSemanticRole.Background)
                return false;
            if (childConfig.semanticRole == PsdUiToolkitSemanticRole.Overlay)
                return false;
            if (IsBackgroundLike(childNode, parentBounds, configMap, childConfig))
                return false;

            return true;
        }

        private static bool IsBackgroundLike(PsdUiToolkitLayoutNode childNode, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitLayerConfig childConfig)
        {
            if (childNode?.SourceLayer == null)
                return false;
            if (!configMap.GetAutoLayoutConfig().detectBackgroundContainers)
                return false;
            if (childNode.SourceLayer.Kind == LayerKind.Type)
                return false;
            if (childConfig.semanticRole == PsdUiToolkitSemanticRole.Content || childConfig.semanticRole == PsdUiToolkitSemanticRole.Container)
                return false;

            int tolerance = Math.Max(2, configMap.GetAutoLayoutConfig().alignmentTolerance);
            bool fillsParent = Math.Abs(childNode.Bounds.Left - parentBounds.Left) <= tolerance
                && Math.Abs(childNode.Bounds.Top - parentBounds.Top) <= tolerance
                && Math.Abs(GetRight(childNode.Bounds) - GetRight(parentBounds)) <= tolerance
                && Math.Abs(GetBottom(childNode.Bounds) - GetBottom(parentBounds)) <= tolerance;
            float parentArea = Math.Max(1f, parentBounds.Width * parentBounds.Height);
            float childArea = Math.Max(1f, childNode.Bounds.Width * childNode.Bounds.Height);
            float fillRatio = childArea / parentArea;
            return fillsParent || fillRatio >= 0.72f;
        }

        private static LayoutScore ScoreRow(List<PsdUiToolkitLayoutNode> flowCandidates, PsdUiToolkitAutoLayoutGlobalConfig autoLayout)
        {
            if (flowCandidates.Count < 2)
                return new LayoutScore(PsdUiToolkitLayoutType.Row, 0f, string.Empty);

            List<PsdUiToolkitLayoutNode> sorted = new List<PsdUiToolkitLayoutNode>(flowCandidates);
            sorted.Sort(CompareByLeftThenTop);
            float tolerance = Math.Max(1f, autoLayout.alignmentTolerance);
            float gapTolerance = Math.Max(1f, autoLayout.gapTolerance);
            float topDeviation;
            float centerDeviation;
            float bottomDeviation;
            float topScore = ComputeAlignmentScore(sorted, node => GetTop(node), tolerance, out topDeviation);
            float centerScore = ComputeAlignmentScore(sorted, GetCenterY, tolerance, out centerDeviation);
            float bottomScore = ComputeAlignmentScore(sorted, node => GetBottom(node), tolerance, out bottomDeviation);
            float alignmentScore = topScore;
            float alignmentDeviation = topDeviation;
            string anchor = "top";
            if (centerScore > alignmentScore)
            {
                alignmentScore = centerScore;
                alignmentDeviation = centerDeviation;
                anchor = "centerY";
            }

            if (bottomScore > alignmentScore)
            {
                alignmentScore = bottomScore;
                alignmentDeviation = bottomDeviation;
                anchor = "bottom";
            }

            float overlapScore;
            float gapScore;
            float averageGap;
            float gapDeviation;
            ComputeMainAxisMetrics(sorted, true, gapTolerance, out overlapScore, out gapScore, out averageGap, out gapDeviation);
            float spanScore = ComputeSpanScore(sorted, true);
            float confidence = 0.42f * alignmentScore + 0.25f * gapScore + 0.2f * overlapScore + 0.13f * spanScore;
            string summary = $"Row heuristic: {anchor} aligned (avg deviation {alignmentDeviation:0.#}), avg gap {averageGap:0.#}, gap deviation {gapDeviation:0.#}, overlap score {overlapScore:0.##}.";
            return new LayoutScore(PsdUiToolkitLayoutType.Row, confidence, summary);
        }

        private static LayoutScore ScoreColumn(List<PsdUiToolkitLayoutNode> flowCandidates, PsdUiToolkitAutoLayoutGlobalConfig autoLayout)
        {
            if (flowCandidates.Count < 2)
                return new LayoutScore(PsdUiToolkitLayoutType.Column, 0f, string.Empty);

            List<PsdUiToolkitLayoutNode> sorted = new List<PsdUiToolkitLayoutNode>(flowCandidates);
            sorted.Sort(CompareByTopThenLeft);
            float tolerance = Math.Max(1f, autoLayout.alignmentTolerance);
            float gapTolerance = Math.Max(1f, autoLayout.gapTolerance);
            float leftDeviation;
            float centerDeviation;
            float rightDeviation;
            float leftScore = ComputeAlignmentScore(sorted, node => GetLeft(node), tolerance, out leftDeviation);
            float centerScore = ComputeAlignmentScore(sorted, GetCenterX, tolerance, out centerDeviation);
            float rightScore = ComputeAlignmentScore(sorted, node => GetRight(node), tolerance, out rightDeviation);
            float alignmentScore = leftScore;
            float alignmentDeviation = leftDeviation;
            string anchor = "left";
            if (centerScore > alignmentScore)
            {
                alignmentScore = centerScore;
                alignmentDeviation = centerDeviation;
                anchor = "centerX";
            }

            if (rightScore > alignmentScore)
            {
                alignmentScore = rightScore;
                alignmentDeviation = rightDeviation;
                anchor = "right";
            }

            float overlapScore;
            float gapScore;
            float averageGap;
            float gapDeviation;
            ComputeMainAxisMetrics(sorted, false, gapTolerance, out overlapScore, out gapScore, out averageGap, out gapDeviation);
            float spanScore = ComputeSpanScore(sorted, false);
            float confidence = 0.42f * alignmentScore + 0.25f * gapScore + 0.2f * overlapScore + 0.13f * spanScore;
            string summary = $"Column heuristic: {anchor} aligned (avg deviation {alignmentDeviation:0.#}), avg gap {averageGap:0.#}, gap deviation {gapDeviation:0.#}, overlap score {overlapScore:0.##}.";
            return new LayoutScore(PsdUiToolkitLayoutType.Column, confidence, summary);
        }

        private static LayoutScore ScoreGrid(List<PsdUiToolkitLayoutNode> flowCandidates, PsdUiToolkitAutoLayoutGlobalConfig autoLayout, PsdUiToolkitLayerConfig config)
        {
            if (flowCandidates.Count < 4)
                return new LayoutScore(PsdUiToolkitLayoutType.Grid, 0f, string.Empty);

            int clusterTolerance = Math.Max(autoLayout.alignmentTolerance, autoLayout.gapTolerance);
            List<List<PsdUiToolkitLayoutNode>> rows = ClusterNodes(flowCandidates, true, clusterTolerance);
            List<List<PsdUiToolkitLayoutNode>> columns = ClusterNodes(flowCandidates, false, clusterTolerance);
            if (rows.Count < 2 || columns.Count < 2)
                return new LayoutScore(PsdUiToolkitLayoutType.Grid, 0f, string.Empty);

            int expectedColumns = config.gridColumnCount > 0 ? config.gridColumnCount : columns.Count;
            float occupancy = Clamp01(flowCandidates.Count / (float)Math.Max(1, rows.Count * Math.Max(2, expectedColumns)));
            float sizeScore = ComputeSizeConsistencyScore(flowCandidates);
            float alignmentScore = ComputeGridAlignmentScore(rows, columns, clusterTolerance);
            float gapScore = ComputeGridGapScore(rows, clusterTolerance);
            float overlapScore = ComputePairwiseOverlapScore(flowCandidates, Math.Max(1f, autoLayout.gapTolerance));
            float confidence = 0.28f * occupancy + 0.24f * sizeScore + 0.2f * alignmentScore + 0.16f * gapScore + 0.12f * overlapScore;
            string summary = $"Grid heuristic: {rows.Count} rows x {columns.Count} columns, occupancy {occupancy:0.##}, size score {sizeScore:0.##}, alignment score {alignmentScore:0.##}.";
            return new LayoutScore(PsdUiToolkitLayoutType.Grid, confidence, summary);
        }

        private static void ReorderChildrenForLayout(List<PsdUiToolkitLayoutNode> childNodes, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayoutType layoutType, PsdUiToolkitLayerConfigMap configMap)
        {
            List<PsdUiToolkitLayoutNode> backgroundChildren = new List<PsdUiToolkitLayoutNode>();
            List<PsdUiToolkitLayoutNode> flowChildren = new List<PsdUiToolkitLayoutNode>();
            List<PsdUiToolkitLayoutNode> remainingChildren = new List<PsdUiToolkitLayoutNode>();

            for (int i = 0; i < childNodes.Count; i++)
            {
                PsdUiToolkitLayoutNode child = childNodes[i];
                if (IsBackgroundLike(child, parentBounds, configMap, configMap.Get(child.SourceLayer)) || IsForcedBackground(child, configMap))
                    backgroundChildren.Add(child);
                else if (ShouldIncludeInFlow(child, parentBounds, configMap))
                    flowChildren.Add(child);
                else
                    remainingChildren.Add(child);
            }

            backgroundChildren.Sort((left, right) => left.OriginalIndex.CompareTo(right.OriginalIndex));
            remainingChildren.Sort((left, right) => left.OriginalIndex.CompareTo(right.OriginalIndex));
            SortFlowChildren(flowChildren, layoutType, configMap);

            childNodes.Clear();
            childNodes.AddRange(backgroundChildren);
            childNodes.AddRange(flowChildren);
            childNodes.AddRange(remainingChildren);
        }

        private static bool IsForcedBackground(PsdUiToolkitLayoutNode child, PsdUiToolkitLayerConfigMap configMap)
        {
            if (child?.SourceLayer == null)
                return false;

            PsdUiToolkitLayerConfig config = configMap.Get(child.SourceLayer);
            return config.forceBackground || config.semanticRole == PsdUiToolkitSemanticRole.Background;
        }

        private static void SortFlowChildren(List<PsdUiToolkitLayoutNode> flowChildren, PsdUiToolkitLayoutType layoutType, PsdUiToolkitLayerConfigMap configMap)
        {
            if (flowChildren.Count <= 1)
                return;

            List<PsdUiToolkitLayoutNode> geometryOrdered = new List<PsdUiToolkitLayoutNode>(flowChildren);
            switch (layoutType)
            {
                case PsdUiToolkitLayoutType.Column:
                    geometryOrdered.Sort(CompareByTopThenLeft);
                    break;
                case PsdUiToolkitLayoutType.Grid:
                    geometryOrdered.Sort(CompareByTopThenLeft);
                    break;
                default:
                    geometryOrdered.Sort(CompareByLeftThenTop);
                    break;
            }

            Dictionary<int, int> geometryRank = new Dictionary<int, int>();
            for (int i = 0; i < geometryOrdered.Count; i++)
            {
                int? layerId = geometryOrdered[i].SourceLayer?.LayerId;
                if (layerId.HasValue)
                    geometryRank[layerId.Value] = i;
            }

            flowChildren.Sort((left, right) =>
            {
                int leftRank = GetEffectiveFlowOrder(left, geometryRank, configMap);
                int rightRank = GetEffectiveFlowOrder(right, geometryRank, configMap);
                int compare = leftRank.CompareTo(rightRank);
                if (compare != 0)
                    return compare;

                return layoutType == PsdUiToolkitLayoutType.Column || layoutType == PsdUiToolkitLayoutType.Grid
                    ? CompareByTopThenLeft(left, right)
                    : CompareByLeftThenTop(left, right);
            });
        }

        private static int GetEffectiveFlowOrder(PsdUiToolkitLayoutNode node, Dictionary<int, int> geometryRank, PsdUiToolkitLayerConfigMap configMap)
        {
            int? layerId = node?.SourceLayer?.LayerId;
            int fallbackRank = layerId.HasValue && geometryRank.TryGetValue(layerId.Value, out int rank)
                ? rank
                : node?.OriginalIndex ?? 0;
            if (node?.SourceLayer == null)
                return fallbackRank;

            PsdUiToolkitLayerConfig config = configMap.Get(node.SourceLayer);
            return config.orderOverride >= 0 ? config.orderOverride : fallbackRank;
        }

        private static float ComputeAlignmentScore(List<PsdUiToolkitLayoutNode> nodes, Func<PsdUiToolkitLayoutNode, float> selector, float tolerance, out float deviation)
        {
            List<float> values = new List<float>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                values.Add(selector(nodes[i]));

            float pivot = Average(values);
            deviation = AverageAbsoluteDeviation(values, pivot);
            return 1f - Clamp01(deviation / Math.Max(1f, tolerance));
        }

        private static void ComputeMainAxisMetrics(List<PsdUiToolkitLayoutNode> sorted, bool horizontal, float gapTolerance, out float overlapScore, out float gapScore, out float averageGap, out float gapDeviation)
        {
            List<float> gaps = new List<float>();
            float maxOverlap = 0f;
            for (int i = 1; i < sorted.Count; i++)
            {
                int currentStart = horizontal ? GetLeft(sorted[i]) : GetTop(sorted[i]);
                int previousEnd = horizontal ? GetRight(sorted[i - 1]) : GetBottom(sorted[i - 1]);
                float gap = currentStart - previousEnd;
                if (gap < 0f)
                    maxOverlap = Math.Max(maxOverlap, -gap);

                gaps.Add(Math.Max(0f, gap));
            }

            overlapScore = 1f - Clamp01(maxOverlap / Math.Max(1f, gapTolerance));
            averageGap = gaps.Count == 0 ? 0f : Average(gaps);
            gapDeviation = gaps.Count == 0 ? 0f : AverageAbsoluteDeviation(gaps, averageGap);
            if (gaps.Count <= 1)
            {
                gapScore = 1f;
                return;
            }

            float denominator = Math.Max(gapTolerance, Math.Max(1f, averageGap * 0.5f));
            gapScore = 1f - Clamp01(gapDeviation / denominator);
        }

        private static float ComputeSpanScore(List<PsdUiToolkitLayoutNode> nodes, bool horizontal)
        {
            if (nodes.Count == 0)
                return 0f;

            int minLeft = int.MaxValue;
            int minTop = int.MaxValue;
            int maxRight = int.MinValue;
            int maxBottom = int.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                minLeft = Math.Min(minLeft, GetLeft(nodes[i]));
                minTop = Math.Min(minTop, GetTop(nodes[i]));
                maxRight = Math.Max(maxRight, GetRight(nodes[i]));
                maxBottom = Math.Max(maxBottom, GetBottom(nodes[i]));
            }

            float spanX = Math.Max(1f, maxRight - minLeft);
            float spanY = Math.Max(1f, maxBottom - minTop);
            return horizontal
                ? Clamp01(spanX / (spanX + spanY))
                : Clamp01(spanY / (spanX + spanY));
        }

        private static List<List<PsdUiToolkitLayoutNode>> ClusterNodes(List<PsdUiToolkitLayoutNode> nodes, bool clusterByTop, int tolerance)
        {
            List<PsdUiToolkitLayoutNode> sorted = new List<PsdUiToolkitLayoutNode>(nodes);
            sorted.Sort(clusterByTop ? (Comparison<PsdUiToolkitLayoutNode>)CompareByTopThenLeft : CompareByLeftThenTop);

            List<List<PsdUiToolkitLayoutNode>> clusters = new List<List<PsdUiToolkitLayoutNode>>();
            List<float> anchors = new List<float>();
            for (int i = 0; i < sorted.Count; i++)
            {
                PsdUiToolkitLayoutNode node = sorted[i];
                float value = clusterByTop ? GetTop(node) : GetLeft(node);
                if (clusters.Count == 0 || Math.Abs(value - anchors[anchors.Count - 1]) > tolerance)
                {
                    clusters.Add(new List<PsdUiToolkitLayoutNode> { node });
                    anchors.Add(value);
                }
                else
                {
                    List<PsdUiToolkitLayoutNode> cluster = clusters[clusters.Count - 1];
                    cluster.Add(node);
                    anchors[anchors.Count - 1] = ((anchors[anchors.Count - 1] * (cluster.Count - 1)) + value) / cluster.Count;
                }
            }

            return clusters;
        }

        private static float ComputeSizeConsistencyScore(List<PsdUiToolkitLayoutNode> nodes)
        {
            List<float> widths = new List<float>(nodes.Count);
            List<float> heights = new List<float>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                widths.Add(nodes[i].Bounds.Width);
                heights.Add(nodes[i].Bounds.Height);
            }

            float averageWidth = Average(widths);
            float averageHeight = Average(heights);
            float widthDeviation = AverageAbsoluteDeviation(widths, averageWidth);
            float heightDeviation = AverageAbsoluteDeviation(heights, averageHeight);
            float widthScore = 1f - Clamp01(widthDeviation / Math.Max(1f, averageWidth));
            float heightScore = 1f - Clamp01(heightDeviation / Math.Max(1f, averageHeight));
            return (widthScore + heightScore) * 0.5f;
        }

        private static float ComputeGridAlignmentScore(List<List<PsdUiToolkitLayoutNode>> rows, List<List<PsdUiToolkitLayoutNode>> columns, int tolerance)
        {
            List<float> rowDeviations = new List<float>();
            for (int i = 0; i < rows.Count; i++)
            {
                List<float> values = new List<float>(rows[i].Count);
                for (int j = 0; j < rows[i].Count; j++)
                    values.Add(GetTop(rows[i][j]));

                float pivot = Average(values);
                rowDeviations.Add(AverageAbsoluteDeviation(values, pivot));
            }

            List<float> columnDeviations = new List<float>();
            for (int i = 0; i < columns.Count; i++)
            {
                List<float> values = new List<float>(columns[i].Count);
                for (int j = 0; j < columns[i].Count; j++)
                    values.Add(GetLeft(columns[i][j]));

                float pivot = Average(values);
                columnDeviations.Add(AverageAbsoluteDeviation(values, pivot));
            }

            float rowScore = 1f - Clamp01(Average(rowDeviations) / Math.Max(1f, tolerance));
            float columnScore = 1f - Clamp01(Average(columnDeviations) / Math.Max(1f, tolerance));
            return (rowScore + columnScore) * 0.5f;
        }

        private static float ComputeGridGapScore(List<List<PsdUiToolkitLayoutNode>> rows, int tolerance)
        {
            List<float> rowGaps = new List<float>();
            List<float> columnGaps = new List<float>();
            int previousRowBottom = 0;
            bool hasPreviousRow = false;
            for (int i = 0; i < rows.Count; i++)
            {
                List<PsdUiToolkitLayoutNode> row = new List<PsdUiToolkitLayoutNode>(rows[i]);
                row.Sort(CompareByLeftThenTop);
                for (int j = 1; j < row.Count; j++)
                    columnGaps.Add(Math.Max(0, GetLeft(row[j]) - GetRight(row[j - 1])));

                int rowTop = int.MaxValue;
                int rowBottom = int.MinValue;
                for (int j = 0; j < row.Count; j++)
                {
                    rowTop = Math.Min(rowTop, GetTop(row[j]));
                    rowBottom = Math.Max(rowBottom, GetBottom(row[j]));
                }

                if (hasPreviousRow)
                    rowGaps.Add(Math.Max(0, rowTop - previousRowBottom));

                previousRowBottom = rowBottom;
                hasPreviousRow = true;
            }

            float rowGapScore = rowGaps.Count <= 1 ? 1f : 1f - Clamp01(AverageAbsoluteDeviation(rowGaps, Average(rowGaps)) / Math.Max(1f, tolerance));
            float columnGapScore = columnGaps.Count <= 1 ? 1f : 1f - Clamp01(AverageAbsoluteDeviation(columnGaps, Average(columnGaps)) / Math.Max(1f, tolerance));
            return (rowGapScore + columnGapScore) * 0.5f;
        }

        private static float ComputePairwiseOverlapScore(List<PsdUiToolkitLayoutNode> nodes, float tolerance)
        {
            float worstOverlap = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    int overlapLeft = Math.Max(GetLeft(nodes[i]), GetLeft(nodes[j]));
                    int overlapTop = Math.Max(GetTop(nodes[i]), GetTop(nodes[j]));
                    int overlapRight = Math.Min(GetRight(nodes[i]), GetRight(nodes[j]));
                    int overlapBottom = Math.Min(GetBottom(nodes[i]), GetBottom(nodes[j]));
                    int overlapWidth = overlapRight - overlapLeft;
                    int overlapHeight = overlapBottom - overlapTop;
                    if (overlapWidth <= 0 || overlapHeight <= 0)
                        continue;

                    float overlapArea = overlapWidth * overlapHeight;
                    float smallerArea = Math.Max(1f, Math.Min(nodes[i].Bounds.Width * nodes[i].Bounds.Height, nodes[j].Bounds.Width * nodes[j].Bounds.Height));
                    worstOverlap = Math.Max(worstOverlap, overlapArea / smallerArea);
                }
            }

            return 1f - Clamp01(worstOverlap / Math.Max(0.01f, tolerance / 100f));
        }

        private static int CompareByLeftThenTop(PsdUiToolkitLayoutNode left, PsdUiToolkitLayoutNode right)
        {
            int compare = GetLeft(left).CompareTo(GetLeft(right));
            return compare != 0 ? compare : GetTop(left).CompareTo(GetTop(right));
        }

        private static int CompareByTopThenLeft(PsdUiToolkitLayoutNode left, PsdUiToolkitLayoutNode right)
        {
            int compare = GetTop(left).CompareTo(GetTop(right));
            return compare != 0 ? compare : GetLeft(left).CompareTo(GetLeft(right));
        }

        private static int GetLeft(PsdUiToolkitLayoutNode node) => node?.Bounds.Left ?? 0;
        private static int GetTop(PsdUiToolkitLayoutNode node) => node?.Bounds.Top ?? 0;
        private static int GetRight(PsdUiToolkitLayoutNode node) => node == null ? 0 : node.Bounds.Left + node.Bounds.Width;
        private static int GetBottom(PsdUiToolkitLayoutNode node) => node == null ? 0 : node.Bounds.Top + node.Bounds.Height;
        private static int GetRight(PsdUiToolkitLayerBounds bounds) => bounds.Left + bounds.Width;
        private static int GetBottom(PsdUiToolkitLayerBounds bounds) => bounds.Top + bounds.Height;
        private static float GetCenterX(PsdUiToolkitLayoutNode node) => GetLeft(node) + (node?.Bounds.Width ?? 0) * 0.5f;
        private static float GetCenterY(PsdUiToolkitLayoutNode node) => GetTop(node) + (node?.Bounds.Height ?? 0) * 0.5f;

        private static float Average(List<float> values)
        {
            if (values == null || values.Count == 0)
                return 0f;

            float total = 0f;
            for (int i = 0; i < values.Count; i++)
                total += values[i];
            return total / values.Count;
        }

        private static float AverageAbsoluteDeviation(List<float> values, float pivot)
        {
            if (values == null || values.Count == 0)
                return 0f;

            float total = 0f;
            for (int i = 0; i < values.Count; i++)
                total += Math.Abs(values[i] - pivot);
            return total / values.Count;
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
                return 0f;
            if (value >= 1f)
                return 1f;
            return value;
        }
    }
}

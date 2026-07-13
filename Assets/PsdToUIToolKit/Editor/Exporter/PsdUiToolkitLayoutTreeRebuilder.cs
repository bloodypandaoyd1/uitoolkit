using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitLayoutTreeRebuilder
    {
        private sealed class RebuildNodeState
        {
            public Layer SourceLayer;
            public PsdUiToolkitLayerBounds Bounds;
            public int DrawOrder;
            public bool IsSynthetic;
            public string DisplayName = string.Empty;
            public string RebuildReason = string.Empty;
            public PsdUiToolkitLayoutType LayoutType = PsdUiToolkitLayoutType.Absolute;
            public float Confidence;
            public string AnalysisSummary = string.Empty;
            public readonly List<RebuildNodeState> Children = new List<RebuildNodeState>();
        }

        private readonly struct SyntheticInsertionCandidate
        {
            public SyntheticInsertionCandidate(int startIndex, int length, RebuildNodeState node)
            {
                StartIndex = startIndex;
                Length = length;
                Node = node;
            }

            public int StartIndex { get; }
            public int Length { get; }
            public RebuildNodeState Node { get; }
            public float Confidence => Node?.Confidence ?? 0f;
        }

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

        public static PsdUiToolkitLayoutTree Build(PsdImage psd, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult, string rootName)
        {
            return BuildInternal(psd, configMap, rasterResult, rootName, false);
        }

        public static PsdUiToolkitLayoutTree AnalyzeForInspector(PsdImage psd, PsdUiToolkitLayerConfigMap configMap, string rootName)
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
            if (!inspectorMode && rasterResult == null)
                throw new ArgumentNullException(nameof(rasterResult));

            List<RebuildNodeState> flattenedNodes = new List<RebuildNodeState>();
            int drawOrder = 0;
            for (int i = 0; i < psd.Children.Count; i++)
                CollectRenderableNodes(psd.Children[i], flattenedNodes, ref drawOrder, configMap, rasterResult, inspectorMode);

            List<int> expectedDrawOrder = new List<int>(flattenedNodes.Count);
            for (int i = 0; i < flattenedNodes.Count; i++)
                expectedDrawOrder.Add(flattenedNodes[i].DrawOrder);

            PsdUiToolkitLayerBounds rootBounds = new PsdUiToolkitLayerBounds(0, 0, psd.Width, psd.Height);
            List<RebuildNodeState> rebuiltNodes = RebuildContainedSiblings(flattenedNodes, rootBounds, configMap);
            int syntheticCounter = 0;
            for (int i = 0; i < rebuiltNodes.Count; i++)
                ApplyAutoLayoutRecursively(rebuiltNodes[i], configMap, ref syntheticCounter);

            int maxPasses = configMap.GetAutoLayoutProfile().MaxNestingDepth;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                if (!TryInsertSyntheticContainers(rebuiltNodes, rootBounds, configMap, rootName, ref syntheticCounter))
                    break;
            }

            ValidateDrawOrderOrThrow(rebuiltNodes, expectedDrawOrder);
            return new PsdUiToolkitLayoutTree(rootName, psd.Width, psd.Height, configMap.IsAutoLayoutEnabled(), ConvertNodes(rebuiltNodes));
        }

        private static void CollectRenderableNodes(
            Layer layer,
            List<RebuildNodeState> flattenedNodes,
            ref int drawOrder,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            bool inspectorMode)
        {
            if (layer?.LayerId == null)
                return;
            if (!configMap.IsExported(layer))
                return;
            if (rasterResult != null && rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
                return;

            if (LayerRendersSelf(layer, configMap, rasterResult, inspectorMode))
            {
                flattenedNodes.Add(new RebuildNodeState
                {
                    SourceLayer = layer,
                    Bounds = PsdUiToolkitRasterExporter.GetLayerBounds(layer),
                    DrawOrder = drawOrder++,
                    DisplayName = layer.Name ?? string.Empty,
                });
            }

            for (int i = 0; i < layer.Children.Count; i++)
                CollectRenderableNodes(layer.Children[i], flattenedNodes, ref drawOrder, configMap, rasterResult, inspectorMode);
        }

        private static bool LayerRendersSelf(Layer layer, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult, bool inspectorMode)
        {
            if (layer == null || layer.LayerId == null)
                return false;
            if (!layer.IsGroup)
                return true;

            if (!inspectorMode && rasterResult != null)
            {
                int layerId = layer.LayerId.Value;
                return rasterResult.AssetsByLayerId.ContainsKey(layerId)
                    || rasterResult.CompositeLeafLayerIds.Contains(layerId);
            }

            return configMap.IsMergeExport(layer) || configMap.UseCustomImage(layer);
        }

        private static List<RebuildNodeState> RebuildContainedSiblings(List<RebuildNodeState> siblings, PsdUiToolkitLayerBounds rootBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            List<RebuildNodeState> rebuilt = new List<RebuildNodeState>(siblings.Count);
            for (int i = 0; i < siblings.Count; i++)
            {
                RebuildNodeState host = siblings[i];
                if (!CanHostChildren(host, rootBounds, configMap))
                {
                    rebuilt.Add(host);
                    continue;
                }

                List<RebuildNodeState> containedBlock = new List<RebuildNodeState>();
                int nextIndex = i + 1;
                while (nextIndex < siblings.Count && ContainsBounds(host.Bounds, siblings[nextIndex].Bounds, configMap.GetAutoLayoutProfile().AlignmentTolerance))
                {
                    containedBlock.Add(siblings[nextIndex]);
                    nextIndex++;
                }

                if (containedBlock.Count == 0)
                {
                    rebuilt.Add(host);
                    continue;
                }

                host.Children.AddRange(RebuildContainedSiblings(containedBlock, rootBounds, configMap));
                rebuilt.Add(host);
                i = nextIndex - 1;
            }

            return rebuilt;
        }

        private static bool CanHostChildren(RebuildNodeState host, PsdUiToolkitLayerBounds rootBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            Layer layer = host?.SourceLayer;
            if (layer?.LayerId == null)
                return false;
            if (layer.Kind == LayerKind.Type)
                return false;

            if (!configMap.IsVisible(layer))
                return false;

            int tolerance = Math.Max(2, configMap.GetAutoLayoutProfile().AlignmentTolerance);
            return !FillsBounds(host.Bounds, rootBounds, tolerance);
        }

        private static bool ContainsBounds(PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerBounds childBounds, int tolerance)
        {
            int adjustedTolerance = Math.Max(0, tolerance);
            return childBounds.Left >= parentBounds.Left - adjustedTolerance
                && childBounds.Top >= parentBounds.Top - adjustedTolerance
                && GetRight(childBounds) <= GetRight(parentBounds) + adjustedTolerance
                && GetBottom(childBounds) <= GetBottom(parentBounds) + adjustedTolerance;
        }

        private static bool FillsBounds(PsdUiToolkitLayerBounds candidateBounds, PsdUiToolkitLayerBounds referenceBounds, int tolerance)
        {
            return Math.Abs(candidateBounds.Left - referenceBounds.Left) <= tolerance
                && Math.Abs(candidateBounds.Top - referenceBounds.Top) <= tolerance
                && Math.Abs(GetRight(candidateBounds) - GetRight(referenceBounds)) <= tolerance
                && Math.Abs(GetBottom(candidateBounds) - GetBottom(referenceBounds)) <= tolerance;
        }

        private static void ApplyAutoLayoutRecursively(RebuildNodeState node, PsdUiToolkitLayerConfigMap configMap, ref int syntheticCounter)
        {
            if (node == null)
                return;

            for (int i = 0; i < node.Children.Count; i++)
                ApplyAutoLayoutRecursively(node.Children[i], configMap, ref syntheticCounter);

            if (node.Children.Count == 0)
            {
                node.LayoutType = PsdUiToolkitLayoutType.Absolute;
                node.Confidence = 0f;
                node.AnalysisSummary = node.IsSynthetic
                    ? "Synthetic node has no children; keep absolute export."
                    : "Leaf node after containment rebuild; keep absolute export.";
                return;
            }

            LayoutAnalysisResult analysis = AnalyzeNodeLayout(node, node.Children, configMap);
            if (CanUseDirectLayout(node, analysis, configMap))
            {
                node.LayoutType = analysis.LayoutType;
                node.Confidence = analysis.Confidence;
                node.AnalysisSummary = analysis.Summary;
                return;
            }

            node.LayoutType = PsdUiToolkitLayoutType.Absolute;
            node.Confidence = 0f;
            node.AnalysisSummary = analysis.Summary;

            if (TryInsertSyntheticContainers(node.Children, node.Bounds, configMap, node.DisplayName, ref syntheticCounter))
            {
                analysis = AnalyzeNodeLayout(node, node.Children, configMap);
                if (CanUseDirectLayout(node, analysis, configMap))
                {
                    node.LayoutType = analysis.LayoutType;
                    node.Confidence = analysis.Confidence;
                    node.AnalysisSummary = analysis.Summary;
                    return;
                }

                node.LayoutType = PsdUiToolkitLayoutType.Absolute;
                node.Confidence = 0f;
                node.AnalysisSummary = analysis.Summary;
            }
        }

        private static bool CanUseDirectLayout(RebuildNodeState node, LayoutAnalysisResult analysis, PsdUiToolkitLayerConfigMap configMap)
        {
            if (analysis.LayoutType == PsdUiToolkitLayoutType.Absolute)
                return false;

            return CanAssignLayoutDirectly(node)
                && PreservesFlowOrder(node.Children, node.Bounds, analysis.LayoutType, configMap);
        }

        private static bool CanAssignLayoutDirectly(RebuildNodeState node)
        {
            if (node == null)
                return false;
            if (node.IsSynthetic)
                return true;
            if (node.SourceLayer == null || node.SourceLayer.Kind == LayerKind.Type)
                return false;

            return true;
        }

        private static bool TryInsertSyntheticContainers(
            List<RebuildNodeState> siblings,
            PsdUiToolkitLayerBounds parentBounds,
            PsdUiToolkitLayerConfigMap configMap,
            string syntheticPrefix,
            ref int syntheticCounter)
        {
            if (!configMap.GetAutoLayoutConfig().allowVirtualContainers)
                return false;

            bool changed = false;
            int index = 0;
            while (index < siblings.Count)
            {
                if (!CanParticipateInSyntheticFlow(siblings[index], parentBounds, configMap))
                {
                    index++;
                    continue;
                }

                int start = index;
                List<RebuildNodeState> segment = new List<RebuildNodeState>();
                while (index < siblings.Count && CanParticipateInSyntheticFlow(siblings[index], parentBounds, configMap))
                {
                    segment.Add(siblings[index]);
                    index++;
                }

                if (segment.Count < configMap.GetAutoLayoutProfile().MinimumVirtualContainerCandidates)
                    continue;

                if (!TryFindBestSyntheticInsertionCandidate(siblings, start, segment.Count, configMap, syntheticPrefix, ref syntheticCounter, out SyntheticInsertionCandidate candidate))
                    continue;

                siblings.RemoveRange(candidate.StartIndex, candidate.Length);
                siblings.Insert(candidate.StartIndex, candidate.Node);
                changed = true;
                index = candidate.StartIndex + 1;
            }

            return changed;
        }

        private static bool TryFindBestSyntheticInsertionCandidate(
            List<RebuildNodeState> siblings,
            int segmentStart,
            int segmentLength,
            PsdUiToolkitLayerConfigMap configMap,
            string syntheticPrefix,
            ref int syntheticCounter,
            out SyntheticInsertionCandidate candidate)
        {
            candidate = default;
            if (siblings == null || segmentLength < 2)
                return false;

            int segmentEnd = segmentStart + segmentLength;
            for (int length = segmentLength; length >= 2; length--)
            {
                bool foundForLength = false;
                SyntheticInsertionCandidate bestForLength = default;
                for (int start = segmentStart; start + length <= segmentEnd; start++)
                {
                    List<RebuildNodeState> window = siblings.GetRange(start, length);
                    RebuildNodeState syntheticNode = TryCreateSyntheticNode(window, configMap, syntheticPrefix, ref syntheticCounter);
                    if (syntheticNode == null)
                        continue;

                    SyntheticInsertionCandidate current = new SyntheticInsertionCandidate(start, length, syntheticNode);
                    if (!foundForLength || current.Confidence > bestForLength.Confidence)
                    {
                        bestForLength = current;
                        foundForLength = true;
                    }
                }

                if (foundForLength)
                {
                    candidate = bestForLength;
                    return true;
                }
            }

            return false;
        }

        private static bool CanParticipateInSyntheticFlow(RebuildNodeState node, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            if (node == null)
                return false;
            if (node.IsSynthetic)
                return node.LayoutType != PsdUiToolkitLayoutType.Overlay;

            return IsFlowCandidate(node, parentBounds, configMap);
        }

        private static RebuildNodeState TryCreateSyntheticNode(
            List<RebuildNodeState> segment,
            PsdUiToolkitLayerConfigMap configMap,
            string syntheticPrefix,
            ref int syntheticCounter)
        {
            if (segment == null || segment.Count < configMap.GetAutoLayoutProfile().MinimumVirtualContainerCandidates)
                return null;

            RebuildNodeState syntheticNode = new RebuildNodeState
            {
                Bounds = ComputeUnionBounds(segment),
                DrawOrder = segment[0].DrawOrder,
                IsSynthetic = true,
            };
            syntheticNode.Children.AddRange(segment);

            LayoutAnalysisResult analysis = AnalyzeNodeLayout(syntheticNode, syntheticNode.Children, configMap);
            if (analysis.LayoutType == PsdUiToolkitLayoutType.Absolute || analysis.LayoutType == PsdUiToolkitLayoutType.Overlay)
                return null;
            if (!PreservesFlowOrder(syntheticNode.Children, syntheticNode.Bounds, analysis.LayoutType, configMap))
                return null;

            syntheticNode.LayoutType = analysis.LayoutType;
            syntheticNode.Confidence = analysis.Confidence;
            syntheticNode.AnalysisSummary = analysis.Summary;
            syntheticNode.DisplayName = $"{SanitizeSyntheticPrefix(syntheticPrefix)}_Auto_{syntheticCounter++}";
            syntheticNode.RebuildReason = $"Wrapped {segment.Count} contiguous children into a synthetic {analysis.LayoutType} container.";
            return syntheticNode;
        }

        private static PsdUiToolkitLayerBounds ComputeUnionBounds(List<RebuildNodeState> nodes)
        {
            int minLeft = int.MaxValue;
            int minTop = int.MaxValue;
            int maxRight = int.MinValue;
            int maxBottom = int.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                minLeft = Math.Min(minLeft, nodes[i].Bounds.Left);
                minTop = Math.Min(minTop, nodes[i].Bounds.Top);
                maxRight = Math.Max(maxRight, GetRight(nodes[i].Bounds));
                maxBottom = Math.Max(maxBottom, GetBottom(nodes[i].Bounds));
            }

            return new PsdUiToolkitLayerBounds(minLeft, minTop, Math.Max(0, maxRight - minLeft), Math.Max(0, maxBottom - minTop));
        }

        private static string SanitizeSyntheticPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Auto";

            string sanitized = value.Trim();
            return sanitized.Replace(' ', '_');
        }

        private static LayoutAnalysisResult AnalyzeNodeLayout(
            RebuildNodeState node,
            List<RebuildNodeState> childNodes,
            PsdUiToolkitLayerConfigMap configMap)
        {
            if (!configMap.IsAutoLayoutEnabled())
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "Auto-layout disabled in PSD settings; keep absolute export.");
            if (!node.IsSynthetic && !configMap.ParticipateInAutoLayout(node.SourceLayer))
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, "Layer opted out of auto-layout participation.");

            PsdUiToolkitAutoLayoutDetectionProfile profile = configMap.GetAutoLayoutProfile();
            List<RebuildNodeState> flowCandidates = GetFlowCandidates(childNodes, node.Bounds, configMap);
            if (flowCandidates.Count < profile.MinimumFlowCandidates)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, $"{profile.Mode}: found {flowCandidates.Count} flow candidates; requires at least {profile.MinimumFlowCandidates}.");

            LayoutScore rowScore = ScoreRow(flowCandidates, profile);
            LayoutScore columnScore = ScoreColumn(flowCandidates, profile);
            LayoutScore gridScore = ScoreGrid(flowCandidates, profile);
            LayoutAnalysisResult best = SelectBestLayout(rowScore, columnScore, gridScore, profile);
            if (best.LayoutType != PsdUiToolkitLayoutType.Absolute)
                return best;
            if (!string.IsNullOrEmpty(best.Summary))
                return best;

            return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, $"{profile.Mode}: no row, column, or grid candidate was detected; keep absolute export.");
        }

        private static LayoutAnalysisResult SelectBestLayout(LayoutScore rowScore, LayoutScore columnScore, LayoutScore gridScore, PsdUiToolkitAutoLayoutDetectionProfile profile)
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
            if (best.Confidence < profile.MinimumConfidence)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, $"{profile.Mode}: best candidate {best.LayoutType} scored {best.Confidence:0.##}, below minimum confidence {profile.MinimumConfidence:0.##}.");
            if (second > 0f && best.Confidence - second < profile.AmbiguityGap)
                return new LayoutAnalysisResult(PsdUiToolkitLayoutType.Absolute, 0f, $"{profile.Mode}: candidates were ambiguous ({best.LayoutType} {best.Confidence:0.##} vs {candidates[1].LayoutType} {second:0.##}; required gap {profile.AmbiguityGap:0.##}).");

            return new LayoutAnalysisResult(best.LayoutType, best.Confidence, $"{profile.Mode}: {best.Summary}");
        }

        private static List<RebuildNodeState> GetFlowCandidates(List<RebuildNodeState> childNodes, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            List<RebuildNodeState> flowCandidates = new List<RebuildNodeState>();
            for (int i = 0; i < childNodes.Count; i++)
            {
                RebuildNodeState child = childNodes[i];
                if (IsFlowCandidate(child, parentBounds, configMap))
                    flowCandidates.Add(child);
            }

            return flowCandidates;
        }

        private static bool IsFlowCandidate(RebuildNodeState childNode, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            if (childNode == null)
                return false;
            if (childNode.IsSynthetic)
                return childNode.LayoutType != PsdUiToolkitLayoutType.Overlay;

            Layer childLayer = childNode.SourceLayer;
            if (childLayer == null)
                return false;

            if (!configMap.ParticipateInAutoLayout(childLayer))
                return false;
            if (IsBackgroundLike(childNode, parentBounds, configMap))
                return false;

            return true;
        }

        private static bool IsBackgroundLike(RebuildNodeState childNode, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayerConfigMap configMap)
        {
            if (childNode?.SourceLayer == null)
                return false;
            if (!configMap.GetAutoLayoutConfig().detectBackgroundContainers)
                return false;
            if (childNode.SourceLayer.Kind == LayerKind.Type)
                return false;

            PsdUiToolkitAutoLayoutDetectionProfile profile = configMap.GetAutoLayoutProfile();
            int tolerance = Math.Max(2, profile.AlignmentTolerance);
            bool fillsParent = Math.Abs(childNode.Bounds.Left - parentBounds.Left) <= tolerance
                && Math.Abs(childNode.Bounds.Top - parentBounds.Top) <= tolerance
                && Math.Abs(GetRight(childNode.Bounds) - GetRight(parentBounds)) <= tolerance
                && Math.Abs(GetBottom(childNode.Bounds) - GetBottom(parentBounds)) <= tolerance;
            float parentArea = Math.Max(1f, parentBounds.Width * parentBounds.Height);
            float childArea = Math.Max(1f, childNode.Bounds.Width * childNode.Bounds.Height);
            float fillRatio = childArea / parentArea;
            return fillsParent || fillRatio >= profile.BackgroundFillThreshold;
        }

        private static bool PreservesFlowOrder(List<RebuildNodeState> childNodes, PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayoutType layoutType, PsdUiToolkitLayerConfigMap configMap)
        {
            if (layoutType != PsdUiToolkitLayoutType.Row && layoutType != PsdUiToolkitLayoutType.Column && layoutType != PsdUiToolkitLayoutType.Grid)
                return true;

            List<RebuildNodeState> flowChildren = GetFlowCandidates(childNodes, parentBounds, configMap);
            if (flowChildren.Count <= 1)
                return true;

            List<RebuildNodeState> geometryOrdered = new List<RebuildNodeState>(flowChildren);
            geometryOrdered.Sort(layoutType == PsdUiToolkitLayoutType.Row ? (Comparison<RebuildNodeState>)CompareByLeftThenTop : CompareByTopThenLeft);
            for (int i = 0; i < flowChildren.Count; i++)
            {
                if (flowChildren[i].DrawOrder != geometryOrdered[i].DrawOrder)
                    return false;
            }

            return true;
        }

        private static LayoutScore ScoreRow(List<RebuildNodeState> flowCandidates, PsdUiToolkitAutoLayoutDetectionProfile profile)
        {
            if (flowCandidates.Count < profile.MinimumFlowCandidates)
                return new LayoutScore(PsdUiToolkitLayoutType.Row, 0f, string.Empty);

            List<RebuildNodeState> sorted = new List<RebuildNodeState>(flowCandidates);
            sorted.Sort(CompareByLeftThenTop);
            float tolerance = Math.Max(1f, profile.AlignmentTolerance);
            float gapTolerance = Math.Max(1f, profile.GapTolerance);
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
            float confidence = profile.FlowAlignmentWeight * alignmentScore
                + profile.FlowGapWeight * gapScore
                + profile.FlowOverlapWeight * overlapScore
                + profile.FlowSpanWeight * spanScore;
            string summary = $"Row heuristic: {anchor} aligned (avg deviation {alignmentDeviation:0.#}), avg gap {averageGap:0.#}, gap deviation {gapDeviation:0.#}, overlap score {overlapScore:0.##}.";
            return new LayoutScore(PsdUiToolkitLayoutType.Row, confidence, summary);
        }

        private static LayoutScore ScoreColumn(List<RebuildNodeState> flowCandidates, PsdUiToolkitAutoLayoutDetectionProfile profile)
        {
            if (flowCandidates.Count < profile.MinimumFlowCandidates)
                return new LayoutScore(PsdUiToolkitLayoutType.Column, 0f, string.Empty);

            List<RebuildNodeState> sorted = new List<RebuildNodeState>(flowCandidates);
            sorted.Sort(CompareByTopThenLeft);
            float tolerance = Math.Max(1f, profile.AlignmentTolerance);
            float gapTolerance = Math.Max(1f, profile.GapTolerance);
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
            float confidence = profile.FlowAlignmentWeight * alignmentScore
                + profile.FlowGapWeight * gapScore
                + profile.FlowOverlapWeight * overlapScore
                + profile.FlowSpanWeight * spanScore;
            string summary = $"Column heuristic: {anchor} aligned (avg deviation {alignmentDeviation:0.#}), avg gap {averageGap:0.#}, gap deviation {gapDeviation:0.#}, overlap score {overlapScore:0.##}.";
            return new LayoutScore(PsdUiToolkitLayoutType.Column, confidence, summary);
        }

        private static LayoutScore ScoreGrid(List<RebuildNodeState> flowCandidates, PsdUiToolkitAutoLayoutDetectionProfile profile)
        {
            if (flowCandidates.Count < profile.MinimumGridCandidates)
                return new LayoutScore(PsdUiToolkitLayoutType.Grid, 0f, string.Empty);

            int clusterTolerance = Math.Max(profile.AlignmentTolerance, profile.GapTolerance);
            List<List<RebuildNodeState>> rows = ClusterNodes(flowCandidates, true, clusterTolerance);
            List<List<RebuildNodeState>> columns = ClusterNodes(flowCandidates, false, clusterTolerance);
            if (rows.Count < 2 || columns.Count < 2)
                return new LayoutScore(PsdUiToolkitLayoutType.Grid, 0f, string.Empty);

            int expectedColumns = columns.Count;
            float occupancy = Clamp01(flowCandidates.Count / (float)Math.Max(1, rows.Count * Math.Max(2, expectedColumns)));
            float sizeScore = ComputeSizeConsistencyScore(flowCandidates);
            float alignmentScore = ComputeGridAlignmentScore(rows, columns, clusterTolerance);
            float gapScore = ComputeGridGapScore(rows, clusterTolerance);
            float overlapScore = ComputePairwiseOverlapScore(flowCandidates, Math.Max(1f, profile.GapTolerance));
            float confidence = profile.GridOccupancyWeight * occupancy
                + profile.GridSizeWeight * sizeScore
                + profile.GridAlignmentWeight * alignmentScore
                + profile.GridGapWeight * gapScore
                + profile.GridOverlapWeight * overlapScore;
            string summary = $"Grid heuristic: {rows.Count} rows x {columns.Count} columns, occupancy {occupancy:0.##}, size score {sizeScore:0.##}, alignment score {alignmentScore:0.##}.";
            return new LayoutScore(PsdUiToolkitLayoutType.Grid, confidence, summary);
        }

        private static float ComputeAlignmentScore(List<RebuildNodeState> nodes, Func<RebuildNodeState, float> selector, float tolerance, out float deviation)
        {
            List<float> values = new List<float>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                values.Add(selector(nodes[i]));

            float pivot = Average(values);
            deviation = AverageAbsoluteDeviation(values, pivot);
            return 1f - Clamp01(deviation / Math.Max(1f, tolerance));
        }

        private static void ComputeMainAxisMetrics(List<RebuildNodeState> sorted, bool horizontal, float gapTolerance, out float overlapScore, out float gapScore, out float averageGap, out float gapDeviation)
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

        private static float ComputeSpanScore(List<RebuildNodeState> nodes, bool horizontal)
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

        private static List<List<RebuildNodeState>> ClusterNodes(List<RebuildNodeState> nodes, bool clusterByTop, int tolerance)
        {
            List<RebuildNodeState> sorted = new List<RebuildNodeState>(nodes);
            sorted.Sort(clusterByTop ? (Comparison<RebuildNodeState>)CompareByTopThenLeft : CompareByLeftThenTop);

            List<List<RebuildNodeState>> clusters = new List<List<RebuildNodeState>>();
            List<float> anchors = new List<float>();
            for (int i = 0; i < sorted.Count; i++)
            {
                RebuildNodeState node = sorted[i];
                float value = clusterByTop ? GetTop(node) : GetLeft(node);
                if (clusters.Count == 0 || Math.Abs(value - anchors[anchors.Count - 1]) > tolerance)
                {
                    clusters.Add(new List<RebuildNodeState> { node });
                    anchors.Add(value);
                }
                else
                {
                    List<RebuildNodeState> cluster = clusters[clusters.Count - 1];
                    cluster.Add(node);
                    anchors[anchors.Count - 1] = ((anchors[anchors.Count - 1] * (cluster.Count - 1)) + value) / cluster.Count;
                }
            }

            return clusters;
        }

        private static float ComputeSizeConsistencyScore(List<RebuildNodeState> nodes)
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

        private static float ComputeGridAlignmentScore(List<List<RebuildNodeState>> rows, List<List<RebuildNodeState>> columns, int tolerance)
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

        private static float ComputeGridGapScore(List<List<RebuildNodeState>> rows, int tolerance)
        {
            List<float> rowGaps = new List<float>();
            List<float> columnGaps = new List<float>();
            int previousRowBottom = 0;
            bool hasPreviousRow = false;
            for (int i = 0; i < rows.Count; i++)
            {
                List<RebuildNodeState> row = new List<RebuildNodeState>(rows[i]);
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

        private static float ComputePairwiseOverlapScore(List<RebuildNodeState> nodes, float tolerance)
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

        private static void ValidateDrawOrderOrThrow(List<RebuildNodeState> rebuiltNodes, List<int> expectedDrawOrder)
        {
            List<int> actualDrawOrder = new List<int>(expectedDrawOrder.Count);
            FlattenRealDrawOrder(rebuiltNodes, actualDrawOrder);
            if (actualDrawOrder.Count != expectedDrawOrder.Count)
                throw new InvalidOperationException($"Rebuilt layout tree changed the number of real nodes ({actualDrawOrder.Count} vs {expectedDrawOrder.Count}).");

            for (int i = 0; i < expectedDrawOrder.Count; i++)
            {
                if (actualDrawOrder[i] != expectedDrawOrder[i])
                    throw new InvalidOperationException($"Rebuilt layout tree changed real-node draw order at index {i} ({actualDrawOrder[i]} vs {expectedDrawOrder[i]}). Rebuild mode requires exact order preservation.");
            }
        }

        private static void FlattenRealDrawOrder(List<RebuildNodeState> nodes, List<int> drawOrder)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                RebuildNodeState node = nodes[i];
                if (!node.IsSynthetic)
                    drawOrder.Add(node.DrawOrder);
                if (node.Children.Count > 0)
                    FlattenRealDrawOrder(node.Children, drawOrder);
            }
        }

        private static int CompareByLeftThenTop(RebuildNodeState left, RebuildNodeState right)
        {
            int compare = GetLeft(left).CompareTo(GetLeft(right));
            return compare != 0 ? compare : GetTop(left).CompareTo(GetTop(right));
        }

        private static int CompareByTopThenLeft(RebuildNodeState left, RebuildNodeState right)
        {
            int compare = GetTop(left).CompareTo(GetTop(right));
            return compare != 0 ? compare : GetLeft(left).CompareTo(GetLeft(right));
        }

        private static int GetLeft(RebuildNodeState node) => node?.Bounds.Left ?? 0;
        private static int GetTop(RebuildNodeState node) => node?.Bounds.Top ?? 0;
        private static int GetRight(RebuildNodeState node) => node == null ? 0 : node.Bounds.Left + node.Bounds.Width;
        private static int GetBottom(RebuildNodeState node) => node == null ? 0 : node.Bounds.Top + node.Bounds.Height;
        private static int GetRight(PsdUiToolkitLayerBounds bounds) => bounds.Left + bounds.Width;
        private static int GetBottom(PsdUiToolkitLayerBounds bounds) => bounds.Top + bounds.Height;
        private static float GetCenterX(RebuildNodeState node) => GetLeft(node) + (node?.Bounds.Width ?? 0) * 0.5f;
        private static float GetCenterY(RebuildNodeState node) => GetTop(node) + (node?.Bounds.Height ?? 0) * 0.5f;

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
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private static List<PsdUiToolkitLayoutNode> ConvertNodes(List<RebuildNodeState> rebuiltNodes)
        {
            List<PsdUiToolkitLayoutNode> result = new List<PsdUiToolkitLayoutNode>(rebuiltNodes.Count);
            for (int i = 0; i < rebuiltNodes.Count; i++)
            {
                RebuildNodeState state = rebuiltNodes[i];
                result.Add(new PsdUiToolkitLayoutNode(
                    state.SourceLayer,
                    state.Bounds,
                    state.Children.Count == 0,
                    state.LayoutType,
                    state.Confidence,
                    state.AnalysisSummary,
                    state.DrawOrder,
                    ConvertNodes(state.Children),
                    state.DisplayName,
                    state.IsSynthetic,
                    state.RebuildReason));
            }

            return result;
        }
    }
}

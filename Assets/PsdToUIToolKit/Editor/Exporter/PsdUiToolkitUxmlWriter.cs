using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using PsdTools.Layers;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitUxmlWriter
    {
        private readonly struct FlowChildPlacement
        {
            public FlowChildPlacement(bool useFlow, int marginLeft, int marginTop)
            {
                UseFlow = useFlow;
                MarginLeft = Math.Max(0, marginLeft);
                MarginTop = Math.Max(0, marginTop);
            }

            public bool UseFlow { get; }
            public int MarginLeft { get; }
            public int MarginTop { get; }

            public static FlowChildPlacement Absolute => new FlowChildPlacement(false, 0, 0);
        }

        private sealed class GridRowPlan
        {
            public List<PsdUiToolkitLayoutNode> Children { get; } = new List<PsdUiToolkitLayoutNode>();
            public Dictionary<PsdUiToolkitLayoutNode, FlowChildPlacement> Placements { get; } = new Dictionary<PsdUiToolkitLayoutNode, FlowChildPlacement>();
            public int GapBefore { get; set; }
            public int Height { get; set; }
        }

        private sealed class FlowContainerPlan
        {
            public PsdUiToolkitLayoutType LayoutType { get; set; }
            public bool UseFlow { get; set; }
            public int PaddingLeft { get; set; }
            public int PaddingTop { get; set; }
            public int PaddingRight { get; set; }
            public int PaddingBottom { get; set; }
            public int InnerWidth { get; set; }
            public List<PsdUiToolkitLayoutNode> FlowChildren { get; } = new List<PsdUiToolkitLayoutNode>();
            public Dictionary<PsdUiToolkitLayoutNode, FlowChildPlacement> Placements { get; } = new Dictionary<PsdUiToolkitLayoutNode, FlowChildPlacement>();
            public List<GridRowPlan> GridRows { get; } = new List<GridRowPlan>();

            public static FlowContainerPlan Disabled(PsdUiToolkitLayoutType layoutType)
            {
                return new FlowContainerPlan
                {
                    LayoutType = layoutType,
                    UseFlow = false,
                };
            }
        }

        public static void Write(
            PsdUiToolkitLayoutTree layoutTree,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping,
            string outputAssetPath)
        {
            if (layoutTree == null)
                throw new ArgumentNullException(nameof(layoutTree));
            if (configMap == null)
                throw new ArgumentNullException(nameof(configMap));
            if (rasterResult == null)
                throw new ArgumentNullException(nameof(rasterResult));

            StringBuilder builder = new StringBuilder(8192);
            builder.AppendLine("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">");
            builder.Append("  <ui:VisualElement");
            builder.Append($" name=\"{EscapeAttribute(layoutTree.RootName)}\"");
            builder.Append($" style=\"position: relative; width: {layoutTree.Width}px; height: {layoutTree.Height}px; overflow: hidden;\"");
            builder.AppendLine(" >");

            foreach (PsdUiToolkitLayoutNode child in layoutTree.Children)
            {
                AppendLayoutNode(builder, child, 2, 0, 0, configMap, rasterResult, fontMapping, FlowChildPlacement.Absolute);
            }

            builder.AppendLine("  </ui:VisualElement>");
            builder.AppendLine("</ui:UXML>");

            string diskPath = PsdUiToolkitAssetPathUtility.GetDiskPath(outputAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath) ?? string.Empty);
            File.WriteAllText(diskPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static void AppendLayoutNode(
            StringBuilder builder,
            PsdUiToolkitLayoutNode node,
            int indentLevel,
            int parentLeft,
            int parentTop,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping,
            FlowChildPlacement placement)
        {
            if (node == null)
                return;
            Layer layer = node.SourceLayer;
            bool isSynthetic = node.IsSynthetic || layer == null || layer.LayerId == null;
            if (!isSynthetic && !configMap.IsExported(layer))
                return;
            if (!isSynthetic && rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
                return;

            PsdUiToolkitLayerBounds bounds = node.Bounds;
            int left = bounds.Left - parentLeft;
            int top = bounds.Top - parentTop;

            PsdUiToolkitRasterAssetInfo rasterInfo = null;
            bool hasRaster = !isSynthetic && rasterResult.AssetsByLayerId.TryGetValue(layer.LayerId.Value, out rasterInfo);
            bool hasChildren = node.Children.Count > 0;
            bool renderAsLeaf = !hasChildren && (isSynthetic || node.RenderAsLeaf || !layer.IsGroup || hasRaster || rasterResult.CompositeLeafLayerIds.Contains(layer.LayerId.Value));

            string indent = new string(' ', indentLevel * 2);
            string elementName = isSynthetic
                ? (string.IsNullOrEmpty(node.DisplayName) ? $"Auto_{node.OriginalIndex}" : node.DisplayName)
                : (string.IsNullOrEmpty(layer.Name) ? $"Layer_{layer.LayerId.Value}" : layer.Name);
            FlowContainerPlan flowPlan = BuildFlowContainerPlan(node, configMap);
            string style = BuildStyle(node, layer, bounds, left, top, configMap, rasterInfo, fontMapping, placement, flowPlan);

            if (!isSynthetic && layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                string rawText = typeLayer.Text;

                if (PsdUiToolkitTextEffectsHelper.TryGetTextGradientCornersFromLayer(layer, out Color32 cTL, out Color32 cTR, out Color32 cBL, out Color32 cBR))
                {
                    string folder = EnsureAndGetGradientFolder();
                    string layerNameSanitized = PsdUiToolkitAssetPathUtility.SanitizeFileName(layer.Name);
                    string assetName = $"Gradient_{layer.LayerId}_{layerNameSanitized}";
                    string assetPath = $"{folder}/{assetName}.asset";
                    
                    UnityEngine.TextCore.Text.TextColorGradient gradientAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.TextColorGradient>(assetPath);
                    if (gradientAsset == null)
                    {
                        gradientAsset = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.TextCore.Text.TextColorGradient>();
                        gradientAsset.colorMode = UnityEngine.TextCore.Text.ColorGradientMode.FourCornersGradient;
                        gradientAsset.topLeft = cTL;
                        gradientAsset.topRight = cTR;
                        gradientAsset.bottomLeft = cBL;
                        gradientAsset.bottomRight = cBR;
                        UnityEditor.AssetDatabase.CreateAsset(gradientAsset, assetPath);
                    }
                    else
                    {
                        gradientAsset.colorMode = UnityEngine.TextCore.Text.ColorGradientMode.FourCornersGradient;
                        gradientAsset.topLeft = cTL;
                        gradientAsset.topRight = cTR;
                        gradientAsset.bottomLeft = cBL;
                        gradientAsset.bottomRight = cBR;
                        UnityEditor.EditorUtility.SetDirty(gradientAsset);
                    }
                    UnityEditor.AssetDatabase.SaveAssets();

                    rawText = $"<gradient=\"{assetName}\">{rawText}</gradient>";
                }

                string richTextAttr = rawText.Contains("<gradient=") ? " enable-rich-text=\"true\"" : "";

                builder.Append(indent);
                builder.Append($"<ui:Label name=\"{EscapeAttribute(elementName)}\" text=\"{EscapeAttribute(rawText)}\"{richTextAttr} style=\"{EscapeAttribute(style)}\" />");
                builder.AppendLine();
                return;
            }

            if (!hasChildren)
            {
                builder.Append(indent);
                builder.Append($"<ui:VisualElement name=\"{EscapeAttribute(elementName)}\" style=\"{EscapeAttribute(style)}\" />");
                builder.AppendLine();
                return;
            }

            builder.Append(indent);
            builder.Append($"<ui:VisualElement name=\"{EscapeAttribute(elementName)}\" style=\"{EscapeAttribute(style)}\"");
            builder.AppendLine(" >");

            if (flowPlan.UseFlow && flowPlan.LayoutType == PsdUiToolkitLayoutType.Grid)
            {
                AppendGridChildren(builder, node, flowPlan, indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping);
            }
            else if (flowPlan.UseFlow)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    PsdUiToolkitLayoutNode child = node.Children[i];
                    FlowChildPlacement childPlacement = flowPlan.Placements.TryGetValue(child, out FlowChildPlacement resolvedPlacement)
                        ? resolvedPlacement
                        : FlowChildPlacement.Absolute;
                    AppendLayoutNode(builder, child, indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping, childPlacement);
                }
            }
            else
            {
                for (int i = 0; i < node.Children.Count; i++)
                    AppendLayoutNode(builder, node.Children[i], indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping, FlowChildPlacement.Absolute);
            }

            builder.Append(indent);
            builder.AppendLine("</ui:VisualElement>");
        }

        private static string BuildStyle(
            PsdUiToolkitLayoutNode node,
            Layer layer,
            PsdUiToolkitLayerBounds bounds,
            int left,
            int top,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterAssetInfo rasterInfo,
            PsdUiToolkitFontMappingLookup fontMapping,
            FlowChildPlacement placement,
            FlowContainerPlan flowPlan)
        {
            StringBuilder style = new StringBuilder(256);
            if (placement.UseFlow)
            {
                style.Append("position: relative;");
                style.AppendFormat(CultureInfo.InvariantCulture, " width: {0}px; height: {1}px;", bounds.Width, bounds.Height);
                if (placement.MarginLeft > 0)
                    style.AppendFormat(CultureInfo.InvariantCulture, " margin-left: {0}px;", placement.MarginLeft);
                if (placement.MarginTop > 0)
                    style.AppendFormat(CultureInfo.InvariantCulture, " margin-top: {0}px;", placement.MarginTop);
                style.Append(" flex-shrink: 0;");
            }
            else
            {
                style.Append("position: absolute;");
                style.AppendFormat(CultureInfo.InvariantCulture, " left: {0}px; top: {1}px; width: {2}px; height: {3}px;", left, top, bounds.Width, bounds.Height);
            }

            if (layer != null)
            {
                style.AppendFormat(CultureInfo.InvariantCulture, " opacity: {0:0.###};", layer.OpacityFloat);
                style.Append(configMap.IsVisible(layer) ? " display: flex;" : " display: none;");
            }
            else
            {
                style.Append(" opacity: 1; display: flex;");
            }

            AppendFlowContainerStyle(style, flowPlan);

            if (layer == null)
                return style.ToString().Trim();

            if (layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                style.AppendFormat(CultureInfo.InvariantCulture, " font-size: {0:0.##}px;", typeLayer.EffectiveFontSize);
                style.Append(" white-space: normal; -unity-text-align: middle-center;");
                string fontUri = fontMapping?.ResolveStyleUri(typeLayer.PsdFontName);
                if (!string.IsNullOrEmpty(fontUri))
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-font-definition: url('{0}');", fontUri);
                if (typeLayer.FillColor != null && typeLayer.FillColor.Length >= 4)
                {
                    int red = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[1] * 255f), 0, 255);
                    int green = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[2] * 255f), 0, 255);
                    int blue = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[3] * 255f), 0, 255);
                    float alpha = Mathf.Clamp01(typeLayer.FillColor[0]);
                    style.AppendFormat(CultureInfo.InvariantCulture, " color: rgba({0}, {1}, {2}, {3:0.###});", red, green, blue, alpha);
                }

                if (PsdUiToolkitTextEffectsHelper.TryGetStrokeEffect(layer, out Color strokeColor, out float strokeSize))
                {
                    int sr = Mathf.Clamp(Mathf.RoundToInt(strokeColor.r * 255f), 0, 255);
                    int sg = Mathf.Clamp(Mathf.RoundToInt(strokeColor.g * 255f), 0, 255);
                    int sb = Mathf.Clamp(Mathf.RoundToInt(strokeColor.b * 255f), 0, 255);
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-text-outline-width: {0:0.##}px;", strokeSize);
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-text-outline-color: rgba({0}, {1}, {2}, {3:0.###});", sr, sg, sb, strokeColor.a);
                }

                return style.ToString().Trim();
            }

            if (rasterInfo != null && !string.IsNullOrEmpty(rasterInfo.StyleImageUri))
            {
                style.AppendFormat(CultureInfo.InvariantCulture, " background-image: url('{0}');", rasterInfo.StyleImageUri);
                style.Append(" background-repeat: no-repeat; -unity-background-scale-mode: stretch-to-fill;");
                if (rasterInfo.SliceBorder.HasValue)
                {
                    Vector4 border = rasterInfo.SliceBorder.Value;
                    style.AppendFormat(CultureInfo.InvariantCulture,
                        " -unity-slice-left: {0:0.###}; -unity-slice-bottom: {1:0.###}; -unity-slice-right: {2:0.###}; -unity-slice-top: {3:0.###};",
                        border.x, border.y, border.z, border.w);
                }
            }

            return style.ToString().Trim();
        }

        private static void AppendFlowContainerStyle(StringBuilder style, FlowContainerPlan flowPlan)
        {
            if (style == null || flowPlan == null || !flowPlan.UseFlow)
                return;

            switch (flowPlan.LayoutType)
            {
                case PsdUiToolkitLayoutType.Row:
                    style.Append(" flex-direction: row; align-items: flex-start;");
                    break;
                case PsdUiToolkitLayoutType.Column:
                    style.Append(" flex-direction: column; align-items: flex-start;");
                    break;
                case PsdUiToolkitLayoutType.Grid:
                    style.Append(" flex-direction: column; align-items: stretch;");
                    break;
            }

            style.AppendFormat(CultureInfo.InvariantCulture,
                " padding-left: {0}px; padding-top: {1}px; padding-right: {2}px; padding-bottom: {3}px;",
                flowPlan.PaddingLeft,
                flowPlan.PaddingTop,
                flowPlan.PaddingRight,
                flowPlan.PaddingBottom);
        }

        private static FlowContainerPlan BuildFlowContainerPlan(PsdUiToolkitLayoutNode node, PsdUiToolkitLayerConfigMap configMap)
        {
            if (node == null || node.RenderAsLeaf || node.Children.Count == 0)
                return FlowContainerPlan.Disabled(node?.LayoutType ?? PsdUiToolkitLayoutType.Absolute);

            if (node.LayoutType != PsdUiToolkitLayoutType.Row && node.LayoutType != PsdUiToolkitLayoutType.Column && node.LayoutType != PsdUiToolkitLayoutType.Grid)
                return FlowContainerPlan.Disabled(node.LayoutType);

            PsdUiToolkitAutoLayoutGlobalConfig autoLayout = configMap.GetAutoLayoutConfig();
            if (!autoLayout.ShouldAnalyze || node.Confidence < autoLayout.minimumConfidence)
                return FlowContainerPlan.Disabled(node.LayoutType);

            FlowContainerPlan plan = new FlowContainerPlan
            {
                LayoutType = node.LayoutType,
                UseFlow = true,
            };

            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = node.Children[i];
                if (ShouldRenderAsFlowItem(node, child, configMap))
                    plan.FlowChildren.Add(child);
            }

            if ((node.LayoutType == PsdUiToolkitLayoutType.Row || node.LayoutType == PsdUiToolkitLayoutType.Column) && plan.FlowChildren.Count < 2)
                return FlowContainerPlan.Disabled(node.LayoutType);
            if (node.LayoutType == PsdUiToolkitLayoutType.Grid && plan.FlowChildren.Count < 4)
                return FlowContainerPlan.Disabled(node.LayoutType);

            ComputeContainerPadding(node, plan);
            plan.InnerWidth = Math.Max(0, node.Bounds.Width - plan.PaddingLeft - plan.PaddingRight);

            switch (node.LayoutType)
            {
                case PsdUiToolkitLayoutType.Row:
                    BuildRowPlacements(node, plan);
                    break;
                case PsdUiToolkitLayoutType.Column:
                    BuildColumnPlacements(node, plan);
                    break;
                case PsdUiToolkitLayoutType.Grid:
                    BuildGridPlans(node, plan, configMap);
                    break;
            }

            if (plan.LayoutType == PsdUiToolkitLayoutType.Grid && plan.GridRows.Count == 0)
                return FlowContainerPlan.Disabled(node.LayoutType);
            if ((plan.LayoutType == PsdUiToolkitLayoutType.Row || plan.LayoutType == PsdUiToolkitLayoutType.Column) && plan.Placements.Count == 0)
                return FlowContainerPlan.Disabled(node.LayoutType);

            return plan;
        }

        private static bool ShouldRenderAsFlowItem(PsdUiToolkitLayoutNode parentNode, PsdUiToolkitLayoutNode childNode, PsdUiToolkitLayerConfigMap configMap)
        {
            if (parentNode == null || childNode == null)
                return false;

            if (childNode.IsSynthetic)
                return childNode.LayoutType != PsdUiToolkitLayoutType.Overlay;

            if (!configMap.ParticipateInAutoLayout(childNode.SourceLayer))
                return false;
            if (IsBackgroundLike(parentNode.Bounds, childNode, configMap))
                return false;

            return true;
        }

        private static bool IsBackgroundLike(PsdUiToolkitLayerBounds parentBounds, PsdUiToolkitLayoutNode childNode, PsdUiToolkitLayerConfigMap configMap)
        {
            if (childNode?.SourceLayer == null)
                return false;
            if (!configMap.GetAutoLayoutConfig().detectBackgroundContainers)
                return false;
            if (childNode.SourceLayer.Kind == LayerKind.Type)
                return false;

            int tolerance = Math.Max(2, configMap.GetAutoLayoutConfig().alignmentTolerance);
            bool fillsParent = Math.Abs(childNode.Bounds.Left - parentBounds.Left) <= tolerance
                && Math.Abs(childNode.Bounds.Top - parentBounds.Top) <= tolerance
                && Math.Abs(GetRight(childNode.Bounds) - GetRight(parentBounds)) <= tolerance
                && Math.Abs(GetBottom(childNode.Bounds) - GetBottom(parentBounds)) <= tolerance;
            float parentArea = Math.Max(1f, parentBounds.Width * parentBounds.Height);
            float childArea = Math.Max(1f, childNode.Bounds.Width * childNode.Bounds.Height);
            return fillsParent || (childArea / parentArea) >= 0.72f;
        }

        private static void ComputeContainerPadding(PsdUiToolkitLayoutNode node, FlowContainerPlan plan)
        {
            int minLeft = int.MaxValue;
            int minTop = int.MaxValue;
            int maxRight = int.MinValue;
            int maxBottom = int.MinValue;
            for (int i = 0; i < plan.FlowChildren.Count; i++)
            {
                PsdUiToolkitLayoutNode child = plan.FlowChildren[i];
                minLeft = Math.Min(minLeft, child.Bounds.Left - node.Bounds.Left);
                minTop = Math.Min(minTop, child.Bounds.Top - node.Bounds.Top);
                maxRight = Math.Max(maxRight, GetRight(child.Bounds) - node.Bounds.Left);
                maxBottom = Math.Max(maxBottom, GetBottom(child.Bounds) - node.Bounds.Top);
            }

            if (minLeft == int.MaxValue)
            {
                plan.PaddingLeft = 0;
                plan.PaddingTop = 0;
                plan.PaddingRight = 0;
                plan.PaddingBottom = 0;
                return;
            }

            plan.PaddingLeft = Math.Max(0, minLeft);
            plan.PaddingTop = Math.Max(0, minTop);
            plan.PaddingRight = Math.Max(0, node.Bounds.Width - maxRight);
            plan.PaddingBottom = Math.Max(0, node.Bounds.Height - maxBottom);
        }

        private static void BuildRowPlacements(PsdUiToolkitLayoutNode node, FlowContainerPlan plan)
        {
            int previousRight = 0;
            bool hasPrevious = false;
            for (int i = 0; i < plan.FlowChildren.Count; i++)
            {
                PsdUiToolkitLayoutNode child = plan.FlowChildren[i];
                int childLeft = Math.Max(0, child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                int childTop = Math.Max(0, child.Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                int marginLeft = hasPrevious
                    ? Math.Max(0, childLeft - previousRight)
                    : childLeft;
                int marginTop = childTop;
                plan.Placements[child] = new FlowChildPlacement(true, marginLeft, marginTop);
                previousRight = childLeft + child.Bounds.Width;
                hasPrevious = true;
            }
        }

        private static void BuildColumnPlacements(PsdUiToolkitLayoutNode node, FlowContainerPlan plan)
        {
            int previousBottom = 0;
            bool hasPrevious = false;
            for (int i = 0; i < plan.FlowChildren.Count; i++)
            {
                PsdUiToolkitLayoutNode child = plan.FlowChildren[i];
                int childLeft = Math.Max(0, child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                int childTop = Math.Max(0, child.Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                int marginLeft = childLeft;
                int marginTop = hasPrevious
                    ? Math.Max(0, childTop - previousBottom)
                    : childTop;
                plan.Placements[child] = new FlowChildPlacement(true, marginLeft, marginTop);
                previousBottom = childTop + child.Bounds.Height;
                hasPrevious = true;
            }
        }

        private static void BuildGridPlans(PsdUiToolkitLayoutNode node, FlowContainerPlan plan, PsdUiToolkitLayerConfigMap configMap)
        {
            List<List<PsdUiToolkitLayoutNode>> rows = BuildGridRows(plan.FlowChildren, configMap);
            int previousRowBottom = 0;
            bool hasPreviousRow = false;
            for (int i = 0; i < rows.Count; i++)
            {
                List<PsdUiToolkitLayoutNode> row = rows[i];
                if (row.Count == 0)
                    continue;

                GridRowPlan rowPlan = new GridRowPlan();
                int rowTop = int.MaxValue;
                int rowBottom = int.MinValue;
                for (int j = 0; j < row.Count; j++)
                {
                    rowTop = Math.Min(rowTop, row[j].Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                    rowBottom = Math.Max(rowBottom, GetBottom(row[j].Bounds) - node.Bounds.Top - plan.PaddingTop);
                }

                rowPlan.Height = Math.Max(1, rowBottom - rowTop);
                rowPlan.GapBefore = hasPreviousRow
                    ? Math.Max(0, rowTop - previousRowBottom)
                    : Math.Max(0, rowTop);

                int previousRight = 0;
                bool hasPrevious = false;
                for (int j = 0; j < row.Count; j++)
                {
                    PsdUiToolkitLayoutNode child = row[j];
                    rowPlan.Children.Add(child);
                    int childLeft = Math.Max(0, child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                    int childTop = Math.Max(0, child.Bounds.Top - node.Bounds.Top - plan.PaddingTop - rowTop);
                    int marginLeft = hasPrevious
                        ? Math.Max(0, childLeft - previousRight)
                        : childLeft;
                    rowPlan.Placements[child] = new FlowChildPlacement(true, marginLeft, childTop);
                    previousRight = childLeft + child.Bounds.Width;
                    hasPrevious = true;
                }

                previousRowBottom = rowTop + rowPlan.Height;
                hasPreviousRow = true;
                plan.GridRows.Add(rowPlan);
            }
        }

        private static List<List<PsdUiToolkitLayoutNode>> BuildGridRows(List<PsdUiToolkitLayoutNode> flowChildren, PsdUiToolkitLayerConfigMap configMap)
        {
            List<List<PsdUiToolkitLayoutNode>> rows = new List<List<PsdUiToolkitLayoutNode>>();
            if (flowChildren.Count == 0)
                return rows;

            int tolerance = Math.Max(4, Math.Max(configMap.GetAutoLayoutConfig().alignmentTolerance, configMap.GetAutoLayoutConfig().gapTolerance));
            List<PsdUiToolkitLayoutNode> sorted = new List<PsdUiToolkitLayoutNode>(flowChildren);
            sorted.Sort(CompareByTopThenLeft);
            List<float> anchors = new List<float>();
            for (int i = 0; i < sorted.Count; i++)
            {
                PsdUiToolkitLayoutNode child = sorted[i];
                float top = child.Bounds.Top;
                if (rows.Count == 0 || Math.Abs(top - anchors[anchors.Count - 1]) > tolerance)
                {
                    rows.Add(new List<PsdUiToolkitLayoutNode> { child });
                    anchors.Add(top);
                }
                else
                {
                    List<PsdUiToolkitLayoutNode> row = rows[rows.Count - 1];
                    row.Add(child);
                    anchors[anchors.Count - 1] = ((anchors[anchors.Count - 1] * (row.Count - 1)) + top) / row.Count;
                }
            }

            for (int i = 0; i < rows.Count; i++)
                rows[i].Sort(CompareByLeftThenTop);
            return rows;
        }

        private static void AppendGridChildren(
            StringBuilder builder,
            PsdUiToolkitLayoutNode node,
            FlowContainerPlan flowPlan,
            int indentLevel,
            int parentLeft,
            int parentTop,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping)
        {
            HashSet<PsdUiToolkitLayoutNode> flowChildren = new HashSet<PsdUiToolkitLayoutNode>(flowPlan.FlowChildren);
            bool rowsWritten = false;
            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = node.Children[i];
                if (flowChildren.Contains(child))
                {
                    if (!rowsWritten)
                    {
                        for (int rowIndex = 0; rowIndex < flowPlan.GridRows.Count; rowIndex++)
                            AppendSyntheticGridRow(builder, node, flowPlan, flowPlan.GridRows[rowIndex], rowIndex, indentLevel, parentLeft, parentTop, configMap, rasterResult, fontMapping);
                        rowsWritten = true;
                    }

                    continue;
                }

                AppendLayoutNode(builder, child, indentLevel, node.Bounds.Left, node.Bounds.Top, configMap, rasterResult, fontMapping, FlowChildPlacement.Absolute);
            }

            if (!rowsWritten)
            {
                for (int rowIndex = 0; rowIndex < flowPlan.GridRows.Count; rowIndex++)
                    AppendSyntheticGridRow(builder, node, flowPlan, flowPlan.GridRows[rowIndex], rowIndex, indentLevel, parentLeft, parentTop, configMap, rasterResult, fontMapping);
            }
        }

        private static void AppendSyntheticGridRow(
            StringBuilder builder,
            PsdUiToolkitLayoutNode parentNode,
            FlowContainerPlan flowPlan,
            GridRowPlan rowPlan,
            int rowIndex,
            int indentLevel,
            int parentLeft,
            int parentTop,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping)
        {
            string indent = new string(' ', indentLevel * 2);
            builder.Append(indent);
            builder.Append($"<ui:VisualElement name=\"{EscapeAttribute(GetSyntheticGridRowName(parentNode, rowIndex))}\" style=\"{EscapeAttribute(BuildSyntheticGridRowStyle(flowPlan, rowPlan))}\"");
            builder.AppendLine(" >");

            for (int i = 0; i < rowPlan.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = rowPlan.Children[i];
                FlowChildPlacement placement = rowPlan.Placements.TryGetValue(child, out FlowChildPlacement resolvedPlacement)
                    ? resolvedPlacement
                    : FlowChildPlacement.Absolute;
                AppendLayoutNode(builder, child, indentLevel + 1, parentNode.Bounds.Left, parentNode.Bounds.Top, configMap, rasterResult, fontMapping, placement);
            }

            builder.Append(indent);
            builder.AppendLine("</ui:VisualElement>");
        }

        private static string BuildSyntheticGridRowStyle(FlowContainerPlan flowPlan, GridRowPlan rowPlan)
        {
            StringBuilder style = new StringBuilder(128);
            style.Append("position: relative; display: flex; flex-direction: row; align-items: flex-start; flex-shrink: 0;");
            style.AppendFormat(CultureInfo.InvariantCulture, " width: {0}px; height: {1}px;", Math.Max(1, flowPlan.InnerWidth), Math.Max(1, rowPlan.Height));
            if (rowPlan.GapBefore > 0)
                style.AppendFormat(CultureInfo.InvariantCulture, " margin-top: {0}px;", rowPlan.GapBefore);
            return style.ToString().Trim();
        }

        private static string GetSyntheticGridRowName(PsdUiToolkitLayoutNode parentNode, int rowIndex)
        {
            string parentName = parentNode?.SourceLayer?.Name;
            if (string.IsNullOrEmpty(parentName))
                parentName = parentNode?.SourceLayer?.LayerId?.ToString() ?? "Grid";
            return $"{parentName}_Row_{rowIndex}";
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

        private static int GetRight(PsdUiToolkitLayerBounds bounds) => bounds.Left + bounds.Width;
        private static int GetBottom(PsdUiToolkitLayerBounds bounds) => bounds.Top + bounds.Height;

        private static string EscapeAttribute(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }


        private static string EnsureAndGetGradientFolder()
        {
            string presetPath = "Assets/Resources/Text Color Gradients";
            var folderList = presetPath.Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);

            string currentPath = folderList[0];
            for (int i = 1; i < folderList.Length; i++)
            {
                string nextPath = currentPath + "/" + folderList[i];
                if (!UnityEditor.AssetDatabase.IsValidFolder(nextPath))
                {
                    UnityEditor.AssetDatabase.CreateFolder(currentPath, folderList[i]);
                }
                currentPath = nextPath;
            }
            return currentPath;
        }
    }
}

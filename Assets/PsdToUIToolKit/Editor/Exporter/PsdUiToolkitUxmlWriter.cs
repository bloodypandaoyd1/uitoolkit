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
                AppendLayoutNode(builder, child, 2, 0, 0, configMap, rasterResult, fontMapping, PsdUiToolkitFlowChildPlacement.Absolute);
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
            PsdUiToolkitFlowChildPlacement placement)
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
            PsdUiToolkitFlowContainerPlan flowPlan = PsdUiToolkitFlowLayoutResolver.Resolve(node, configMap);
            string style = BuildStyle(node, layer, bounds, left, top, configMap, rasterInfo, fontMapping, placement, flowPlan);

            if (!isSynthetic && layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                string rawText = NormalizeExplicitLineBreaks(typeLayer.Text, out bool hasExplicitLineBreak);

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

                    // UI Toolkit drops rich-text gradients when an outline is set unless the vertex color is reset inline (UUM-86168).
                    rawText = $"<color=white><gradient=\"{assetName}\">{rawText}</gradient></color>";
                }

                string richTextAttr = hasExplicitLineBreak || rawText.Contains("<gradient=")
                    ? " enable-rich-text=\"true\""
                    : "";

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
                    PsdUiToolkitFlowChildPlacement childPlacement = flowPlan.Placements.TryGetValue(child, out PsdUiToolkitFlowChildPlacement resolvedPlacement)
                        ? resolvedPlacement
                        : PsdUiToolkitFlowChildPlacement.Absolute;
                    AppendLayoutNode(builder, child, indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping, childPlacement);
                }
            }
            else
            {
                for (int i = 0; i < node.Children.Count; i++)
                    AppendLayoutNode(builder, node.Children[i], indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping, PsdUiToolkitFlowChildPlacement.Absolute);
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
            PsdUiToolkitFlowChildPlacement placement,
            PsdUiToolkitFlowContainerPlan flowPlan)
        {
            StringBuilder style = new StringBuilder(256);
            if (placement.UseFlow)
            {
                style.Append("position: relative; margin: 0;");
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
                style.Append(placement.UseFlow ? " padding: 0;" : " margin: 0; padding: 0;");
                style.AppendFormat(CultureInfo.InvariantCulture, " font-size: {0:0.##}px;", typeLayer.EffectiveFontSize);
                style.Append(" white-space: nowrap;");
                style.Append(" -unity-text-align: middle-center;");
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
                    float outlineWidth = Mathf.Clamp01(strokeSize / Mathf.Max(1f, typeLayer.EffectiveFontSize) * 2f);
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-text-outline-width: {0:0.###}px;", outlineWidth);
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-text-outline-color: rgba({0}, {1}, {2}, {3:0.###});", sr, sg, sb, strokeColor.a);
                }

                if (PsdUiToolkitTextEffectsHelper.TryGetDropShadowEffect(layer, out Color shadowColor, out Vector2 shadowOffset, out float blurRadius))
                {
                    int sr = Mathf.Clamp(Mathf.RoundToInt(shadowColor.r * 255f), 0, 255);
                    int sg = Mathf.Clamp(Mathf.RoundToInt(shadowColor.g * 255f), 0, 255);
                    int sb = Mathf.Clamp(Mathf.RoundToInt(shadowColor.b * 255f), 0, 255);
                    style.AppendFormat(CultureInfo.InvariantCulture,
                        " text-shadow: {0:0.###}px {1:0.###}px {2:0.###}px rgba({3}, {4}, {5}, {6:0.###});",
                        shadowOffset.x, shadowOffset.y, blurRadius, sr, sg, sb, shadowColor.a);
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

        private static string NormalizeExplicitLineBreaks(string text, out bool hasExplicitLineBreak)
        {
            string value = text ?? string.Empty;
            hasExplicitLineBreak = value.IndexOf('\r') >= 0
                || value.IndexOf('\n') >= 0
                || value.IndexOf('\u2028') >= 0
                || value.IndexOf('\u2029') >= 0;
            if (!hasExplicitLineBreak)
                return value;

            return value.Replace("\r\n", "<br>")
                .Replace("\r", "<br>")
                .Replace("\n", "<br>")
                .Replace("\u2028", "<br>")
                .Replace("\u2029", "<br>");
        }

        private static void AppendFlowContainerStyle(
            StringBuilder style,
            PsdUiToolkitFlowContainerPlan flowPlan)
        {
            if (style == null || flowPlan == null || !flowPlan.UseFlow)
                return;

            switch (flowPlan.LayoutType)
            {
                case PsdUiToolkitLayoutType.Row:
                    style.Append(" flex-direction: row;");
                    AppendMainAxisDistribution(style, flowPlan.MainAxisDistribution);
                    AppendCrossAxisAlignment(style, flowPlan.CrossAxisAlignment);
                    break;
                case PsdUiToolkitLayoutType.Column:
                    style.Append(" flex-direction: column;");
                    AppendMainAxisDistribution(style, flowPlan.MainAxisDistribution);
                    AppendCrossAxisAlignment(style, flowPlan.CrossAxisAlignment);
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

        private static void AppendMainAxisDistribution(
            StringBuilder style,
            PsdUiToolkitMainAxisDistribution distribution)
        {
            switch (distribution)
            {
                case PsdUiToolkitMainAxisDistribution.Center:
                    style.Append(" justify-content: center;");
                    break;
                case PsdUiToolkitMainAxisDistribution.End:
                    style.Append(" justify-content: flex-end;");
                    break;
                case PsdUiToolkitMainAxisDistribution.SpaceBetween:
                    style.Append(" justify-content: space-between;");
                    break;
                case PsdUiToolkitMainAxisDistribution.SpaceAround:
                    style.Append(" justify-content: space-around;");
                    break;
                case PsdUiToolkitMainAxisDistribution.Start:
                    style.Append(" justify-content: flex-start;");
                    break;
            }
        }

        private static void AppendCrossAxisAlignment(
            StringBuilder style,
            PsdUiToolkitCrossAxisAlignment alignment)
        {
            switch (alignment)
            {
                case PsdUiToolkitCrossAxisAlignment.Center:
                    style.Append(" align-items: center;");
                    break;
                case PsdUiToolkitCrossAxisAlignment.End:
                    style.Append(" align-items: flex-end;");
                    break;
                default:
                    style.Append(" align-items: flex-start;");
                    break;
            }
        }

        private static void AppendGridChildren(
            StringBuilder builder,
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitFlowContainerPlan flowPlan,
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

                AppendLayoutNode(builder, child, indentLevel, node.Bounds.Left, node.Bounds.Top, configMap, rasterResult, fontMapping, PsdUiToolkitFlowChildPlacement.Absolute);
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
            PsdUiToolkitFlowContainerPlan flowPlan,
            PsdUiToolkitGridRowPlan rowPlan,
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
                PsdUiToolkitFlowChildPlacement placement = rowPlan.Placements.TryGetValue(child, out PsdUiToolkitFlowChildPlacement resolvedPlacement)
                    ? resolvedPlacement
                    : PsdUiToolkitFlowChildPlacement.Absolute;
                AppendLayoutNode(builder, child, indentLevel + 1, parentNode.Bounds.Left, parentNode.Bounds.Top, configMap, rasterResult, fontMapping, placement);
            }

            builder.Append(indent);
            builder.AppendLine("</ui:VisualElement>");
        }

        private static string BuildSyntheticGridRowStyle(
            PsdUiToolkitFlowContainerPlan flowPlan,
            PsdUiToolkitGridRowPlan rowPlan)
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

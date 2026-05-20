using System;
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
        public static void Write(PsdImage psd, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult, string outputAssetPath, string rootName)
        {
            if (psd == null)
                throw new ArgumentNullException(nameof(psd));
            if (configMap == null)
                throw new ArgumentNullException(nameof(configMap));
            if (rasterResult == null)
                throw new ArgumentNullException(nameof(rasterResult));

            StringBuilder builder = new StringBuilder(8192);
            builder.AppendLine("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">");
            builder.Append("  <ui:VisualElement");
            builder.Append($" name=\"{EscapeAttribute(rootName)}\"");
            builder.Append($" style=\"position: relative; width: {psd.Width}px; height: {psd.Height}px; overflow: hidden;\"");
            builder.AppendLine(" >");

            foreach (Layer child in psd.Children)
            {
                AppendLayer(builder, child, 2, 0, 0, configMap, rasterResult);
            }

            builder.AppendLine("  </ui:VisualElement>");
            builder.AppendLine("</ui:UXML>");

            string diskPath = PsdUiToolkitAssetPathUtility.GetDiskPath(outputAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath) ?? string.Empty);
            File.WriteAllText(diskPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static void AppendLayer(StringBuilder builder, Layer layer, int indentLevel, int parentLeft, int parentTop, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterExportResult rasterResult)
        {
            if (layer == null || layer.LayerId == null)
                return;
            if (!configMap.IsExported(layer))
                return;
            if (rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
                return;

            PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(layer);
            int left = bounds.Left - parentLeft;
            int top = bounds.Top - parentTop;

            bool hasRaster = rasterResult.AssetsByLayerId.TryGetValue(layer.LayerId.Value, out PsdUiToolkitRasterAssetInfo rasterInfo);
            bool renderAsLeaf = !layer.IsGroup || hasRaster || rasterResult.CompositeLeafLayerIds.Contains(layer.LayerId.Value);

            string indent = new string(' ', indentLevel * 2);
            string elementName = string.IsNullOrEmpty(layer.Name) ? $"Layer_{layer.LayerId.Value}" : layer.Name;
            string style = BuildStyle(layer, bounds, left, top, configMap, rasterInfo);

            if (layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                builder.Append(indent);
                builder.Append($"<ui:Label name=\"{EscapeAttribute(elementName)}\" text=\"{EscapeAttribute(typeLayer.Text)}\" style=\"{EscapeAttribute(style)}\" />");
                builder.AppendLine();
                return;
            }

            if (!layer.IsGroup || renderAsLeaf)
            {
                builder.Append(indent);
                builder.Append($"<ui:VisualElement name=\"{EscapeAttribute(elementName)}\" style=\"{EscapeAttribute(style)}\" />");
                builder.AppendLine();
                return;
            }

            builder.Append(indent);
            builder.Append($"<ui:VisualElement name=\"{EscapeAttribute(elementName)}\" style=\"{EscapeAttribute(style)}\"");
            builder.AppendLine(" >");

            foreach (Layer child in layer.Children)
            {
                AppendLayer(builder, child, indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult);
            }

            builder.Append(indent);
            builder.AppendLine("</ui:VisualElement>");
        }

        private static string BuildStyle(Layer layer, PsdUiToolkitLayerBounds bounds, int left, int top, PsdUiToolkitLayerConfigMap configMap, PsdUiToolkitRasterAssetInfo rasterInfo)
        {
            StringBuilder style = new StringBuilder(256);
            style.Append("position: absolute;");
            style.AppendFormat(CultureInfo.InvariantCulture, " left: {0}px; top: {1}px; width: {2}px; height: {3}px;", left, top, bounds.Width, bounds.Height);
            style.AppendFormat(CultureInfo.InvariantCulture, " opacity: {0:0.###};", layer.OpacityFloat);
            style.Append(configMap.IsVisible(layer) ? " display: flex;" : " display: none;");

            if (layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                style.AppendFormat(CultureInfo.InvariantCulture, " font-size: {0:0.##}px;", typeLayer.EffectiveFontSize);
                style.Append(" white-space: normal; -unity-text-align: upper-left;");
                if (typeLayer.FillColor != null && typeLayer.FillColor.Length >= 4)
                {
                    int red = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[1] * 255f), 0, 255);
                    int green = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[2] * 255f), 0, 255);
                    int blue = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[3] * 255f), 0, 255);
                    float alpha = Mathf.Clamp01(typeLayer.FillColor[0]);
                    style.AppendFormat(CultureInfo.InvariantCulture, " color: rgba({0}, {1}, {2}, {3:0.###});", red, green, blue, alpha);
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

        private static string EscapeAttribute(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }
    }
}
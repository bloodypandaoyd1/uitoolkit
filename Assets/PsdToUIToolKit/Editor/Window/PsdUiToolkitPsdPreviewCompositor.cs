using System;
using System.Collections.Generic;
using PsdTools.Layers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitPsdPreviewCompositor
    {
        public static Texture2D Create(PsdImage psd)
        {
            if (psd == null)
                throw new ArgumentNullException(nameof(psd));
            if (psd.Width <= 0 || psd.Height <= 0)
                return null;

            Color32[] pixels = new Color32[psd.Width * psd.Height];
            BlitGroup(psd, psd.Root, pixels, psd.Width, psd.Height);

            Texture2D result = new Texture2D(
                psd.Width,
                psd.Height,
                TextureFormat.RGBA32,
                false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        internal static Texture2D CreateTextOutlineTexture(
            Texture2D source,
            Color strokeColor,
            float strokeSize,
            out int expansion)
        {
            expansion = 0;
            if (source == null || strokeSize <= 0f || strokeColor.a <= 0f)
                return source;

            expansion = Mathf.CeilToInt(strokeSize);
            int sourceWidth = source.width;
            int sourceHeight = source.height;
            int outputWidth = sourceWidth + expansion * 2;
            int outputHeight = sourceHeight + expansion * 2;
            Color32[] sourcePixels = source.GetPixels32();
            Color32[] outputPixels = new Color32[outputWidth * outputHeight];

            for (int outputY = 0; outputY < outputHeight; outputY++)
            {
                int sourceY = outputY - expansion;
                for (int outputX = 0; outputX < outputWidth; outputX++)
                {
                    int sourceX = outputX - expansion;
                    Color32 sourcePixel = GetPixelOrClear(
                        sourcePixels,
                        sourceWidth,
                        sourceHeight,
                        sourceX,
                        sourceY);
                    float sourceAlpha = sourcePixel.a / 255f;
                    float dilatedAlpha = SampleDilatedAlpha(
                        sourcePixels,
                        sourceWidth,
                        sourceHeight,
                        sourceX,
                        sourceY,
                        strokeSize);
                    float outlineAlpha =
                        Mathf.Max(0f, dilatedAlpha - sourceAlpha)
                        * strokeColor.a;

                    Color32 output = default;
                    BlendOver(ref output, strokeColor, outlineAlpha);
                    BlendOver(ref output, sourcePixel, sourceAlpha);
                    outputPixels[outputY * outputWidth + outputX] = output;
                }
            }

            Texture2D result = new Texture2D(
                outputWidth,
                outputHeight,
                TextureFormat.RGBA32,
                false);
            result.SetPixels32(outputPixels);
            result.Apply();
            return result;
        }

        internal static void BlitPixelRectOntoCanvas(
            Color32[] sourcePixels,
            int sourceLeft,
            int sourceTop,
            int sourceWidth,
            int sourceHeight,
            float opacity,
            Color32[] canvas,
            int canvasWidth,
            int canvasHeight)
        {
            for (int sourceY = 0; sourceY < sourceHeight; sourceY++)
            {
                for (int sourceX = 0; sourceX < sourceWidth; sourceX++)
                {
                    int canvasX = sourceLeft + sourceX;
                    int canvasY = sourceTop + sourceY;
                    if (canvasX < 0
                        || canvasX >= canvasWidth
                        || canvasY < 0
                        || canvasY >= canvasHeight)
                    {
                        continue;
                    }

                    int sourceIndex =
                        (sourceHeight - 1 - sourceY) * sourceWidth
                        + sourceX;
                    int canvasIndex =
                        (canvasHeight - 1 - canvasY) * canvasWidth
                        + canvasX;
                    Color32 output = canvas[canvasIndex];
                    Color32 source = sourcePixels[sourceIndex];
                    BlendOver(
                        ref output,
                        source,
                        source.a / 255f * opacity);
                    canvas[canvasIndex] = output;
                }
            }
        }

        private static void BlitGroup(
            PsdImage psd,
            Layer group,
            Color32[] canvas,
            int canvasWidth,
            int canvasHeight)
        {
            Layer currentBase = null;
            List<Layer> clippedLayers = new List<Layer>();
            IReadOnlyList<Layer> children = group.Children;
            for (int index = 0; index < children.Count; index++)
            {
                Layer child = children[index];
                if (!child.Visible)
                    continue;

                if (child.IsClipped)
                {
                    if (currentBase != null)
                        clippedLayers.Add(child);
                    continue;
                }

                if (currentBase != null)
                {
                    BlitBaseWithClippedLayers(
                        psd,
                        currentBase,
                        clippedLayers,
                        canvas,
                        canvasWidth,
                        canvasHeight);
                    clippedLayers.Clear();
                }

                currentBase = child;
            }

            if (currentBase != null)
            {
                BlitBaseWithClippedLayers(
                    psd,
                    currentBase,
                    clippedLayers,
                    canvas,
                    canvasWidth,
                    canvasHeight);
            }
        }

        private static void BlitBaseWithClippedLayers(
            PsdImage psd,
            Layer baseLayer,
            List<Layer> clippedLayers,
            Color32[] canvas,
            int canvasWidth,
            int canvasHeight)
        {
            if (clippedLayers.Count == 0)
            {
                BlitSingleLayer(
                    psd,
                    baseLayer,
                    canvas,
                    canvasWidth,
                    canvasHeight);
                return;
            }

            Texture2D merged = BuildMergedClippingTexture(
                psd,
                baseLayer,
                clippedLayers,
                out int left,
                out int top);
            if (merged == null)
                return;

            try
            {
                BlitPixelRectOntoCanvas(
                    merged.GetPixels32(),
                    left,
                    top,
                    merged.width,
                    merged.height,
                    baseLayer.OpacityFloat,
                    canvas,
                    canvasWidth,
                    canvasHeight);
            }
            finally
            {
                Object.DestroyImmediate(merged);
            }
        }

        private static void BlitSingleLayer(
            PsdImage psd,
            Layer layer,
            Color32[] canvas,
            int canvasWidth,
            int canvasHeight)
        {
            if (layer.IsGroup)
            {
                BlitGroup(psd, layer, canvas, canvasWidth, canvasHeight);
                return;
            }

            Texture2D texture = CreateLayerTexture(
                layer,
                true,
                out int expansion);
            if (texture == null)
                return;

            try
            {
                BlitPixelRectOntoCanvas(
                    texture.GetPixels32(),
                    layer.Left - expansion,
                    layer.Top - expansion,
                    texture.width,
                    texture.height,
                    layer.OpacityFloat,
                    canvas,
                    canvasWidth,
                    canvasHeight);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static Texture2D BuildMergedClippingTexture(
            PsdImage psd,
            Layer baseLayer,
            List<Layer> clippedLayers,
            out int baseLeft,
            out int baseTop)
        {
            Texture2D baseTexture;
            if (baseLayer.IsGroup)
            {
                Group group = (Group)baseLayer;
                var bounds = group.BBox;
                baseLeft = bounds.Left;
                baseTop = bounds.Top;
                baseTexture = RasterizeGroup(psd, group);
            }
            else
            {
                baseLeft = baseLayer.Left;
                baseTop = baseLayer.Top;
                baseTexture = CreateLayerTexture(
                    baseLayer,
                    false,
                    out _);
            }

            if (baseTexture == null)
                return null;

            Color32[] basePixels = baseTexture.GetPixels32();
            byte[] originalBaseAlpha = new byte[basePixels.Length];
            for (int index = 0; index < basePixels.Length; index++)
                originalBaseAlpha[index] = basePixels[index].a;

            foreach (Layer clippedLayer in clippedLayers)
            {
                if (!clippedLayer.Visible)
                    continue;

                Texture2D clippedTexture;
                int clippedLeft;
                int clippedTop;
                if (clippedLayer.IsGroup)
                {
                    Group clippedGroup = (Group)clippedLayer;
                    var bounds = clippedGroup.BBox;
                    clippedLeft = bounds.Left;
                    clippedTop = bounds.Top;
                    clippedTexture = RasterizeGroup(psd, clippedGroup);
                }
                else
                {
                    clippedTexture = CreateLayerTexture(
                        clippedLayer,
                        true,
                        out int expansion);
                    clippedLeft = clippedLayer.Left - expansion;
                    clippedTop = clippedLayer.Top - expansion;
                }

                if (clippedTexture == null)
                    continue;

                try
                {
                    BlendClippedTexture(
                        clippedTexture,
                        clippedLeft,
                        clippedTop,
                        clippedLayer.OpacityFloat,
                        basePixels,
                        originalBaseAlpha,
                        baseLeft,
                        baseTop,
                        baseTexture.width,
                        baseTexture.height);
                }
                finally
                {
                    Object.DestroyImmediate(clippedTexture);
                }
            }

            baseTexture.SetPixels32(basePixels);
            baseTexture.Apply();
            if (baseLayer.Kind != LayerKind.Type
                || !PsdUiToolkitTextEffectsHelper.TryGetStrokeEffect(
                    baseLayer,
                    out Color strokeColor,
                    out float strokeSize)
                || strokeSize <= 0f)
            {
                return baseTexture;
            }

            Texture2D outlined = CreateTextOutlineTexture(
                baseTexture,
                strokeColor,
                strokeSize,
                out int outlineExpansion);
            if (outlined == baseTexture)
                return baseTexture;

            Object.DestroyImmediate(baseTexture);
            baseLeft -= outlineExpansion;
            baseTop -= outlineExpansion;
            return outlined;
        }

        private static Texture2D RasterizeGroup(PsdImage psd, Group group)
        {
            var bounds = group.BBox;
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            if (width <= 0 || height <= 0)
                return null;

            Color32[] documentPixels =
                new Color32[psd.Width * psd.Height];
            BlitGroup(
                psd,
                group,
                documentPixels,
                psd.Width,
                psd.Height);

            Color32[] croppedPixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int documentX = bounds.Left + x;
                    int documentY = bounds.Top + y;
                    if (documentX < 0
                        || documentX >= psd.Width
                        || documentY < 0
                        || documentY >= psd.Height)
                    {
                        continue;
                    }

                    int sourceIndex =
                        (psd.Height - 1 - documentY) * psd.Width
                        + documentX;
                    int targetIndex =
                        (height - 1 - y) * width + x;
                    croppedPixels[targetIndex] =
                        documentPixels[sourceIndex];
                }
            }

            Texture2D result = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false);
            result.SetPixels32(croppedPixels);
            result.Apply();
            return result;
        }

        private static Texture2D CreateLayerTexture(
            Layer layer,
            bool includeTextOutline,
            out int expansion)
        {
            expansion = 0;
            if (layer == null
                || (!layer.HasPixels()
                    && !LayerEffectsHelper.HasExtractableColor(layer)))
            {
                return null;
            }

            Texture2D texture =
                LayerEffectsHelper.CreateLayerTextureWithEffects(layer);
            if (texture == null
                || !includeTextOutline
                || layer.Kind != LayerKind.Type
                || !PsdUiToolkitTextEffectsHelper.TryGetStrokeEffect(
                    layer,
                    out Color strokeColor,
                    out float strokeSize)
                || strokeSize <= 0f)
            {
                return texture;
            }

            Texture2D outlined = CreateTextOutlineTexture(
                texture,
                strokeColor,
                strokeSize,
                out expansion);
            if (outlined != texture)
                Object.DestroyImmediate(texture);
            return outlined;
        }

        private static void BlendClippedTexture(
            Texture2D clippedTexture,
            int clippedLeft,
            int clippedTop,
            float clippedOpacity,
            Color32[] basePixels,
            byte[] originalBaseAlpha,
            int baseLeft,
            int baseTop,
            int baseWidth,
            int baseHeight)
        {
            Color32[] clippedPixels = clippedTexture.GetPixels32();
            for (int y = 0; y < clippedTexture.height; y++)
            {
                for (int x = 0; x < clippedTexture.width; x++)
                {
                    int baseX = clippedLeft + x - baseLeft;
                    int baseY = clippedTop + y - baseTop;
                    if (baseX < 0
                        || baseX >= baseWidth
                        || baseY < 0
                        || baseY >= baseHeight)
                    {
                        continue;
                    }

                    int baseIndex =
                        (baseHeight - 1 - baseY) * baseWidth + baseX;
                    int clippedIndex =
                        (clippedTexture.height - 1 - y)
                        * clippedTexture.width
                        + x;
                    float maskAlpha =
                        originalBaseAlpha[baseIndex] / 255f;
                    if (maskAlpha <= 0f)
                        continue;

                    Color32 output = basePixels[baseIndex];
                    Color32 source = clippedPixels[clippedIndex];
                    BlendOver(
                        ref output,
                        source,
                        source.a / 255f
                        * maskAlpha
                        * clippedOpacity);
                    basePixels[baseIndex] = output;
                }
            }
        }

        private static Color32 GetPixelOrClear(
            Color32[] pixels,
            int width,
            int height,
            int x,
            int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return default;
            return pixels[y * width + x];
        }

        private static float SampleDilatedAlpha(
            Color32[] pixels,
            int width,
            int height,
            int centerX,
            int centerY,
            float radius)
        {
            int reach = Mathf.CeilToInt(radius);
            float radiusSquared = radius * radius + 0.25f;
            float maximum = 0f;
            for (int offsetY = -reach; offsetY <= reach; offsetY++)
            {
                for (int offsetX = -reach; offsetX <= reach; offsetX++)
                {
                    if (offsetX * offsetX + offsetY * offsetY
                        > radiusSquared)
                    {
                        continue;
                    }

                    Color32 sample = GetPixelOrClear(
                        pixels,
                        width,
                        height,
                        centerX + offsetX,
                        centerY + offsetY);
                    maximum = Mathf.Max(
                        maximum,
                        sample.a / 255f);
                }
            }

            return maximum;
        }

        private static void BlendOver(
            ref Color32 destination,
            Color source,
            float sourceAlpha)
        {
            BlendOver(
                ref destination,
                new Color32(
                    (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(source.r) * 255f),
                    (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(source.g) * 255f),
                    (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(source.b) * 255f),
                    255),
                sourceAlpha);
        }

        private static void BlendOver(
            ref Color32 destination,
            Color32 source,
            float sourceAlpha)
        {
            sourceAlpha = Mathf.Clamp01(sourceAlpha);
            if (sourceAlpha <= 0f)
                return;

            float destinationAlpha = destination.a / 255f;
            float outputAlpha =
                sourceAlpha + destinationAlpha * (1f - sourceAlpha);
            if (outputAlpha <= 0f)
                return;

            destination = new Color32(
                (byte)(
                    (source.r * sourceAlpha
                        + destination.r
                        * destinationAlpha
                        * (1f - sourceAlpha))
                    / outputAlpha),
                (byte)(
                    (source.g * sourceAlpha
                        + destination.g
                        * destinationAlpha
                        * (1f - sourceAlpha))
                    / outputAlpha),
                (byte)(
                    (source.b * sourceAlpha
                        + destination.b
                        * destinationAlpha
                        * (1f - sourceAlpha))
                    / outputAlpha),
                (byte)(outputAlpha * 255f));
        }
    }
}

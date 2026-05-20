using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    [Serializable]
    public struct PsdUiToolkitNineSliceParams
    {
        public int borderInset;
        public int pixelThreshold;
        public int minCenterCols;
        public int minCenterRows;
        public int minSameZone;

        public static PsdUiToolkitNineSliceParams Default => new PsdUiToolkitNineSliceParams
        {
            borderInset = 2,
            pixelThreshold = 10,
            minCenterCols = 10,
            minCenterRows = 10,
            minSameZone = 15,
        };
    }

    [Serializable]
    public sealed class PsdUiToolkitLayerConfig
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
        public bool useCustomImage;
        public string customImagePath = "";

        public static PsdUiToolkitLayerConfig CreateDefault(Layer layer)
        {
            PsdUiToolkitNineSliceParams defaults = PsdUiToolkitNineSliceParams.Default;
            return new PsdUiToolkitLayerConfig
            {
                id = layer?.LayerId ?? -1,
                name = layer?.Name ?? string.Empty,
                exported = true,
                visible = layer?.Visible ?? true,
                merge = false,
                sliceImage = true,
                participateLocalDedup = true,
                participateCommonDedup = true,
                useCustomNineSliceParams = false,
                nineSliceBorderInset = defaults.borderInset,
                nineSlicePixelThreshold = defaults.pixelThreshold,
                nineSliceMinCenterCols = defaults.minCenterCols,
                nineSliceMinCenterRows = defaults.minCenterRows,
                nineSliceMinSameZone = defaults.minSameZone,
                useCustomImage = false,
                customImagePath = string.Empty,
            };
        }

        public PsdUiToolkitNineSliceParams GetNineSliceParams()
        {
            return new PsdUiToolkitNineSliceParams
            {
                borderInset = nineSliceBorderInset,
                pixelThreshold = nineSlicePixelThreshold,
                minCenterCols = nineSliceMinCenterCols,
                minCenterRows = nineSliceMinCenterRows,
                minSameZone = nineSliceMinSameZone,
            };
        }
    }

    [Serializable]
    public sealed class PsdUiToolkitExportConfigData
    {
        public PsdUiToolkitLayerConfig[] layers = Array.Empty<PsdUiToolkitLayerConfig>();
    }

    internal sealed class PsdUiToolkitLayerConfigMap
    {
        private readonly Dictionary<int, PsdUiToolkitLayerConfig> _lookup;

        public PsdUiToolkitLayerConfigMap(PsdUiToolkitExportConfigData data)
        {
            _lookup = PsdUiToolkitConfigStore.BuildLookup(data);
        }

        public PsdUiToolkitLayerConfig Get(Layer layer)
        {
            if (layer?.LayerId == null)
                return PsdUiToolkitLayerConfig.CreateDefault(layer);

            return _lookup.TryGetValue(layer.LayerId.Value, out PsdUiToolkitLayerConfig config)
                ? config
                : PsdUiToolkitLayerConfig.CreateDefault(layer);
        }

        public bool IsExported(Layer layer)
        {
            return Get(layer).exported;
        }

        public bool IsVisible(Layer layer)
        {
            return Get(layer).visible;
        }

        public bool IsMergeExport(Layer layer)
        {
            return Get(layer).merge;
        }

        public bool GetSliceImage(Layer layer)
        {
            return Get(layer).sliceImage;
        }

        public bool ParticipateLocalDedup(Layer layer)
        {
            return Get(layer).participateLocalDedup;
        }

        public bool ParticipateCommonDedup(Layer layer)
        {
            return Get(layer).participateCommonDedup;
        }

        public bool LayerUsesCustomNineSliceParams(Layer layer)
        {
            return GetSliceImage(layer) && Get(layer).useCustomNineSliceParams;
        }

        public PsdUiToolkitNineSliceParams GetResolvedNineSliceParams(Layer layer, PsdUiToolkitNineSliceParams defaults)
        {
            PsdUiToolkitLayerConfig config = Get(layer);
            if (config.sliceImage && config.useCustomNineSliceParams)
                return config.GetNineSliceParams();

            return defaults;
        }

        public bool UseCustomImage(Layer layer)
        {
            PsdUiToolkitLayerConfig config = Get(layer);
            return config.useCustomImage && !string.IsNullOrEmpty(config.customImagePath);
        }

        public string GetCustomImagePath(Layer layer)
        {
            return Get(layer).customImagePath ?? string.Empty;
        }
    }

    internal static class PsdUiToolkitEditorPrefs
    {
        private const string ImageExportRootKey = "PsdUiToolkit_ImageExportRoot";
        private const string UxmlExportRootKey = "PsdUiToolkit_UxmlExportRoot";
        private const string AutoImageNamingKey = "PsdUiToolkit_AutoImageNaming";

        public const string DefaultImageExportRoot = "Assets/PsdToUIToolKit/Generated/Images";
        public const string DefaultUxmlExportRoot = "Assets/PsdToUIToolKit/Generated/Uxml";

        public static string ImageExportRoot
        {
            get => UnityEditor.EditorPrefs.GetString(ImageExportRootKey, DefaultImageExportRoot);
            set => UnityEditor.EditorPrefs.SetString(ImageExportRootKey, value);
        }

        public static string UxmlExportRoot
        {
            get => UnityEditor.EditorPrefs.GetString(UxmlExportRootKey, DefaultUxmlExportRoot);
            set => UnityEditor.EditorPrefs.SetString(UxmlExportRootKey, value);
        }

        public static bool AutoImageNaming
        {
            get => UnityEditor.EditorPrefs.GetBool(AutoImageNamingKey, true);
            set => UnityEditor.EditorPrefs.SetBool(AutoImageNamingKey, value);
        }
    }
}
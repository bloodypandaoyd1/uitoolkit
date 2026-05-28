using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    public enum PsdUiToolkitAutoLayoutMode
    {
        Disabled = 0,
        Conservative = 1,
        Balanced = 2,
        Aggressive = 3,
    }

    public enum PsdUiToolkitLayoutFallbackMode
    {
        Absolute = 0,
    }

    public enum PsdUiToolkitLayoutType
    {
        Auto = 0,
        Absolute = 1,
        Row = 2,
        Column = 3,
        Grid = 4,
        Overlay = 5,
    }

    public enum PsdUiToolkitSemanticRole
    {
        Auto = 0,
        Container = 1,
        Background = 2,
        Content = 3,
        Decoration = 4,
        Overlay = 5,
        Ignore = 6,
    }

    public enum PsdUiToolkitSizePolicy
    {
        Auto = 0,
        Fixed = 1,
        Intrinsic = 2,
        Fill = 3,
    }

    public enum PsdUiToolkitMainAxisAlignment
    {
        Auto = 0,
        Start = 1,
        Center = 2,
        End = 3,
        SpaceBetween = 4,
    }

    public enum PsdUiToolkitCrossAxisAlignment
    {
        Auto = 0,
        Start = 1,
        Center = 2,
        End = 3,
        Stretch = 4,
    }

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
    public struct PsdUiToolkitAutoLayoutGlobalConfig
    {
        public bool enabled;
        public bool rebuildLayoutTree;
        public PsdUiToolkitAutoLayoutMode detectionMode;
        public float minimumConfidence;
        public int alignmentTolerance;
        public int gapTolerance;
        public bool allowVirtualContainers;
        public bool allowCrossGroupRegrouping;
        public bool detectBackgroundContainers;
        public int maxNestingDepth;
        public PsdUiToolkitLayoutFallbackMode fallbackMode;

        public static PsdUiToolkitAutoLayoutGlobalConfig Default => new PsdUiToolkitAutoLayoutGlobalConfig
        {
            enabled = false,
            rebuildLayoutTree = false,
            detectionMode = PsdUiToolkitAutoLayoutMode.Conservative,
            minimumConfidence = 0.8f,
            alignmentTolerance = 8,
            gapTolerance = 10,
            allowVirtualContainers = true,
            allowCrossGroupRegrouping = false,
            detectBackgroundContainers = true,
            maxNestingDepth = 3,
            fallbackMode = PsdUiToolkitLayoutFallbackMode.Absolute,
        };

        public bool ShouldAnalyze => enabled && detectionMode != PsdUiToolkitAutoLayoutMode.Disabled;

        public PsdUiToolkitAutoLayoutGlobalConfig GetValidated()
        {
            PsdUiToolkitAutoLayoutGlobalConfig validated = this;
            validated.minimumConfidence = Math.Max(0f, Math.Min(1f, validated.minimumConfidence));
            validated.alignmentTolerance = Math.Max(0, validated.alignmentTolerance);
            validated.gapTolerance = Math.Max(0, validated.gapTolerance);
            validated.maxNestingDepth = Math.Max(1, validated.maxNestingDepth);
            if (!Enum.IsDefined(typeof(PsdUiToolkitAutoLayoutMode), validated.detectionMode))
                validated.detectionMode = Default.detectionMode;
            if (!Enum.IsDefined(typeof(PsdUiToolkitLayoutFallbackMode), validated.fallbackMode))
                validated.fallbackMode = Default.fallbackMode;
            return validated;
        }
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
        public bool participateInAutoLayout = true;
        public PsdUiToolkitSemanticRole semanticRole = PsdUiToolkitSemanticRole.Auto;
        public int parentHintLayerId = -1;
        public string virtualContainerKey = "";
        public PsdUiToolkitLayoutType forcedLayoutType = PsdUiToolkitLayoutType.Auto;
        public bool forceContainer;
        public bool forceBackground;
        public bool keepAbsoluteInsideParent;
        public bool includeInFlow = true;
        public int orderOverride = -1;
        public PsdUiToolkitSizePolicy sizePolicy = PsdUiToolkitSizePolicy.Auto;
        public float growWeight;
        public bool useSpacingOverride;
        public int spacingOverride;
        public bool usePaddingOverride;
        public int paddingLeft;
        public int paddingTop;
        public int paddingRight;
        public int paddingBottom;
        public PsdUiToolkitMainAxisAlignment mainAxisAlignment = PsdUiToolkitMainAxisAlignment.Auto;
        public PsdUiToolkitCrossAxisAlignment crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.Auto;
        public bool wrap;
        public int gridColumnCount;
        public int gridCellWidth;
        public int gridCellHeight;

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
                participateInAutoLayout = true,
                semanticRole = PsdUiToolkitSemanticRole.Auto,
                parentHintLayerId = -1,
                virtualContainerKey = string.Empty,
                forcedLayoutType = PsdUiToolkitLayoutType.Auto,
                forceContainer = false,
                forceBackground = false,
                keepAbsoluteInsideParent = false,
                includeInFlow = true,
                orderOverride = -1,
                sizePolicy = PsdUiToolkitSizePolicy.Auto,
                growWeight = 0f,
                useSpacingOverride = false,
                spacingOverride = 0,
                usePaddingOverride = false,
                paddingLeft = 0,
                paddingTop = 0,
                paddingRight = 0,
                paddingBottom = 0,
                mainAxisAlignment = PsdUiToolkitMainAxisAlignment.Auto,
                crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.Auto,
                wrap = false,
                gridColumnCount = 0,
                gridCellWidth = 0,
                gridCellHeight = 0,
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

        public void Sanitize()
        {
            name ??= string.Empty;
            customImagePath ??= string.Empty;
            virtualContainerKey ??= string.Empty;
            parentHintLayerId = Math.Max(-1, parentHintLayerId);
            orderOverride = Math.Max(-1, orderOverride);
            growWeight = Math.Max(0f, growWeight);
            spacingOverride = Math.Max(0, spacingOverride);
            paddingLeft = Math.Max(0, paddingLeft);
            paddingTop = Math.Max(0, paddingTop);
            paddingRight = Math.Max(0, paddingRight);
            paddingBottom = Math.Max(0, paddingBottom);
            gridColumnCount = Math.Max(0, gridColumnCount);
            gridCellWidth = Math.Max(0, gridCellWidth);
            gridCellHeight = Math.Max(0, gridCellHeight);
            if (!Enum.IsDefined(typeof(PsdUiToolkitSemanticRole), semanticRole))
                semanticRole = PsdUiToolkitSemanticRole.Auto;
            if (!Enum.IsDefined(typeof(PsdUiToolkitLayoutType), forcedLayoutType))
                forcedLayoutType = PsdUiToolkitLayoutType.Auto;
            if (!Enum.IsDefined(typeof(PsdUiToolkitSizePolicy), sizePolicy))
                sizePolicy = PsdUiToolkitSizePolicy.Auto;
            if (!Enum.IsDefined(typeof(PsdUiToolkitMainAxisAlignment), mainAxisAlignment))
                mainAxisAlignment = PsdUiToolkitMainAxisAlignment.Auto;
            if (!Enum.IsDefined(typeof(PsdUiToolkitCrossAxisAlignment), crossAxisAlignment))
                crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.Auto;
        }

        public bool HasParentHint => parentHintLayerId >= 0;
    }

    [Serializable]
    public sealed class PsdUiToolkitExportConfigData
    {
        public PsdUiToolkitAutoLayoutGlobalConfig autoLayout = PsdUiToolkitAutoLayoutGlobalConfig.Default;
        public PsdUiToolkitLayerConfig[] layers = Array.Empty<PsdUiToolkitLayerConfig>();
    }

    internal sealed class PsdUiToolkitLayerConfigMap
    {
        private readonly Dictionary<int, PsdUiToolkitLayerConfig> _lookup;
        private readonly PsdUiToolkitAutoLayoutGlobalConfig _autoLayout;

        public PsdUiToolkitLayerConfigMap(PsdUiToolkitExportConfigData data)
        {
            _lookup = PsdUiToolkitConfigStore.BuildLookup(data);
            _autoLayout = data == null
                ? PsdUiToolkitAutoLayoutGlobalConfig.Default
                : data.autoLayout.GetValidated();
        }

        public PsdUiToolkitLayerConfig Get(Layer layer)
        {
            if (layer?.LayerId == null)
            {
                PsdUiToolkitLayerConfig defaultConfig = PsdUiToolkitLayerConfig.CreateDefault(layer);
                defaultConfig.Sanitize();
                return defaultConfig;
            }

            if (_lookup.TryGetValue(layer.LayerId.Value, out PsdUiToolkitLayerConfig config))
            {
                config.Sanitize();
                return config;
            }

            PsdUiToolkitLayerConfig fallbackConfig = PsdUiToolkitLayerConfig.CreateDefault(layer);
            fallbackConfig.Sanitize();
            return fallbackConfig;
        }

        public PsdUiToolkitAutoLayoutGlobalConfig GetAutoLayoutConfig()
        {
            return _autoLayout;
        }

        public bool IsAutoLayoutEnabled()
        {
            return _autoLayout.ShouldAnalyze;
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

        public bool ParticipateInAutoLayout(Layer layer)
        {
            PsdUiToolkitLayerConfig config = Get(layer);
            return config.exported
                && config.visible
                && config.participateInAutoLayout
                && config.semanticRole != PsdUiToolkitSemanticRole.Ignore;
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
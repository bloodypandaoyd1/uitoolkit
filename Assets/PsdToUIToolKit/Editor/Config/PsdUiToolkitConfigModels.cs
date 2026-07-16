using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    public enum PsdUiToolkitAutoLayoutMode
    {
        Conservative = 1,
        Balanced = 2,
        Aggressive = 3,
        Custom = 4,
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

    public enum PsdUiToolkitContainerLayout
    {
        Unspecified = 0,
        Absolute = 1,
        Row = 2,
        Column = 3,
    }

    public enum PsdUiToolkitItemRole
    {
        FollowParent = 0,
        KeepAbsolute = 1,
        Background = 2,
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
        public bool detectBackgroundContainers;
        public int maxNestingDepth;
        public int customScoringVersion;
        public float ambiguityGap;
        public float backgroundFillThreshold;
        public int minimumFlowCandidates;
        public int minimumGridCandidates;
        public int minimumVirtualContainerCandidates;
        public float flowAlignmentWeight;
        public float flowGapWeight;
        public float flowOverlapWeight;
        public float flowSpanWeight;
        public float gridOccupancyWeight;
        public float gridSizeWeight;
        public float gridAlignmentWeight;
        public float gridGapWeight;
        public float gridOverlapWeight;
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
            detectBackgroundContainers = true,
            maxNestingDepth = 3,
            customScoringVersion = 1,
            ambiguityGap = 0.08f,
            backgroundFillThreshold = 0.72f,
            minimumFlowCandidates = 2,
            minimumGridCandidates = 4,
            minimumVirtualContainerCandidates = 2,
            flowAlignmentWeight = 0.42f,
            flowGapWeight = 0.25f,
            flowOverlapWeight = 0.2f,
            flowSpanWeight = 0.13f,
            gridOccupancyWeight = 0.28f,
            gridSizeWeight = 0.24f,
            gridAlignmentWeight = 0.2f,
            gridGapWeight = 0.16f,
            gridOverlapWeight = 0.12f,
            fallbackMode = PsdUiToolkitLayoutFallbackMode.Absolute,
        };

        public bool ShouldAnalyze => enabled;

        public PsdUiToolkitAutoLayoutGlobalConfig GetValidated()
        {
            PsdUiToolkitAutoLayoutGlobalConfig validated = this;
            validated.minimumConfidence = Math.Max(0f, Math.Min(1f, validated.minimumConfidence));
            validated.alignmentTolerance = Math.Max(0, validated.alignmentTolerance);
            validated.gapTolerance = Math.Max(0, validated.gapTolerance);
            validated.maxNestingDepth = Math.Max(1, validated.maxNestingDepth);
            if (validated.customScoringVersion < 1)
            {
                PsdUiToolkitAutoLayoutGlobalConfig defaults = Default;
                validated.customScoringVersion = defaults.customScoringVersion;
                validated.ambiguityGap = defaults.ambiguityGap;
                validated.backgroundFillThreshold = defaults.backgroundFillThreshold;
                validated.minimumFlowCandidates = defaults.minimumFlowCandidates;
                validated.minimumGridCandidates = defaults.minimumGridCandidates;
                validated.minimumVirtualContainerCandidates = defaults.minimumVirtualContainerCandidates;
                validated.flowAlignmentWeight = defaults.flowAlignmentWeight;
                validated.flowGapWeight = defaults.flowGapWeight;
                validated.flowOverlapWeight = defaults.flowOverlapWeight;
                validated.flowSpanWeight = defaults.flowSpanWeight;
                validated.gridOccupancyWeight = defaults.gridOccupancyWeight;
                validated.gridSizeWeight = defaults.gridSizeWeight;
                validated.gridAlignmentWeight = defaults.gridAlignmentWeight;
                validated.gridGapWeight = defaults.gridGapWeight;
                validated.gridOverlapWeight = defaults.gridOverlapWeight;
            }
            validated.ambiguityGap = Clamp01(validated.ambiguityGap);
            validated.backgroundFillThreshold = Clamp01(validated.backgroundFillThreshold);
            validated.minimumFlowCandidates = Math.Max(2, validated.minimumFlowCandidates);
            validated.minimumGridCandidates = Math.Max(4, validated.minimumGridCandidates);
            validated.minimumVirtualContainerCandidates = Math.Max(2, validated.minimumVirtualContainerCandidates);
            validated.flowAlignmentWeight = Math.Max(0f, validated.flowAlignmentWeight);
            validated.flowGapWeight = Math.Max(0f, validated.flowGapWeight);
            validated.flowOverlapWeight = Math.Max(0f, validated.flowOverlapWeight);
            validated.flowSpanWeight = Math.Max(0f, validated.flowSpanWeight);
            validated.gridOccupancyWeight = Math.Max(0f, validated.gridOccupancyWeight);
            validated.gridSizeWeight = Math.Max(0f, validated.gridSizeWeight);
            validated.gridAlignmentWeight = Math.Max(0f, validated.gridAlignmentWeight);
            validated.gridGapWeight = Math.Max(0f, validated.gridGapWeight);
            validated.gridOverlapWeight = Math.Max(0f, validated.gridOverlapWeight);
            if (!Enum.IsDefined(typeof(PsdUiToolkitAutoLayoutMode), validated.detectionMode))
                validated.detectionMode = Default.detectionMode;
            if (!Enum.IsDefined(typeof(PsdUiToolkitLayoutFallbackMode), validated.fallbackMode))
                validated.fallbackMode = Default.fallbackMode;
            return validated;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    internal readonly struct PsdUiToolkitAutoLayoutDetectionProfile
    {
        public PsdUiToolkitAutoLayoutMode Mode { get; }
        public float MinimumConfidence { get; }
        public int AlignmentTolerance { get; }
        public int GapTolerance { get; }
        public int MaxNestingDepth { get; }
        public float AmbiguityGap { get; }
        public float BackgroundFillThreshold { get; }
        public int MinimumFlowCandidates { get; }
        public int MinimumGridCandidates { get; }
        public int MinimumVirtualContainerCandidates { get; }
        public float FlowAlignmentWeight { get; }
        public float FlowGapWeight { get; }
        public float FlowOverlapWeight { get; }
        public float FlowSpanWeight { get; }
        public float GridOccupancyWeight { get; }
        public float GridSizeWeight { get; }
        public float GridAlignmentWeight { get; }
        public float GridGapWeight { get; }
        public float GridOverlapWeight { get; }
        public bool UsedFlowWeightFallback { get; }
        public bool UsedGridWeightFallback { get; }

        private PsdUiToolkitAutoLayoutDetectionProfile(
            PsdUiToolkitAutoLayoutMode mode,
            float minimumConfidence,
            int alignmentTolerance,
            int gapTolerance,
            int maxNestingDepth,
            float ambiguityGap,
            float backgroundFillThreshold,
            int minimumFlowCandidates,
            int minimumGridCandidates,
            int minimumVirtualContainerCandidates,
            float flowAlignmentWeight,
            float flowGapWeight,
            float flowOverlapWeight,
            float flowSpanWeight,
            float gridOccupancyWeight,
            float gridSizeWeight,
            float gridAlignmentWeight,
            float gridGapWeight,
            float gridOverlapWeight,
            bool usedFlowWeightFallback = false,
            bool usedGridWeightFallback = false)
        {
            Mode = mode;
            MinimumConfidence = minimumConfidence;
            AlignmentTolerance = alignmentTolerance;
            GapTolerance = gapTolerance;
            MaxNestingDepth = maxNestingDepth;
            AmbiguityGap = ambiguityGap;
            BackgroundFillThreshold = backgroundFillThreshold;
            MinimumFlowCandidates = minimumFlowCandidates;
            MinimumGridCandidates = minimumGridCandidates;
            MinimumVirtualContainerCandidates = minimumVirtualContainerCandidates;
            FlowAlignmentWeight = flowAlignmentWeight;
            FlowGapWeight = flowGapWeight;
            FlowOverlapWeight = flowOverlapWeight;
            FlowSpanWeight = flowSpanWeight;
            GridOccupancyWeight = gridOccupancyWeight;
            GridSizeWeight = gridSizeWeight;
            GridAlignmentWeight = gridAlignmentWeight;
            GridGapWeight = gridGapWeight;
            GridOverlapWeight = gridOverlapWeight;
            UsedFlowWeightFallback = usedFlowWeightFallback;
            UsedGridWeightFallback = usedGridWeightFallback;
        }

        public static PsdUiToolkitAutoLayoutDetectionProfile Resolve(PsdUiToolkitAutoLayoutGlobalConfig config)
        {
            PsdUiToolkitAutoLayoutGlobalConfig validated = config.GetValidated();
            switch (validated.detectionMode)
            {
                case PsdUiToolkitAutoLayoutMode.Balanced:
                    return CreateBalanced();
                case PsdUiToolkitAutoLayoutMode.Aggressive:
                    return new PsdUiToolkitAutoLayoutDetectionProfile(
                        validated.detectionMode, 0.55f, 16, 20, 5, 0.03f, 0.60f, 2, 4, 2,
                        0.32f, 0.23f, 0.15f, 0.30f,
                        0.34f, 0.12f, 0.18f, 0.24f, 0.12f);
                case PsdUiToolkitAutoLayoutMode.Custom:
                    return CreateCustom(validated);
                default:
                    return new PsdUiToolkitAutoLayoutDetectionProfile(
                        PsdUiToolkitAutoLayoutMode.Conservative, 0.85f, 4, 6, 2, 0.12f, 0.85f, 3, 6, 3,
                        0.50f, 0.25f, 0.20f, 0.05f,
                        0.28f, 0.24f, 0.28f, 0.14f, 0.06f);
            }
        }

        public string GetSummary()
        {
            return $"{Mode}: confidence >= {MinimumConfidence:0.##}, alignment tolerance {AlignmentTolerance}px, gap tolerance {GapTolerance}px, max nesting {MaxNestingDepth}, ambiguity gap {AmbiguityGap:0.##}.";
        }

        private static PsdUiToolkitAutoLayoutDetectionProfile CreateBalanced()
        {
            return new PsdUiToolkitAutoLayoutDetectionProfile(
                PsdUiToolkitAutoLayoutMode.Balanced, 0.70f, 8, 10, 3, 0.08f, 0.72f, 2, 4, 2,
                0.42f, 0.25f, 0.20f, 0.13f,
                0.28f, 0.24f, 0.20f, 0.16f, 0.12f);
        }

        private static PsdUiToolkitAutoLayoutDetectionProfile CreateCustom(PsdUiToolkitAutoLayoutGlobalConfig config)
        {
            PsdUiToolkitAutoLayoutDetectionProfile balanced = CreateBalanced();
            float flowTotal = config.flowAlignmentWeight + config.flowGapWeight + config.flowOverlapWeight + config.flowSpanWeight;
            float gridTotal = config.gridOccupancyWeight + config.gridSizeWeight + config.gridAlignmentWeight + config.gridGapWeight + config.gridOverlapWeight;
            bool flowFallback = flowTotal <= 0f;
            bool gridFallback = gridTotal <= 0f;
            if (flowFallback)
                flowTotal = 1f;
            if (gridFallback)
                gridTotal = 1f;

            return new PsdUiToolkitAutoLayoutDetectionProfile(
                PsdUiToolkitAutoLayoutMode.Custom,
                config.minimumConfidence,
                config.alignmentTolerance,
                config.gapTolerance,
                config.maxNestingDepth,
                config.ambiguityGap,
                config.backgroundFillThreshold,
                config.minimumFlowCandidates,
                config.minimumGridCandidates,
                config.minimumVirtualContainerCandidates,
                flowFallback ? balanced.FlowAlignmentWeight : config.flowAlignmentWeight / flowTotal,
                flowFallback ? balanced.FlowGapWeight : config.flowGapWeight / flowTotal,
                flowFallback ? balanced.FlowOverlapWeight : config.flowOverlapWeight / flowTotal,
                flowFallback ? balanced.FlowSpanWeight : config.flowSpanWeight / flowTotal,
                gridFallback ? balanced.GridOccupancyWeight : config.gridOccupancyWeight / gridTotal,
                gridFallback ? balanced.GridSizeWeight : config.gridSizeWeight / gridTotal,
                gridFallback ? balanced.GridAlignmentWeight : config.gridAlignmentWeight / gridTotal,
                gridFallback ? balanced.GridGapWeight : config.gridGapWeight / gridTotal,
                gridFallback ? balanced.GridOverlapWeight : config.gridOverlapWeight / gridTotal,
                flowFallback,
                gridFallback);
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
        public bool participateInAutoLayout = true;
        public PsdUiToolkitContainerLayout childrenLayout = PsdUiToolkitContainerLayout.Unspecified;
        public PsdUiToolkitItemRole itemRole = PsdUiToolkitItemRole.FollowParent;

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
                participateInAutoLayout = true,
                childrenLayout = PsdUiToolkitContainerLayout.Unspecified,
                itemRole = PsdUiToolkitItemRole.FollowParent,
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
            if (!Enum.IsDefined(typeof(PsdUiToolkitContainerLayout), childrenLayout))
                childrenLayout = PsdUiToolkitContainerLayout.Unspecified;
            if (!Enum.IsDefined(typeof(PsdUiToolkitItemRole), itemRole))
                itemRole = PsdUiToolkitItemRole.FollowParent;
        }
    }

    [Serializable]
    public sealed class PsdUiToolkitVirtualGroupConfig
    {
        public string id = "";
        public string name = "";
        public int parentLayerId = -1;
        public int[] memberLayerIds = Array.Empty<int>();
        public PsdUiToolkitContainerLayout layout = PsdUiToolkitContainerLayout.Row;

        public void Sanitize()
        {
            id ??= string.Empty;
            name ??= string.Empty;
            memberLayerIds ??= Array.Empty<int>();
            if (layout != PsdUiToolkitContainerLayout.Row && layout != PsdUiToolkitContainerLayout.Column)
                layout = PsdUiToolkitContainerLayout.Row;

            HashSet<int> seen = new HashSet<int>();
            List<int> uniqueIds = new List<int>(memberLayerIds.Length);
            for (int i = 0; i < memberLayerIds.Length; i++)
            {
                if (seen.Add(memberLayerIds[i]))
                    uniqueIds.Add(memberLayerIds[i]);
            }

            memberLayerIds = uniqueIds.ToArray();
        }
    }

    [Serializable]
    public sealed class PsdUiToolkitExportConfigData
    {
        public const int CurrentConfigVersion = 2;

        public int configVersion;
        public PsdUiToolkitAutoLayoutGlobalConfig autoLayout = PsdUiToolkitAutoLayoutGlobalConfig.Default;
        public PsdUiToolkitLayerConfig[] layers = Array.Empty<PsdUiToolkitLayerConfig>();
        public PsdUiToolkitVirtualGroupConfig[] virtualGroups = Array.Empty<PsdUiToolkitVirtualGroupConfig>();
    }

    internal sealed class PsdUiToolkitLayerConfigMap
    {
        private readonly Dictionary<int, PsdUiToolkitLayerConfig> _lookup;
        private readonly PsdUiToolkitVirtualGroupConfig[] _virtualGroups;
        private readonly PsdUiToolkitAutoLayoutGlobalConfig _autoLayout;
        private readonly PsdUiToolkitAutoLayoutDetectionProfile _autoLayoutProfile;

        public PsdUiToolkitLayerConfigMap(PsdUiToolkitExportConfigData data)
        {
            _lookup = PsdUiToolkitConfigStore.BuildLookup(data);
            _virtualGroups = data?.virtualGroups ?? Array.Empty<PsdUiToolkitVirtualGroupConfig>();
            _autoLayout = data == null
                ? PsdUiToolkitAutoLayoutGlobalConfig.Default
                : data.autoLayout.GetValidated();
            _autoLayoutProfile = PsdUiToolkitAutoLayoutDetectionProfile.Resolve(_autoLayout);
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

        public PsdUiToolkitAutoLayoutDetectionProfile GetAutoLayoutProfile()
        {
            return _autoLayoutProfile;
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

        public bool ParticipateInAutoLayout(Layer layer)
        {
            PsdUiToolkitLayerConfig config = Get(layer);
            return config.exported
                && config.visible
                && config.participateInAutoLayout;
        }

        public PsdUiToolkitContainerLayout GetChildrenLayout(Layer layer)
        {
            return Get(layer).childrenLayout;
        }

        public PsdUiToolkitItemRole GetItemRole(Layer layer)
        {
            return Get(layer).itemRole;
        }

        public PsdUiToolkitVirtualGroupConfig[] GetVirtualGroups()
        {
            return _virtualGroups;
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

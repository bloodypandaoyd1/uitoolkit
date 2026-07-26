using System;
using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    internal enum PsdUiToolkitLayoutType
    {
        Absolute = 1,
        Row = 2,
        Column = 3,
    }

    public enum PsdUiToolkitContainerLayout
    {
        // Kept only so v1-v3 JSON can be migrated. The v4 editor never writes it.
        Unspecified = 0,
        Absolute = 1,
        Row = 2,
        Column = 3,
    }

    public enum PsdUiToolkitItemRole
    {
        FollowParent = 0,
        KeepAbsolute = 1,
    }

    public enum PsdUiToolkitMainAxisDistribution
    {
        PreservePsd = 0,
        Start = 1,
        Center = 2,
        End = 3,
        SpaceBetween = 4,
        SpaceAround = 5,
    }

    public enum PsdUiToolkitCrossAxisAlignment
    {
        PreservePsd = 0,
        Start = 1,
        Center = 2,
        End = 3,
    }

    public enum PsdUiToolkitWrapMode
    {
        NoWrap = 0,
        Wrap = 1,
    }

    public enum PsdUiToolkitMultiLineDistribution
    {
        PreservePsd = 0,
        Start = 1,
        Center = 2,
        End = 3,
    }

    public enum PsdUiToolkitNodeReferenceKind
    {
        Layer = 0,
        VirtualGroup = 1,
    }

    [Serializable]
    public struct PsdUiToolkitNodeReference : IEquatable<PsdUiToolkitNodeReference>
    {
        public PsdUiToolkitNodeReferenceKind kind;
        public int layerId;
        public string virtualGroupId;

        public static PsdUiToolkitNodeReference Layer(int id)
        {
            return new PsdUiToolkitNodeReference
            {
                kind = PsdUiToolkitNodeReferenceKind.Layer,
                layerId = id,
                virtualGroupId = string.Empty,
            };
        }

        public static PsdUiToolkitNodeReference VirtualGroup(string id)
        {
            return new PsdUiToolkitNodeReference
            {
                kind = PsdUiToolkitNodeReferenceKind.VirtualGroup,
                layerId = -1,
                virtualGroupId = id ?? string.Empty,
            };
        }

        public bool IsValid =>
            kind == PsdUiToolkitNodeReferenceKind.Layer
                ? layerId >= 0
                : !string.IsNullOrEmpty(virtualGroupId);

        public string StableKey =>
            kind == PsdUiToolkitNodeReferenceKind.Layer
                ? $"layer:{layerId}"
                : $"group:{virtualGroupId ?? string.Empty}";

        public void Sanitize()
        {
            if (!Enum.IsDefined(typeof(PsdUiToolkitNodeReferenceKind), kind))
                kind = PsdUiToolkitNodeReferenceKind.Layer;
            virtualGroupId ??= string.Empty;
            if (kind == PsdUiToolkitNodeReferenceKind.Layer)
                virtualGroupId = string.Empty;
            else
                layerId = -1;
        }

        public bool Equals(PsdUiToolkitNodeReference other)
        {
            return kind == other.kind
                && layerId == other.layerId
                && string.Equals(virtualGroupId ?? string.Empty, other.virtualGroupId ?? string.Empty, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PsdUiToolkitNodeReference other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)kind;
                hash = (hash * 397) ^ layerId;
                hash = (hash * 397) ^ (virtualGroupId ?? string.Empty).GetHashCode();
                return hash;
            }
        }
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
        public PsdUiToolkitContainerLayout childrenLayout = PsdUiToolkitContainerLayout.Absolute;
        public PsdUiToolkitItemRole itemRole = PsdUiToolkitItemRole.FollowParent;
        public PsdUiToolkitMainAxisDistribution mainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd;
        public PsdUiToolkitCrossAxisAlignment crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd;
        public PsdUiToolkitWrapMode wrapMode = PsdUiToolkitWrapMode.NoWrap;
        public PsdUiToolkitMultiLineDistribution multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd;

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
                childrenLayout = PsdUiToolkitContainerLayout.Absolute,
                itemRole = PsdUiToolkitItemRole.FollowParent,
                mainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd,
                crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd,
                wrapMode = PsdUiToolkitWrapMode.NoWrap,
                multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd,
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
            if (childrenLayout == PsdUiToolkitContainerLayout.Unspecified
                || !Enum.IsDefined(typeof(PsdUiToolkitContainerLayout), childrenLayout))
            {
                childrenLayout = PsdUiToolkitContainerLayout.Absolute;
            }
            // Config v4 serialized the removed Background role as 2.
            if ((int)itemRole == 2)
                itemRole = PsdUiToolkitItemRole.KeepAbsolute;
            else if (!Enum.IsDefined(typeof(PsdUiToolkitItemRole), itemRole))
                itemRole = PsdUiToolkitItemRole.FollowParent;
            if (!Enum.IsDefined(typeof(PsdUiToolkitMainAxisDistribution), mainAxisDistribution))
                mainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd;
            if (!Enum.IsDefined(typeof(PsdUiToolkitCrossAxisAlignment), crossAxisAlignment))
                crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd;
            if (!Enum.IsDefined(typeof(PsdUiToolkitWrapMode), wrapMode))
                wrapMode = PsdUiToolkitWrapMode.NoWrap;
            if (!Enum.IsDefined(typeof(PsdUiToolkitMultiLineDistribution), multiLineDistribution))
                multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd;
            if (childrenLayout != PsdUiToolkitContainerLayout.Row
                && childrenLayout != PsdUiToolkitContainerLayout.Column)
            {
                wrapMode = PsdUiToolkitWrapMode.NoWrap;
            }
        }
    }

    [Serializable]
    public sealed class PsdUiToolkitVirtualGroupConfig
    {
        public string id = "";
        public string name = "";
        public int hostParentLayerId = -1;
        public PsdUiToolkitNodeReference[] members = Array.Empty<PsdUiToolkitNodeReference>();
        public PsdUiToolkitContainerLayout layout = PsdUiToolkitContainerLayout.Row;
        public PsdUiToolkitWrapMode wrapMode = PsdUiToolkitWrapMode.NoWrap;
        public PsdUiToolkitMultiLineDistribution multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd;
        public PsdUiToolkitMainAxisDistribution mainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd;
        public PsdUiToolkitCrossAxisAlignment crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd;

        public void Sanitize()
        {
            id ??= string.Empty;
            name ??= string.Empty;
            members = PsdUiToolkitConfigSanitizer.SanitizeReferences(members);
            if (layout != PsdUiToolkitContainerLayout.Row && layout != PsdUiToolkitContainerLayout.Column)
                layout = PsdUiToolkitContainerLayout.Row;
            if (!Enum.IsDefined(typeof(PsdUiToolkitWrapMode), wrapMode))
                wrapMode = PsdUiToolkitWrapMode.NoWrap;
            if (!Enum.IsDefined(typeof(PsdUiToolkitMultiLineDistribution), multiLineDistribution))
                multiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd;
            if (!Enum.IsDefined(typeof(PsdUiToolkitMainAxisDistribution), mainAxisDistribution))
                mainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd;
            if (!Enum.IsDefined(typeof(PsdUiToolkitCrossAxisAlignment), crossAxisAlignment))
                crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd;
        }
    }

    [Serializable]
    public sealed class PsdUiToolkitExportConfigData
    {
        public const int CurrentConfigVersion = 4;

        public int configVersion;
        public PsdUiToolkitLayerConfig[] layers = Array.Empty<PsdUiToolkitLayerConfig>();
        public PsdUiToolkitVirtualGroupConfig[] virtualGroups = Array.Empty<PsdUiToolkitVirtualGroupConfig>();
    }

    internal sealed class PsdUiToolkitLayerConfigMap
    {
        private readonly Dictionary<int, PsdUiToolkitLayerConfig> _lookup;
        private readonly PsdUiToolkitVirtualGroupConfig[] _virtualGroups;

        public PsdUiToolkitLayerConfigMap(PsdUiToolkitExportConfigData data)
        {
            _lookup = PsdUiToolkitConfigStore.BuildLookup(data);
            _virtualGroups = data?.virtualGroups ?? Array.Empty<PsdUiToolkitVirtualGroupConfig>();
        }

        public PsdUiToolkitLayerConfig Get(Layer layer)
        {
            if (layer?.LayerId != null
                && _lookup.TryGetValue(layer.LayerId.Value, out PsdUiToolkitLayerConfig config))
            {
                config.Sanitize();
                return config;
            }

            PsdUiToolkitLayerConfig fallback = PsdUiToolkitLayerConfig.CreateDefault(layer);
            fallback.Sanitize();
            return fallback;
        }

        public bool IsExported(Layer layer) => Get(layer).exported;
        public bool IsVisible(Layer layer) => Get(layer).visible;
        public bool IsMergeExport(Layer layer) => Get(layer).merge;
        public bool GetSliceImage(Layer layer) => Get(layer).sliceImage;
        public bool ParticipateLocalDedup(Layer layer) => Get(layer).participateLocalDedup;
        public bool ParticipateCommonDedup(Layer layer) => Get(layer).participateCommonDedup;
        public bool LayerUsesCustomNineSliceParams(Layer layer) => GetSliceImage(layer) && Get(layer).useCustomNineSliceParams;

        public PsdUiToolkitNineSliceParams GetResolvedNineSliceParams(
            Layer layer,
            PsdUiToolkitNineSliceParams defaults)
        {
            PsdUiToolkitLayerConfig config = Get(layer);
            return config.sliceImage && config.useCustomNineSliceParams
                ? config.GetNineSliceParams()
                : defaults;
        }

        public PsdUiToolkitContainerLayout GetChildrenLayout(Layer layer) => Get(layer).childrenLayout;
        public PsdUiToolkitItemRole GetItemRole(Layer layer) => Get(layer).itemRole;
        public PsdUiToolkitMainAxisDistribution GetMainAxisDistribution(Layer layer) => Get(layer).mainAxisDistribution;
        public PsdUiToolkitCrossAxisAlignment GetCrossAxisAlignment(Layer layer) => Get(layer).crossAxisAlignment;
        public PsdUiToolkitWrapMode GetWrapMode(Layer layer) => Get(layer).wrapMode;
        public PsdUiToolkitMultiLineDistribution GetMultiLineDistribution(Layer layer) => Get(layer).multiLineDistribution;

        public PsdUiToolkitVirtualGroupConfig GetVirtualGroup(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < _virtualGroups.Length; i++)
            {
                if (_virtualGroups[i] != null
                    && string.Equals(_virtualGroups[i].id, id, StringComparison.Ordinal))
                {
                    return _virtualGroups[i];
                }
            }
            return null;
        }

        public PsdUiToolkitVirtualGroupConfig[] GetVirtualGroups() => _virtualGroups;
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

    internal static class PsdUiToolkitConfigSanitizer
    {
        public static PsdUiToolkitNodeReference[] SanitizeReferences(
            PsdUiToolkitNodeReference[] references)
        {
            references ??= Array.Empty<PsdUiToolkitNodeReference>();
            HashSet<PsdUiToolkitNodeReference> seen = new HashSet<PsdUiToolkitNodeReference>();
            List<PsdUiToolkitNodeReference> valid = new List<PsdUiToolkitNodeReference>();
            for (int i = 0; i < references.Length; i++)
            {
                PsdUiToolkitNodeReference reference = references[i];
                reference.Sanitize();
                if (reference.IsValid && seen.Add(reference))
                    valid.Add(reference);
            }
            return valid.ToArray();
        }
    }
}

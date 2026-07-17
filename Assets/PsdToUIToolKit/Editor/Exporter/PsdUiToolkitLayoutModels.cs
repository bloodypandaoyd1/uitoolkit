using System.Collections.Generic;
using PsdTools.Layers;

namespace PsdTools.UIToolKit
{
    internal sealed class PsdUiToolkitLayoutDiagnostic
    {
        public PsdUiToolkitLayoutDiagnostic(
            string code,
            string message,
            int layerId = -1,
            string virtualGroupId = null)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            LayerId = layerId;
            VirtualGroupId = virtualGroupId ?? string.Empty;
        }

        public string Code { get; }
        public string Message { get; }
        public int LayerId { get; }
        public string VirtualGroupId { get; }
    }

    internal sealed class PsdUiToolkitLayoutNode
    {
        public PsdUiToolkitLayoutNode(
            Layer sourceLayer,
            PsdUiToolkitLayerBounds bounds,
            bool renderAsLeaf,
            PsdUiToolkitLayoutType layoutType,
            int originalIndex,
            List<PsdUiToolkitLayoutNode> children,
            string displayName = null,
            bool isSynthetic = false,
            PsdUiToolkitItemRole itemRole = PsdUiToolkitItemRole.FollowParent,
            string virtualGroupId = null,
            PsdUiToolkitMainAxisDistribution mainAxisDistribution =
                PsdUiToolkitMainAxisDistribution.PreservePsd,
            PsdUiToolkitCrossAxisAlignment crossAxisAlignment =
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
            PsdUiToolkitWrapMode wrapMode = PsdUiToolkitWrapMode.NoWrap,
            PsdUiToolkitMultiLineDistribution multiLineDistribution =
                PsdUiToolkitMultiLineDistribution.PreservePsd)
        {
            SourceLayer = sourceLayer;
            Bounds = bounds;
            RenderAsLeaf = renderAsLeaf;
            LayoutType = layoutType;
            OriginalIndex = originalIndex;
            Children = children ?? new List<PsdUiToolkitLayoutNode>();
            DisplayName = string.IsNullOrEmpty(displayName)
                ? sourceLayer?.Name ?? string.Empty
                : displayName;
            IsSynthetic = isSynthetic;
            ItemRole = itemRole;
            VirtualGroupId = virtualGroupId ?? string.Empty;
            MainAxisDistribution = mainAxisDistribution;
            CrossAxisAlignment = crossAxisAlignment;
            WrapMode = layoutType == PsdUiToolkitLayoutType.Row
                || layoutType == PsdUiToolkitLayoutType.Column
                    ? wrapMode
                    : PsdUiToolkitWrapMode.NoWrap;
            MultiLineDistribution = multiLineDistribution;
        }

        public Layer SourceLayer { get; }
        public PsdUiToolkitLayerBounds Bounds { get; }
        public bool RenderAsLeaf { get; }
        public PsdUiToolkitLayoutType LayoutType { get; }
        public int OriginalIndex { get; }
        public List<PsdUiToolkitLayoutNode> Children { get; }
        public string DisplayName { get; }
        public bool IsSynthetic { get; }
        public PsdUiToolkitItemRole ItemRole { get; }
        public string VirtualGroupId { get; }
        public PsdUiToolkitMainAxisDistribution MainAxisDistribution { get; }
        public PsdUiToolkitCrossAxisAlignment CrossAxisAlignment { get; }
        public PsdUiToolkitWrapMode WrapMode { get; }
        public PsdUiToolkitMultiLineDistribution MultiLineDistribution { get; }

        public PsdUiToolkitNodeReference Reference =>
            !string.IsNullOrEmpty(VirtualGroupId)
                ? PsdUiToolkitNodeReference.VirtualGroup(VirtualGroupId)
                : PsdUiToolkitNodeReference.Layer(SourceLayer?.LayerId ?? -1);
    }

    internal sealed class PsdUiToolkitLayoutTree
    {
        public PsdUiToolkitLayoutTree(
            string rootName,
            int width,
            int height,
            List<PsdUiToolkitLayoutNode> children,
            List<string> warnings = null,
            List<PsdUiToolkitLayoutDiagnostic> diagnostics = null)
        {
            RootName = rootName ?? string.Empty;
            Width = width;
            Height = height;
            Children = children ?? new List<PsdUiToolkitLayoutNode>();
            Warnings = warnings ?? new List<string>();
            Diagnostics = diagnostics ?? new List<PsdUiToolkitLayoutDiagnostic>();
        }

        public string RootName { get; }
        public int Width { get; }
        public int Height { get; }
        public List<PsdUiToolkitLayoutNode> Children { get; }
        public List<string> Warnings { get; }
        public List<PsdUiToolkitLayoutDiagnostic> Diagnostics { get; }
    }
}

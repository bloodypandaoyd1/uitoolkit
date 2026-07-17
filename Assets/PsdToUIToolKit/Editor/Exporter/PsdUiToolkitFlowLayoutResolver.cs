using System;
using System.Collections.Generic;

namespace PsdTools.UIToolKit
{
    internal readonly struct PsdUiToolkitFlowChildPlacement
    {
        public PsdUiToolkitFlowChildPlacement(bool useFlow, int marginLeft, int marginTop)
        {
            UseFlow = useFlow;
            MarginLeft = Math.Max(0, marginLeft);
            MarginTop = Math.Max(0, marginTop);
        }

        public bool UseFlow { get; }
        public int MarginLeft { get; }
        public int MarginTop { get; }

        public static PsdUiToolkitFlowChildPlacement Absolute =>
            new PsdUiToolkitFlowChildPlacement(false, 0, 0);
    }

    internal sealed class PsdUiToolkitFlowContainerPlan
    {
        public PsdUiToolkitLayoutType LayoutType { get; set; }
        public bool UseFlow { get; set; }
        public int PaddingLeft { get; set; }
        public int PaddingTop { get; set; }
        public int PaddingRight { get; set; }
        public int PaddingBottom { get; set; }
        public PsdUiToolkitMainAxisDistribution MainAxisDistribution { get; set; }
        public PsdUiToolkitCrossAxisAlignment CrossAxisAlignment { get; set; }
        public PsdUiToolkitWrapMode WrapMode { get; set; }
        public PsdUiToolkitMultiLineDistribution MultiLineDistribution { get; set; }
        public int DerivedMainGap { get; set; }
        public int DerivedLineGap { get; set; }
        public List<PsdUiToolkitLayoutNode> FlowChildren { get; } =
            new List<PsdUiToolkitLayoutNode>();
        public Dictionary<PsdUiToolkitLayoutNode, PsdUiToolkitFlowChildPlacement> Placements { get; } =
            new Dictionary<PsdUiToolkitLayoutNode, PsdUiToolkitFlowChildPlacement>();

        public static PsdUiToolkitFlowContainerPlan Disabled(PsdUiToolkitLayoutType layoutType)
        {
            return new PsdUiToolkitFlowContainerPlan
            {
                LayoutType = layoutType,
                UseFlow = false,
                MainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd,
                CrossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd,
                WrapMode = PsdUiToolkitWrapMode.NoWrap,
                MultiLineDistribution = PsdUiToolkitMultiLineDistribution.PreservePsd,
            };
        }
    }

    internal static class PsdUiToolkitFlowLayoutResolver
    {
        private sealed class FlowLine
        {
            public readonly List<PsdUiToolkitLayoutNode> Children =
                new List<PsdUiToolkitLayoutNode>();
            public int CrossStart;
            public int CrossEnd;
        }

        public static PsdUiToolkitFlowContainerPlan Resolve(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitLayerConfigMap configMap)
        {
            _ = configMap;
            if (node == null || node.RenderAsLeaf || node.Children.Count == 0)
            {
                return PsdUiToolkitFlowContainerPlan.Disabled(
                    node?.LayoutType ?? PsdUiToolkitLayoutType.Absolute);
            }
            if (node.LayoutType != PsdUiToolkitLayoutType.Row
                && node.LayoutType != PsdUiToolkitLayoutType.Column)
            {
                return PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType);
            }

            PsdUiToolkitFlowContainerPlan plan = new PsdUiToolkitFlowContainerPlan
            {
                LayoutType = node.LayoutType,
                UseFlow = true,
                MainAxisDistribution = node.MainAxisDistribution,
                CrossAxisAlignment = node.CrossAxisAlignment,
                WrapMode = node.WrapMode,
                MultiLineDistribution = node.MultiLineDistribution,
            };
            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = node.Children[i];
                if (child != null
                    && (child.IsSynthetic
                        || child.ItemRole == PsdUiToolkitItemRole.FollowParent))
                {
                    plan.FlowChildren.Add(child);
                }
            }
            if (plan.FlowChildren.Count == 0)
                return PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType);

            ComputeContainerPadding(node, plan);
            if (plan.WrapMode == PsdUiToolkitWrapMode.Wrap)
                BuildWrappedPlacements(node, plan);
            else if (node.LayoutType == PsdUiToolkitLayoutType.Row)
                BuildRowPlacements(node, plan);
            else
                BuildColumnPlacements(node, plan);

            return plan.Placements.Count == 0
                ? PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType)
                : plan;
        }

        private static void ComputeContainerPadding(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitFlowContainerPlan plan)
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
                return;
            plan.PaddingLeft = Math.Max(0, minLeft);
            plan.PaddingTop = Math.Max(0, minTop);
            plan.PaddingRight = Math.Max(0, node.Bounds.Width - maxRight);
            plan.PaddingBottom = Math.Max(0, node.Bounds.Height - maxBottom);
        }

        private static void BuildRowPlacements(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitFlowContainerPlan plan)
        {
            int previousRight = 0;
            bool hasPrevious = false;
            for (int i = 0; i < plan.FlowChildren.Count; i++)
            {
                PsdUiToolkitLayoutNode child = plan.FlowChildren[i];
                int childLeft = Math.Max(
                    0,
                    child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                int childTop = Math.Max(
                    0,
                    child.Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                int marginLeft = hasPrevious
                    ? Math.Max(0, childLeft - previousRight)
                    : childLeft;
                if (plan.MainAxisDistribution
                    != PsdUiToolkitMainAxisDistribution.PreservePsd)
                {
                    marginLeft = 0;
                }
                if (plan.CrossAxisAlignment
                    != PsdUiToolkitCrossAxisAlignment.PreservePsd)
                {
                    childTop = 0;
                }
                plan.Placements[child] =
                    new PsdUiToolkitFlowChildPlacement(true, marginLeft, childTop);
                previousRight = childLeft + child.Bounds.Width;
                hasPrevious = true;
            }
        }

        private static void BuildColumnPlacements(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitFlowContainerPlan plan)
        {
            int previousBottom = 0;
            bool hasPrevious = false;
            for (int i = 0; i < plan.FlowChildren.Count; i++)
            {
                PsdUiToolkitLayoutNode child = plan.FlowChildren[i];
                int childLeft = Math.Max(
                    0,
                    child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                int childTop = Math.Max(
                    0,
                    child.Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                int marginTop = hasPrevious
                    ? Math.Max(0, childTop - previousBottom)
                    : childTop;
                if (plan.MainAxisDistribution
                    != PsdUiToolkitMainAxisDistribution.PreservePsd)
                {
                    marginTop = 0;
                }
                if (plan.CrossAxisAlignment
                    != PsdUiToolkitCrossAxisAlignment.PreservePsd)
                {
                    childLeft = 0;
                }
                plan.Placements[child] =
                    new PsdUiToolkitFlowChildPlacement(true, childLeft, marginTop);
                previousBottom = childTop + child.Bounds.Height;
                hasPrevious = true;
            }
        }

        private static void BuildWrappedPlacements(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitFlowContainerPlan plan)
        {
            bool row = node.LayoutType == PsdUiToolkitLayoutType.Row;
            List<FlowLine> lines = BuildLines(plan.FlowChildren, row);
            List<int> mainGaps = new List<int>();
            List<int> lineGaps = new List<int>();
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                FlowLine line = lines[lineIndex];
                for (int i = 1; i < line.Children.Count; i++)
                {
                    PsdUiToolkitLayoutNode previous = line.Children[i - 1];
                    PsdUiToolkitLayoutNode current = line.Children[i];
                    int gap = row
                        ? current.Bounds.Left - GetRight(previous.Bounds)
                        : current.Bounds.Top - GetBottom(previous.Bounds);
                    mainGaps.Add(Math.Max(0, gap));
                }
                if (lineIndex > 0)
                    lineGaps.Add(Math.Max(0, line.CrossStart - lines[lineIndex - 1].CrossEnd));
            }

            plan.DerivedMainGap = Median(mainGaps);
            plan.DerivedLineGap = Median(lineGaps);
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                FlowLine line = lines[lineIndex];
                for (int itemIndex = 0; itemIndex < line.Children.Count; itemIndex++)
                {
                    PsdUiToolkitLayoutNode child = line.Children[itemIndex];
                    int crossOffset = row
                        ? Math.Max(0, child.Bounds.Top - line.CrossStart)
                        : Math.Max(0, child.Bounds.Left - line.CrossStart);
                    if (plan.CrossAxisAlignment
                        != PsdUiToolkitCrossAxisAlignment.PreservePsd)
                    {
                        crossOffset = 0;
                    }

                    int mainGap = itemIndex == 0
                        || plan.MainAxisDistribution
                            != PsdUiToolkitMainAxisDistribution.PreservePsd
                            ? 0
                            : plan.DerivedMainGap;
                    int lineGap = lineIndex == 0
                        || plan.MultiLineDistribution
                            != PsdUiToolkitMultiLineDistribution.PreservePsd
                            ? 0
                            : plan.DerivedLineGap;
                    plan.Placements[child] = row
                        ? new PsdUiToolkitFlowChildPlacement(
                            true,
                            mainGap,
                            crossOffset + lineGap)
                        : new PsdUiToolkitFlowChildPlacement(
                            true,
                            crossOffset + lineGap,
                            mainGap);
                }
            }
        }

        private static List<FlowLine> BuildLines(
            List<PsdUiToolkitLayoutNode> children,
            bool row)
        {
            List<FlowLine> lines = new List<FlowLine>();
            for (int i = 0; i < children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = children[i];
                int crossStart = row ? child.Bounds.Top : child.Bounds.Left;
                int crossEnd = crossStart + (row ? child.Bounds.Height : child.Bounds.Width);
                FlowLine target = null;
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    FlowLine candidate = lines[lineIndex];
                    if (Math.Min(candidate.CrossEnd, crossEnd)
                        > Math.Max(candidate.CrossStart, crossStart))
                    {
                        target = candidate;
                        break;
                    }
                }
                if (target == null)
                {
                    target = new FlowLine
                    {
                        CrossStart = crossStart,
                        CrossEnd = crossEnd,
                    };
                    lines.Add(target);
                }
                else
                {
                    target.CrossStart = Math.Min(target.CrossStart, crossStart);
                    target.CrossEnd = Math.Max(target.CrossEnd, crossEnd);
                }
                target.Children.Add(child);
            }

            lines.Sort((left, right) => left.CrossStart.CompareTo(right.CrossStart));
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].Children.Sort(row
                    ? (Comparison<PsdUiToolkitLayoutNode>)CompareByLeftThenTop
                    : CompareByTopThenLeft);
            }
            return lines;
        }

        private static int Median(List<int> values)
        {
            if (values == null || values.Count == 0)
                return 0;
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2
                : values[middle];
        }

        private static int CompareByLeftThenTop(
            PsdUiToolkitLayoutNode left,
            PsdUiToolkitLayoutNode right)
        {
            int compare = left.Bounds.Left.CompareTo(right.Bounds.Left);
            return compare != 0 ? compare : left.Bounds.Top.CompareTo(right.Bounds.Top);
        }

        private static int CompareByTopThenLeft(
            PsdUiToolkitLayoutNode left,
            PsdUiToolkitLayoutNode right)
        {
            int compare = left.Bounds.Top.CompareTo(right.Bounds.Top);
            return compare != 0 ? compare : left.Bounds.Left.CompareTo(right.Bounds.Left);
        }

        private static int GetRight(PsdUiToolkitLayerBounds bounds) =>
            bounds.Left + bounds.Width;

        private static int GetBottom(PsdUiToolkitLayerBounds bounds) =>
            bounds.Top + bounds.Height;
    }
}

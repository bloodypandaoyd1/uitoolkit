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

    internal sealed class PsdUiToolkitGridRowPlan
    {
        public List<PsdUiToolkitLayoutNode> Children { get; } = new List<PsdUiToolkitLayoutNode>();
        public Dictionary<PsdUiToolkitLayoutNode, PsdUiToolkitFlowChildPlacement> Placements { get; } =
            new Dictionary<PsdUiToolkitLayoutNode, PsdUiToolkitFlowChildPlacement>();
        public int GapBefore { get; set; }
        public int Height { get; set; }
    }

    internal sealed class PsdUiToolkitFlowContainerPlan
    {
        public PsdUiToolkitLayoutType LayoutType { get; set; }
        public bool UseFlow { get; set; }
        public int PaddingLeft { get; set; }
        public int PaddingTop { get; set; }
        public int PaddingRight { get; set; }
        public int PaddingBottom { get; set; }
        public int InnerWidth { get; set; }
        public PsdUiToolkitMainAxisDistribution MainAxisDistribution { get; set; }
        public PsdUiToolkitCrossAxisAlignment CrossAxisAlignment { get; set; }
        public List<PsdUiToolkitLayoutNode> FlowChildren { get; } = new List<PsdUiToolkitLayoutNode>();
        public Dictionary<PsdUiToolkitLayoutNode, PsdUiToolkitFlowChildPlacement> Placements { get; } =
            new Dictionary<PsdUiToolkitLayoutNode, PsdUiToolkitFlowChildPlacement>();
        public List<PsdUiToolkitGridRowPlan> GridRows { get; } = new List<PsdUiToolkitGridRowPlan>();

        public static PsdUiToolkitFlowContainerPlan Disabled(PsdUiToolkitLayoutType layoutType)
        {
            return new PsdUiToolkitFlowContainerPlan
            {
                LayoutType = layoutType,
                UseFlow = false,
                MainAxisDistribution = PsdUiToolkitMainAxisDistribution.PreservePsd,
                CrossAxisAlignment = PsdUiToolkitCrossAxisAlignment.PreservePsd,
            };
        }
    }

    internal static class PsdUiToolkitFlowLayoutResolver
    {
        public static PsdUiToolkitFlowContainerPlan Resolve(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitLayerConfigMap configMap)
        {
            if (node == null || node.RenderAsLeaf || node.Children.Count == 0)
                return PsdUiToolkitFlowContainerPlan.Disabled(node?.LayoutType ?? PsdUiToolkitLayoutType.Absolute);

            if (node.LayoutType != PsdUiToolkitLayoutType.Row
                && node.LayoutType != PsdUiToolkitLayoutType.Column
                && node.LayoutType != PsdUiToolkitLayoutType.Grid)
            {
                return PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType);
            }

            PsdUiToolkitFlowContainerPlan plan = new PsdUiToolkitFlowContainerPlan
            {
                LayoutType = node.LayoutType,
                UseFlow = true,
                MainAxisDistribution = node.MainAxisDistribution,
                CrossAxisAlignment = node.CrossAxisAlignment,
            };

            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = node.Children[i];
                if (ShouldRenderAsFlowItem(child))
                    plan.FlowChildren.Add(child);
            }

            if (plan.FlowChildren.Count == 0)
                return PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType);

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
                return PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType);
            if ((plan.LayoutType == PsdUiToolkitLayoutType.Row || plan.LayoutType == PsdUiToolkitLayoutType.Column)
                && plan.Placements.Count == 0)
            {
                return PsdUiToolkitFlowContainerPlan.Disabled(node.LayoutType);
            }

            return plan;
        }

        private static bool ShouldRenderAsFlowItem(PsdUiToolkitLayoutNode childNode)
        {
            if (childNode == null)
                return false;
            if (childNode.IsSynthetic)
                return childNode.LayoutType != PsdUiToolkitLayoutType.Overlay;
            return childNode.ItemRole == PsdUiToolkitItemRole.FollowParent;
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
                int childLeft = Math.Max(0, child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                int childTop = Math.Max(0, child.Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                int marginLeft = hasPrevious ? Math.Max(0, childLeft - previousRight) : childLeft;

                if (plan.MainAxisDistribution != PsdUiToolkitMainAxisDistribution.PreservePsd)
                    marginLeft = 0;
                if (plan.CrossAxisAlignment != PsdUiToolkitCrossAxisAlignment.PreservePsd)
                    childTop = 0;

                plan.Placements[child] = new PsdUiToolkitFlowChildPlacement(true, marginLeft, childTop);
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
                int childLeft = Math.Max(0, child.Bounds.Left - node.Bounds.Left - plan.PaddingLeft);
                int childTop = Math.Max(0, child.Bounds.Top - node.Bounds.Top - plan.PaddingTop);
                int marginTop = hasPrevious ? Math.Max(0, childTop - previousBottom) : childTop;

                if (plan.MainAxisDistribution != PsdUiToolkitMainAxisDistribution.PreservePsd)
                    marginTop = 0;
                if (plan.CrossAxisAlignment != PsdUiToolkitCrossAxisAlignment.PreservePsd)
                    childLeft = 0;

                plan.Placements[child] = new PsdUiToolkitFlowChildPlacement(true, childLeft, marginTop);
                previousBottom = childTop + child.Bounds.Height;
                hasPrevious = true;
            }
        }

        private static void BuildGridPlans(
            PsdUiToolkitLayoutNode node,
            PsdUiToolkitFlowContainerPlan plan,
            PsdUiToolkitLayerConfigMap configMap)
        {
            List<List<PsdUiToolkitLayoutNode>> rows = BuildGridRows(plan.FlowChildren, configMap);
            int previousRowBottom = 0;
            bool hasPreviousRow = false;
            for (int i = 0; i < rows.Count; i++)
            {
                List<PsdUiToolkitLayoutNode> row = rows[i];
                if (row.Count == 0)
                    continue;

                PsdUiToolkitGridRowPlan rowPlan = new PsdUiToolkitGridRowPlan();
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
                    rowPlan.Placements[child] =
                        new PsdUiToolkitFlowChildPlacement(true, marginLeft, childTop);
                    previousRight = childLeft + child.Bounds.Width;
                    hasPrevious = true;
                }

                previousRowBottom = rowTop + rowPlan.Height;
                hasPreviousRow = true;
                plan.GridRows.Add(rowPlan);
            }
        }

        private static List<List<PsdUiToolkitLayoutNode>> BuildGridRows(
            List<PsdUiToolkitLayoutNode> flowChildren,
            PsdUiToolkitLayerConfigMap configMap)
        {
            List<List<PsdUiToolkitLayoutNode>> rows = new List<List<PsdUiToolkitLayoutNode>>();
            if (flowChildren.Count == 0)
                return rows;

            PsdUiToolkitAutoLayoutDetectionProfile profile = configMap.GetAutoLayoutProfile();
            int tolerance = Math.Max(4, Math.Max(profile.AlignmentTolerance, profile.GapTolerance));
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
                    anchors[anchors.Count - 1] =
                        ((anchors[anchors.Count - 1] * (row.Count - 1)) + top) / row.Count;
                }
            }

            for (int i = 0; i < rows.Count; i++)
                rows[i].Sort(CompareByLeftThenTop);
            return rows;
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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using PsdTools.Layers;
using PsdTools.Psd;
using UnityEngine;

namespace PsdTools.UIToolKit.Tests
{
    public sealed class PsdUiToolkitLayoutTests
    {
        [Test]
        public void MigrateVersionTwo_PreservesExistingIntentAndUsesPsdAxisDefaults()
        {
            PsdUiToolkitExportConfigData data = new PsdUiToolkitExportConfigData
            {
                configVersion = 2,
                layers = new[]
                {
                    new PsdUiToolkitLayerConfig
                    {
                        id = 7,
                        childrenLayout = PsdUiToolkitContainerLayout.Row,
                        itemRole = PsdUiToolkitItemRole.Background,
                        mainAxisDistribution = PsdUiToolkitMainAxisDistribution.End,
                        crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.Center,
                    },
                },
                virtualGroups = new[]
                {
                    new PsdUiToolkitVirtualGroupConfig
                    {
                        id = "group",
                        parentLayerId = -1,
                        memberLayerIds = new[] { 1, 2 },
                        layout = PsdUiToolkitContainerLayout.Column,
                        mainAxisDistribution = PsdUiToolkitMainAxisDistribution.End,
                        crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.Center,
                    },
                },
            };

            PsdUiToolkitConfigStore.MigrateToCurrentVersion(data);

            Assert.That(data.configVersion, Is.EqualTo(3));
            Assert.That(data.layers[0].childrenLayout, Is.EqualTo(PsdUiToolkitContainerLayout.Row));
            Assert.That(data.layers[0].itemRole, Is.EqualTo(PsdUiToolkitItemRole.Background));
            Assert.That(
                data.layers[0].mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.PreservePsd));
            Assert.That(
                data.layers[0].crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.PreservePsd));
            Assert.That(
                data.virtualGroups[0].mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.PreservePsd));
            Assert.That(
                data.virtualGroups[0].crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.PreservePsd));
        }

        [Test]
        public void MigrateVersionOne_MapsLegacyParticipationBeforeVersionThree()
        {
            PsdUiToolkitExportConfigData data = new PsdUiToolkitExportConfigData
            {
                configVersion = 1,
                layers = new[]
                {
                    new PsdUiToolkitLayerConfig
                    {
                        id = 1,
                        participateInAutoLayout = false,
                    },
                    new PsdUiToolkitLayerConfig
                    {
                        id = 2,
                        participateInAutoLayout = true,
                    },
                },
            };

            PsdUiToolkitConfigStore.MigrateToCurrentVersion(data);

            Assert.That(data.layers[0].itemRole, Is.EqualTo(PsdUiToolkitItemRole.KeepAbsolute));
            Assert.That(data.layers[1].itemRole, Is.EqualTo(PsdUiToolkitItemRole.FollowParent));
            Assert.That(
                data.layers[0].mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.PreservePsd));
        }

        [Test]
        public void Sanitize_InvalidAxisValues_ReturnsToPsdDefaults()
        {
            PsdUiToolkitLayerConfig layer = new PsdUiToolkitLayerConfig
            {
                mainAxisDistribution = (PsdUiToolkitMainAxisDistribution)999,
                crossAxisAlignment = (PsdUiToolkitCrossAxisAlignment)999,
            };
            PsdUiToolkitVirtualGroupConfig group = new PsdUiToolkitVirtualGroupConfig
            {
                memberLayerIds = new[] { 1, 2 },
                mainAxisDistribution = (PsdUiToolkitMainAxisDistribution)999,
                crossAxisAlignment = (PsdUiToolkitCrossAxisAlignment)999,
            };

            layer.Sanitize();
            group.Sanitize();

            Assert.That(
                layer.mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.PreservePsd));
            Assert.That(
                layer.crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.PreservePsd));
            Assert.That(
                group.mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.PreservePsd));
            Assert.That(
                group.crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.PreservePsd));
        }

        [Test]
        public void ResolveRow_PreservePsd_KeepsDerivedSpacingAndCrossOffsets()
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(10, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(40, 9, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.UseFlow, Is.True);
            Assert.That(plan.PaddingLeft, Is.EqualTo(10));
            Assert.That(plan.PaddingTop, Is.EqualTo(5));
            Assert.That(plan.PaddingRight, Is.EqualTo(40));
            Assert.That(plan.PaddingBottom, Is.EqualTo(21));
            Assert.That(plan.Placements[first].MarginLeft, Is.Zero);
            Assert.That(plan.Placements[first].MarginTop, Is.Zero);
            Assert.That(plan.Placements[second].MarginLeft, Is.EqualTo(10));
            Assert.That(plan.Placements[second].MarginTop, Is.EqualTo(4));
        }

        [Test]
        public void ResolveRow_SemanticAxes_ClearOnlyGeneratedChildOffsets()
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(10, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(40, 9, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitMainAxisDistribution.SpaceBetween,
                PsdUiToolkitCrossAxisAlignment.End,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(
                plan.MainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.SpaceBetween));
            Assert.That(
                plan.CrossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.End));
            Assert.That(plan.Placements[first].MarginLeft, Is.Zero);
            Assert.That(plan.Placements[first].MarginTop, Is.Zero);
            Assert.That(plan.Placements[second].MarginLeft, Is.Zero);
            Assert.That(plan.Placements[second].MarginTop, Is.Zero);
            Assert.That(plan.PaddingLeft, Is.EqualTo(10));
            Assert.That(plan.PaddingTop, Is.EqualTo(5));
        }

        [Test]
        public void ResolveColumn_PreservesVerticalGapAndHorizontalOffset()
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(12, 4, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(18, 24, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Column,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.Placements[second].MarginTop, Is.EqualTo(10));
            Assert.That(plan.Placements[second].MarginLeft, Is.EqualTo(6));
        }

        [Test]
        public void ResolveRow_OnlyFollowParentItemsParticipateInFlow()
        {
            PsdUiToolkitLayoutNode background = CreateLayerNode(
                0,
                PsdUiToolkitItemRole.Background);
            PsdUiToolkitLayoutNode flow = CreateLayerNode(
                1,
                PsdUiToolkitItemRole.FollowParent);
            PsdUiToolkitLayoutNode overlay = CreateLayerNode(
                2,
                PsdUiToolkitItemRole.KeepAbsolute);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                background,
                flow,
                overlay);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.FlowChildren, Is.EqualTo(new[] { flow }));
            Assert.That(plan.Placements.ContainsKey(background), Is.False);
            Assert.That(plan.Placements.ContainsKey(flow), Is.True);
            Assert.That(plan.Placements.ContainsKey(overlay), Is.False);
        }

        [Test]
        public void ConfigJsonRoundTrip_PreservesVirtualGroupAxisSettings()
        {
            PsdUiToolkitExportConfigData data = new PsdUiToolkitExportConfigData
            {
                configVersion = 3,
                virtualGroups = new[]
                {
                    new PsdUiToolkitVirtualGroupConfig
                    {
                        id = "group",
                        name = "Footer",
                        memberLayerIds = new[] { 4, 5 },
                        layout = PsdUiToolkitContainerLayout.Column,
                        mainAxisDistribution = PsdUiToolkitMainAxisDistribution.SpaceAround,
                        crossAxisAlignment = PsdUiToolkitCrossAxisAlignment.End,
                    },
                },
            };

            string json = JsonUtility.ToJson(data);
            PsdUiToolkitExportConfigData restored =
                JsonUtility.FromJson<PsdUiToolkitExportConfigData>(json);
            PsdUiToolkitConfigStore.MigrateToCurrentVersion(restored);

            Assert.That(restored.virtualGroups, Has.Length.EqualTo(1));
            Assert.That(
                restored.virtualGroups[0].mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.SpaceAround));
            Assert.That(
                restored.virtualGroups[0].crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.End));
        }

        [Test]
        public void LayoutHistory_UndoAndRedo_DoNotTouchExportFields()
        {
            PsdUiToolkitLayerConfig layer = new PsdUiToolkitLayerConfig
            {
                id = 10,
                exported = true,
                childrenLayout = PsdUiToolkitContainerLayout.Unspecified,
            };
            PsdUiToolkitExportConfigData data = new PsdUiToolkitExportConfigData
            {
                configVersion = 3,
                layers = new[] { layer },
            };
            PsdUiToolkitLayoutEditHistory history = new PsdUiToolkitLayoutEditHistory();
            history.Reset(data);
            layer.childrenLayout = PsdUiToolkitContainerLayout.Row;
            history.Record(data);
            layer.exported = false;

            Assert.That(history.Undo(data), Is.True);
            Assert.That(layer.childrenLayout, Is.EqualTo(PsdUiToolkitContainerLayout.Unspecified));
            Assert.That(layer.exported, Is.False);

            Assert.That(history.Redo(data), Is.True);
            Assert.That(layer.childrenLayout, Is.EqualTo(PsdUiToolkitContainerLayout.Row));
            Assert.That(layer.exported, Is.False);
        }

        [Test]
        public void LayoutHistory_RestoresVirtualGroupsAsIndependentCopies()
        {
            PsdUiToolkitExportConfigData data = new PsdUiToolkitExportConfigData
            {
                configVersion = 3,
                virtualGroups = Array.Empty<PsdUiToolkitVirtualGroupConfig>(),
            };
            PsdUiToolkitLayoutEditHistory history = new PsdUiToolkitLayoutEditHistory();
            history.Reset(data);
            data.virtualGroups = new[]
            {
                new PsdUiToolkitVirtualGroupConfig
                {
                    id = "group",
                    name = "Buttons",
                    memberLayerIds = new[] { 1, 2 },
                    layout = PsdUiToolkitContainerLayout.Row,
                },
            };
            history.Record(data);

            Assert.That(history.Undo(data), Is.True);
            Assert.That(data.virtualGroups, Is.Empty);
            Assert.That(history.Redo(data), Is.True);
            Assert.That(data.virtualGroups, Has.Length.EqualTo(1));
            Assert.That(data.virtualGroups[0].name, Is.EqualTo("Buttons"));
        }

        private static PsdUiToolkitLayerConfigMap CreateConfigMap()
        {
            return new PsdUiToolkitLayerConfigMap(new PsdUiToolkitExportConfigData
            {
                configVersion = 3,
            });
        }

        private static PsdUiToolkitLayoutNode CreateLeaf(
            int left,
            int top,
            int width,
            int height,
            int originalIndex)
        {
            return new PsdUiToolkitLayoutNode(
                null,
                new PsdUiToolkitLayerBounds(left, top, width, height),
                true,
                PsdUiToolkitLayoutType.Absolute,
                0f,
                string.Empty,
                originalIndex,
                new List<PsdUiToolkitLayoutNode>(),
                $"Leaf{originalIndex}",
                true);
        }

        private static PsdUiToolkitLayoutNode CreateLayerNode(
            int originalIndex,
            PsdUiToolkitItemRole itemRole)
        {
            Layer layer = new Layer(
                new LayerRecord
                {
                    Left = originalIndex * 20,
                    Top = 0,
                    Right = originalIndex * 20 + 10,
                    Bottom = 10,
                },
                new FileHeader());
            return new PsdUiToolkitLayoutNode(
                layer,
                new PsdUiToolkitLayerBounds(originalIndex * 20, 0, 10, 10),
                true,
                PsdUiToolkitLayoutType.Absolute,
                0f,
                string.Empty,
                originalIndex,
                new List<PsdUiToolkitLayoutNode>(),
                $"Layer{originalIndex}",
                false,
                itemRole: itemRole);
        }

        private static PsdUiToolkitLayoutNode CreateContainer(
            PsdUiToolkitLayoutType layout,
            PsdUiToolkitMainAxisDistribution mainAxis,
            PsdUiToolkitCrossAxisAlignment crossAxis,
            params PsdUiToolkitLayoutNode[] children)
        {
            return new PsdUiToolkitLayoutNode(
                null,
                new PsdUiToolkitLayerBounds(0, 0, 100, 40),
                false,
                layout,
                1f,
                string.Empty,
                0,
                new List<PsdUiToolkitLayoutNode>(children),
                "Container",
                true,
                mainAxisDistribution: mainAxis,
                crossAxisAlignment: crossAxis);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PsdTools.Layers;
using PsdTools.Psd;
using UnityEditor;

namespace PsdTools.UIToolKit.Tests
{
    public sealed class PsdUiToolkitLayoutTests
    {
        private const string GeneratedTestRoot =
            "Assets/PsdToUIToolKit/Tests/GeneratedV4";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(GeneratedTestRoot);
        }

        [Test]
        public void MigrateVersionOne_MapsParticipationAndRemovesUnspecified()
        {
            const string json =
                "{\"configVersion\":1,\"layers\":["
                + "{\"id\":1,\"participateInAutoLayout\":false,\"childrenLayout\":0},"
                + "{\"id\":2,\"participateInAutoLayout\":true,\"childrenLayout\":0}"
                + "]}";

            PsdUiToolkitExportConfigData data =
                PsdUiToolkitConfigStore.DeserializeAndMigrate(json);

            Assert.That(data.configVersion, Is.EqualTo(4));
            Assert.That(
                data.layers[0].itemRole,
                Is.EqualTo(PsdUiToolkitItemRole.KeepAbsolute));
            Assert.That(
                data.layers[1].itemRole,
                Is.EqualTo(PsdUiToolkitItemRole.FollowParent));
            Assert.That(
                data.layers[0].childrenLayout,
                Is.EqualTo(PsdUiToolkitContainerLayout.Absolute));
        }

        [Test]
        public void MigrateVersionThree_PreservesManualIntentAndAxes()
        {
            const string json =
                "{\"configVersion\":3,\"layers\":[{"
                + "\"id\":7,\"childrenLayout\":2,\"itemRole\":2,"
                + "\"mainAxisDistribution\":3,\"crossAxisAlignment\":2"
                + "}],\"virtualGroups\":[{"
                + "\"id\":\"group\",\"name\":\"Footer\",\"parentLayerId\":-1,"
                + "\"memberLayerIds\":[1,2],\"layout\":3,"
                + "\"mainAxisDistribution\":5,\"crossAxisAlignment\":3"
                + "}]}";

            PsdUiToolkitExportConfigData data =
                PsdUiToolkitConfigStore.DeserializeAndMigrate(json);

            Assert.That(data.configVersion, Is.EqualTo(4));
            Assert.That(
                data.layers[0].childrenLayout,
                Is.EqualTo(PsdUiToolkitContainerLayout.Row));
            Assert.That(
                data.layers[0].itemRole,
                Is.EqualTo(PsdUiToolkitItemRole.KeepAbsolute));
            Assert.That(
                data.layers[0].mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.End));
            Assert.That(data.virtualGroups[0].members, Has.Length.EqualTo(2));
            Assert.That(
                data.virtualGroups[0].members[1],
                Is.EqualTo(PsdUiToolkitNodeReference.Layer(2)));
            Assert.That(
                data.virtualGroups[0].crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.End));
        }

        [Test]
        public void MigrateVersionTwo_PreservesLayoutButUsesPsdAxisDefaults()
        {
            const string json =
                "{\"configVersion\":2,\"layers\":[{"
                + "\"id\":9,\"childrenLayout\":3,\"itemRole\":1,"
                + "\"mainAxisDistribution\":3,\"crossAxisAlignment\":2"
                + "}]}";

            PsdUiToolkitExportConfigData data =
                PsdUiToolkitConfigStore.DeserializeAndMigrate(json);

            Assert.That(
                data.layers[0].childrenLayout,
                Is.EqualTo(PsdUiToolkitContainerLayout.Column));
            Assert.That(
                data.layers[0].itemRole,
                Is.EqualTo(PsdUiToolkitItemRole.KeepAbsolute));
            Assert.That(
                data.layers[0].mainAxisDistribution,
                Is.EqualTo(PsdUiToolkitMainAxisDistribution.PreservePsd));
            Assert.That(
                data.layers[0].crossAxisAlignment,
                Is.EqualTo(PsdUiToolkitCrossAxisAlignment.PreservePsd));
        }

        [Test]
        public void SerializeVersionFour_DoesNotWriteLegacyDetectionFields()
        {
            PsdUiToolkitExportConfigData data =
                new PsdUiToolkitExportConfigData
                {
                    layers = new[]
                    {
                        new PsdUiToolkitLayerConfig { id = 1 },
                    },
                };

            string json = PsdUiToolkitConfigStore.Serialize(data);

            Assert.That(json, Does.Not.Contain("autoLayout"));
            Assert.That(json, Does.Not.Contain("participateInAutoLayout"));
            Assert.That(json, Does.Not.Contain("confidence"));
            Assert.That(json, Does.Not.Contain("Grid"));
        }

        [Test]
        public void NewLayerAndInvalidValues_SanitizeToAbsoluteDefaults()
        {
            PsdUiToolkitLayerConfig layer = new PsdUiToolkitLayerConfig
            {
                childrenLayout = PsdUiToolkitContainerLayout.Unspecified,
                wrapMode = (PsdUiToolkitWrapMode)999,
                multiLineDistribution =
                    (PsdUiToolkitMultiLineDistribution)999,
            };

            layer.Sanitize();

            Assert.That(
                layer.childrenLayout,
                Is.EqualTo(PsdUiToolkitContainerLayout.Absolute));
            Assert.That(layer.wrapMode, Is.EqualTo(PsdUiToolkitWrapMode.NoWrap));
            Assert.That(
                layer.multiLineDistribution,
                Is.EqualTo(PsdUiToolkitMultiLineDistribution.PreservePsd));
        }

        [Test]
        public void OrderChildrenForFlow_PreservesAbsolutePsdSlots()
        {
            PsdUiToolkitLayoutNode background = CreateRoleLeaf(
                "Background",
                0,
                PsdUiToolkitItemRole.KeepAbsolute);
            PsdUiToolkitLayoutNode right = CreateRoleLeaf(
                "Right",
                50,
                PsdUiToolkitItemRole.FollowParent);
            PsdUiToolkitLayoutNode left = CreateRoleLeaf(
                "Left",
                10,
                PsdUiToolkitItemRole.FollowParent);
            PsdUiToolkitLayoutNode overlay = CreateRoleLeaf(
                "Overlay",
                0,
                PsdUiToolkitItemRole.KeepAbsolute);
            List<PsdUiToolkitLayoutNode> children =
                new List<PsdUiToolkitLayoutNode>
                {
                    background,
                    right,
                    left,
                    overlay,
                };

            PsdUiToolkitManualLayoutBuilder.OrderChildrenForFlow(
                children,
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitWrapMode.NoWrap);

            Assert.That(children[0], Is.SameAs(background));
            Assert.That(children[1], Is.SameAs(left));
            Assert.That(children[2], Is.SameAs(right));
            Assert.That(children[3], Is.SameAs(overlay));
        }

        [Test]
        public void ResolveRow_PreservePsd_KeepsDerivedSpacingAndCrossOffsets()
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(10, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(40, 9, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitWrapMode.NoWrap,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                PsdUiToolkitMultiLineDistribution.PreservePsd,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.UseFlow, Is.True);
            Assert.That(plan.PaddingLeft, Is.EqualTo(10));
            Assert.That(plan.PaddingTop, Is.EqualTo(5));
            Assert.That(plan.PaddingRight, Is.EqualTo(40));
            Assert.That(plan.PaddingBottom, Is.EqualTo(21));
            Assert.That(plan.Placements[second].MarginLeft, Is.EqualTo(10));
            Assert.That(plan.Placements[second].MarginTop, Is.EqualTo(4));
        }

        [Test]
        public void ResolveWrap_DerivesRepresentativeItemAndLineGaps()
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(5, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(35, 5, 20, 10, 1);
            PsdUiToolkitLayoutNode third = CreateLeaf(5, 23, 20, 10, 2);
            PsdUiToolkitLayoutNode fourth = CreateLeaf(35, 23, 20, 10, 3);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitWrapMode.Wrap,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                PsdUiToolkitMultiLineDistribution.PreservePsd,
                first,
                second,
                third,
                fourth);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.WrapMode, Is.EqualTo(PsdUiToolkitWrapMode.Wrap));
            Assert.That(plan.DerivedMainGap, Is.EqualTo(10));
            Assert.That(plan.DerivedLineGap, Is.EqualTo(8));
            Assert.That(plan.Placements[second].MarginLeft, Is.EqualTo(10));
            Assert.That(plan.Placements[third].MarginTop, Is.EqualTo(8));
        }

        [TestCase(PsdUiToolkitMultiLineDistribution.Start)]
        [TestCase(PsdUiToolkitMultiLineDistribution.Center)]
        [TestCase(PsdUiToolkitMultiLineDistribution.End)]
        public void ResolveWrap_SemanticLineDistributionClearsPsdLineOffset(
            PsdUiToolkitMultiLineDistribution distribution)
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(5, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(5, 25, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitWrapMode.Wrap,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                distribution,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.MultiLineDistribution, Is.EqualTo(distribution));
            Assert.That(plan.Placements[second].MarginTop, Is.Zero);
        }

        [TestCase(PsdUiToolkitMainAxisDistribution.PreservePsd)]
        [TestCase(PsdUiToolkitMainAxisDistribution.Start)]
        [TestCase(PsdUiToolkitMainAxisDistribution.Center)]
        [TestCase(PsdUiToolkitMainAxisDistribution.End)]
        [TestCase(PsdUiToolkitMainAxisDistribution.SpaceBetween)]
        [TestCase(PsdUiToolkitMainAxisDistribution.SpaceAround)]
        public void ResolveRow_PreservesEveryMainAxisChoice(
            PsdUiToolkitMainAxisDistribution distribution)
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(5, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(35, 5, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitWrapMode.NoWrap,
                distribution,
                PsdUiToolkitCrossAxisAlignment.PreservePsd,
                PsdUiToolkitMultiLineDistribution.PreservePsd,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.MainAxisDistribution, Is.EqualTo(distribution));
            Assert.That(
                plan.Placements[second].MarginLeft,
                Is.EqualTo(
                    distribution
                        == PsdUiToolkitMainAxisDistribution.PreservePsd
                            ? 10
                            : 0));
        }

        [TestCase(PsdUiToolkitCrossAxisAlignment.PreservePsd)]
        [TestCase(PsdUiToolkitCrossAxisAlignment.Start)]
        [TestCase(PsdUiToolkitCrossAxisAlignment.Center)]
        [TestCase(PsdUiToolkitCrossAxisAlignment.End)]
        public void ResolveRow_PreservesEveryCrossAxisChoice(
            PsdUiToolkitCrossAxisAlignment alignment)
        {
            PsdUiToolkitLayoutNode first = CreateLeaf(5, 5, 20, 10, 0);
            PsdUiToolkitLayoutNode second = CreateLeaf(35, 9, 20, 10, 1);
            PsdUiToolkitLayoutNode parent = CreateContainer(
                PsdUiToolkitLayoutType.Row,
                PsdUiToolkitWrapMode.NoWrap,
                PsdUiToolkitMainAxisDistribution.PreservePsd,
                alignment,
                PsdUiToolkitMultiLineDistribution.PreservePsd,
                first,
                second);

            PsdUiToolkitFlowContainerPlan plan =
                PsdUiToolkitFlowLayoutResolver.Resolve(parent, CreateConfigMap());

            Assert.That(plan.CrossAxisAlignment, Is.EqualTo(alignment));
            Assert.That(
                plan.Placements[second].MarginTop,
                Is.EqualTo(
                    alignment
                        == PsdUiToolkitCrossAxisAlignment.PreservePsd
                            ? 4
                            : 0));
        }

        [Test]
        public void NestedVirtualGroups_RoundTripTwentyOneLevels()
        {
            List<PsdUiToolkitVirtualGroupConfig> groups =
                new List<PsdUiToolkitVirtualGroupConfig>();
            PsdUiToolkitNodeReference member = PsdUiToolkitNodeReference.Layer(1);
            for (int i = 0; i < 21; i++)
            {
                string id = $"group-{i}";
                groups.Add(new PsdUiToolkitVirtualGroupConfig
                {
                    id = id,
                    name = id,
                    hostParentLayerId = -1,
                    members = new[] { member },
                    layout = i % 2 == 0
                        ? PsdUiToolkitContainerLayout.Row
                        : PsdUiToolkitContainerLayout.Column,
                    wrapMode = PsdUiToolkitWrapMode.Wrap,
                });
                member = PsdUiToolkitNodeReference.VirtualGroup(id);
            }
            PsdUiToolkitExportConfigData data =
                new PsdUiToolkitExportConfigData
                {
                    virtualGroups = groups.ToArray(),
                };

            PsdUiToolkitExportConfigData restored =
                PsdUiToolkitConfigStore.DeserializeAndMigrate(
                    PsdUiToolkitConfigStore.Serialize(data));

            Assert.That(restored.virtualGroups, Has.Length.EqualTo(21));
            Assert.That(
                restored.virtualGroups[20].members[0],
                Is.EqualTo(PsdUiToolkitNodeReference.VirtualGroup("group-19")));
            Assert.That(
                restored.virtualGroups[20].wrapMode,
                Is.EqualTo(PsdUiToolkitWrapMode.Wrap));
        }

        [Test]
        public void Writer_KeepsLegacyUxmlShape()
        {
            WriteLayout(
                "Legacy",
                new[] { CreateVirtualLeaf("leaf", 0, 0, 20, 10, 0) },
                new PsdUiToolkitExportConfigData(),
                out string uxml,
                out string uss);

            Assert.That(uxml, Does.Not.Contain("<ui:Style"));
            Assert.That(uxml, Does.Not.Contain("<ui:Template"));
            Assert.That(uxml, Does.Not.Contain("<ui:Button"));
            Assert.That(uss, Does.Contain("Generated by PSDToUIToolKit"));
        }

        [Test]
        public void LayoutHistory_RestoresLayoutWithoutTouchingExportFields()
        {
            PsdUiToolkitLayerConfig layer = new PsdUiToolkitLayerConfig
            {
                id = 10,
                exported = true,
                childrenLayout = PsdUiToolkitContainerLayout.Absolute,
            };
            PsdUiToolkitExportConfigData data =
                new PsdUiToolkitExportConfigData
                {
                    layers = new[] { layer },
                };
            PsdUiToolkitLayoutEditHistory history =
                new PsdUiToolkitLayoutEditHistory();
            history.Reset(data);
            layer.childrenLayout = PsdUiToolkitContainerLayout.Row;
            history.Record(data);
            layer.exported = false;

            Assert.That(history.Undo(data), Is.True);
            Assert.That(
                layer.childrenLayout,
                Is.EqualTo(PsdUiToolkitContainerLayout.Absolute));
            Assert.That(layer.exported, Is.False);
            Assert.That(history.Redo(data), Is.True);
            Assert.That(
                layer.childrenLayout,
                Is.EqualTo(PsdUiToolkitContainerLayout.Row));
            Assert.That(layer.exported, Is.False);
        }

        private static void WriteLayout(
            string name,
            PsdUiToolkitLayoutNode[] nodes,
            PsdUiToolkitExportConfigData data,
            out string uxml,
            out string uss)
        {
            string pageUxml = GeneratedTestRoot + $"/{name}.generated.uxml";
            string pageUss = GeneratedTestRoot + $"/{name}.generated.uss";
            PsdUiToolkitUxmlWriter.Write(
                new PsdUiToolkitLayoutTree(
                    name,
                    200,
                    100,
                    new List<PsdUiToolkitLayoutNode>(nodes)),
                new PsdUiToolkitLayerConfigMap(data),
                new PsdUiToolkitRasterExportResult(),
                null,
                pageUxml,
                pageUss);
            uxml = File.ReadAllText(
                PsdUiToolkitAssetPathUtility.GetDiskPath(pageUxml));
            uss = File.ReadAllText(
                PsdUiToolkitAssetPathUtility.GetDiskPath(pageUss));
        }

        private static PsdUiToolkitLayerConfigMap CreateConfigMap()
        {
            return new PsdUiToolkitLayerConfigMap(
                new PsdUiToolkitExportConfigData());
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
                originalIndex,
                new List<PsdUiToolkitLayoutNode>(),
                $"Leaf{originalIndex}",
                true);
        }

        private static PsdUiToolkitLayoutNode CreateVirtualLeaf(
            string id,
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
                originalIndex,
                new List<PsdUiToolkitLayoutNode>(),
                id,
                true,
                virtualGroupId: id);
        }

        private static PsdUiToolkitLayoutNode CreateRoleLeaf(
            string name,
            int left,
            PsdUiToolkitItemRole itemRole)
        {
            return new PsdUiToolkitLayoutNode(
                null,
                new PsdUiToolkitLayerBounds(left, 0, 20, 10),
                true,
                PsdUiToolkitLayoutType.Absolute,
                0,
                new List<PsdUiToolkitLayoutNode>(),
                name,
                false,
                itemRole);
        }

        private static PsdUiToolkitLayoutNode CreateContainer(
            PsdUiToolkitLayoutType layout,
            PsdUiToolkitWrapMode wrap,
            PsdUiToolkitMainAxisDistribution mainAxis,
            PsdUiToolkitCrossAxisAlignment crossAxis,
            PsdUiToolkitMultiLineDistribution multiLine,
            params PsdUiToolkitLayoutNode[] children)
        {
            return new PsdUiToolkitLayoutNode(
                null,
                new PsdUiToolkitLayerBounds(0, 0, 100, 40),
                false,
                layout,
                0,
                new List<PsdUiToolkitLayoutNode>(children),
                "Container",
                true,
                mainAxisDistribution: mainAxis,
                crossAxisAlignment: crossAxis,
                wrapMode: wrap,
                multiLineDistribution: multiLine);
        }
    }
}

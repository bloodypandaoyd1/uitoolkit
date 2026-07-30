using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PsdTools.Layers;
using PsdTools.Psd;
using UnityEditor;
using UnityEngine;

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
        public void CreateDefaultConfig_DiscardsSavedLayerAndVirtualGroupSettings()
        {
            PsdImage psd = PsdImage.Open(
                PsdUiToolkitAssetPathUtility.GetDiskPath(
                    "Assets/PsdToUIToolKit/psdui.psd"));
            try
            {
                PsdUiToolkitExportConfigData saved =
                    PsdUiToolkitConfigStore.CreateDefaultConfig(psd);
                Assert.That(saved.layers, Is.Not.Empty);

                saved.layers[0].name = "Saved custom layer name";
                saved.layers[0].exported = false;
                saved.layers[0].merge = true;
                saved.layers[0].childrenLayout =
                    PsdUiToolkitContainerLayout.Row;
                saved.virtualGroups = new[]
                {
                    new PsdUiToolkitVirtualGroupConfig
                    {
                        id = "saved-group",
                        name = "Saved Group",
                    },
                };

                PsdUiToolkitExportConfigData reset =
                    PsdUiToolkitConfigStore.CreateDefaultConfig(psd);
                List<Layer> layers = new List<Layer>();
                PsdUiToolkitConfigStore.CollectLayers(psd.Root, layers);
                Dictionary<int, Layer> layersById = new Dictionary<int, Layer>();
                foreach (Layer layer in layers)
                {
                    if (layer.LayerId.HasValue)
                        layersById[layer.LayerId.Value] = layer;
                }

                Assert.That(reset.virtualGroups, Is.Empty);
                Assert.That(reset.layers, Has.Length.EqualTo(layersById.Count));
                foreach (PsdUiToolkitLayerConfig actual in reset.layers)
                {
                    Assert.That(layersById.ContainsKey(actual.id), Is.True);
                    PsdUiToolkitLayerConfig expected =
                        PsdUiToolkitLayerConfig.CreateDefault(
                            layersById[actual.id]);
                    Assert.That(
                        JsonUtility.ToJson(actual),
                        Is.EqualTo(JsonUtility.ToJson(expected)));
                }

                string resetJson = PsdUiToolkitConfigStore.Serialize(reset);
                Assert.That(resetJson, Does.Not.Contain("Saved custom layer name"));
                Assert.That(resetJson, Does.Not.Contain("saved-group"));
            }
            finally
            {
                psd.ReleaseAllData();
            }
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
        public void TextFillColor_UsesArgbChannelsAndClampsValues()
        {
            bool converted = PsdUiToolkitTextEffectsHelper.TryConvertFillColor(
                new[] { 0.5f, 1.2f, 0.5f, -0.1f },
                out Color color);

            Assert.That(converted, Is.True);
            Assert.That(color.a, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(color.r, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(color.g, Is.EqualTo(128f / 255f).Within(0.0001f));
            Assert.That(color.b, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void TextOutlineWidth_MatchesExportFormulaAndClamps()
        {
            Assert.That(
                PsdUiToolkitTextEffectsHelper.CalculateOutlineWidth(20f, 2f),
                Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(
                PsdUiToolkitTextEffectsHelper.CalculateOutlineWidth(1f, 10f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TextStrokeParser_ReadsCurrentPsdStroke()
        {
            PsdImage psd = PsdImage.Open(
                PsdUiToolkitAssetPathUtility.GetDiskPath(
                    "Assets/PsdToUIToolKit/psdui.psd"));
            try
            {
                List<Layer> layers = new List<Layer>();
                PsdUiToolkitConfigStore.CollectLayers(psd.Root, layers);
                Layer buyText = layers.Find(layer => layer.LayerId == 177);
                Assert.That(buyText, Is.Not.Null);
                Assert.That(
                    PsdUiToolkitTextEffectsHelper.TryGetStrokeEffect(
                        buyText,
                        out Color strokeColor,
                        out float strokeSize),
                    Is.True);
                Assert.That(strokeSize, Is.EqualTo(2f).Within(0.0001f));
                Assert.That(strokeColor.a, Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                psd.ReleaseAllData();
            }
        }

        [Test]
        public void PsdPreviewOutline_ExpandsAlphaWithoutChangingSourcePixel()
        {
            Texture2D source = new Texture2D(
                1,
                1,
                TextureFormat.RGBA32,
                false);
            source.SetPixel(0, 0, Color.white);
            source.Apply();

            Texture2D outlined =
                PsdUiToolkitPsdPreviewCompositor.CreateTextOutlineTexture(
                    source,
                    Color.red,
                    1f,
                    out int expansion);
            try
            {
                Assert.That(expansion, Is.EqualTo(1));
                Assert.That(outlined.width, Is.EqualTo(3));
                Assert.That(outlined.height, Is.EqualTo(3));
                Assert.That(outlined.GetPixel(1, 1), Is.EqualTo(Color.white));
                Assert.That(outlined.GetPixel(1, 0).r, Is.EqualTo(1f));
                Assert.That(outlined.GetPixel(1, 0).a, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(outlined);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void PsdPreviewLayerOrder_TopLayerOccludesTextOutline()
        {
            Texture2D source = new Texture2D(
                1,
                1,
                TextureFormat.RGBA32,
                false);
            source.SetPixel(0, 0, Color.white);
            source.Apply();
            Texture2D outlined =
                PsdUiToolkitPsdPreviewCompositor.CreateTextOutlineTexture(
                    source,
                    Color.red,
                    1f,
                    out _);
            try
            {
                Color32[] canvas = new Color32[9];
                PsdUiToolkitPsdPreviewCompositor.BlitPixelRectOntoCanvas(
                    outlined.GetPixels32(),
                    0,
                    0,
                    3,
                    3,
                    1f,
                    canvas,
                    3,
                    3);
                PsdUiToolkitPsdPreviewCompositor.BlitPixelRectOntoCanvas(
                    new[] { new Color32(0, 0, 255, 255) },
                    1,
                    0,
                    1,
                    1,
                    1f,
                    canvas,
                    3,
                    3);

                Assert.That(canvas[7], Is.EqualTo(
                    new Color32(0, 0, 255, 255)));
            }
            finally
            {
                Object.DestroyImmediate(outlined);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void PsdPreviewCompositor_AddsStrokeAndPreservesBaseComposition()
        {
            PsdImage psd = PsdImage.Open(
                PsdUiToolkitAssetPathUtility.GetDiskPath(
                    "Assets/PsdToUIToolKit/psdui.psd"));
            try
            {
                List<Layer> layers = new List<Layer>();
                PsdUiToolkitConfigStore.CollectLayers(psd.Root, layers);
                List<Layer> strokedTextLayers = layers.FindAll(
                    layer => layer.Kind == LayerKind.Type
                        && PsdUiToolkitTextEffectsHelper.TryGetStrokeEffect(
                            layer,
                            out _,
                            out _));
                Assert.That(strokedTextLayers, Is.Not.Empty);

                Texture2D basePreview = psd.CompositeFromLayersOnly();
                Texture2D strokePreview =
                    PsdUiToolkitPsdPreviewCompositor.Create(psd);
                try
                {
                    Assert.That(strokePreview.width, Is.EqualTo(psd.Width));
                    Assert.That(strokePreview.height, Is.EqualTo(psd.Height));
                    Color32[] basePixels = basePreview.GetPixels32();
                    Color32[] strokePixels = strokePreview.GetPixels32();
                    int changedPixels = 0;
                    for (int index = 0;
                        index < basePixels.Length;
                        index++)
                    {
                        if (!basePixels[index].Equals(strokePixels[index]))
                            changedPixels++;
                    }
                    Assert.That(changedPixels, Is.GreaterThan(0));
                }
                finally
                {
                    Object.DestroyImmediate(strokePreview);
                    Object.DestroyImmediate(basePreview);
                }

                bool[] originalVisibility =
                    new bool[strokedTextLayers.Count];
                for (int index = 0;
                    index < strokedTextLayers.Count;
                    index++)
                {
                    originalVisibility[index] =
                        strokedTextLayers[index].Visible;
                    strokedTextLayers[index].Visible = false;
                }

                try
                {
                    Texture2D baseWithoutStrokes =
                        psd.CompositeFromLayersOnly();
                    Texture2D previewWithoutStrokes =
                        PsdUiToolkitPsdPreviewCompositor.Create(psd);
                    try
                    {
                        CollectionAssert.AreEqual(
                            baseWithoutStrokes.GetPixels32(),
                            previewWithoutStrokes.GetPixels32());
                    }
                    finally
                    {
                        Object.DestroyImmediate(previewWithoutStrokes);
                        Object.DestroyImmediate(baseWithoutStrokes);
                    }
                }
                finally
                {
                    for (int index = 0;
                        index < strokedTextLayers.Count;
                        index++)
                    {
                        strokedTextLayers[index].Visible =
                            originalVisibility[index];
                    }
                }
            }
            finally
            {
                psd.ReleaseAllData();
            }
        }

        [Test]
        public void FontMapping_BlankOrMissingAssetFallsBackToDefault()
        {
            PsdUiToolkitFontMappingLookup lookup =
                new PsdUiToolkitFontMappingLookup(
                    new PsdUiToolkitFontMappingData
                    {
                        entries = new[]
                        {
                            new PsdUiToolkitFontMappingEntry
                            {
                                psdFontName = "Blank",
                                fontAssetPath = string.Empty,
                            },
                        },
                    });

            Assert.That(lookup.ResolveAsset("Blank"), Is.Null);
            Assert.That(lookup.ResolveAsset("Missing"), Is.Null);
            Assert.That(lookup.ResolveStyleUri("Blank"), Is.Empty);
        }

        [Test]
        public void ExportOutputPaths_UseFinalNamesWithoutGeneratedDraft()
        {
            PsdUiToolkitExporter.GetOutputAssetPaths(
                "/tmp/Shop Screen.psd",
                "Assets/Generated/Uxml/",
                out string uxmlPath,
                out string ussPath);

            Assert.That(
                uxmlPath,
                Is.EqualTo("Assets/Generated/Uxml/Shop Screen.uxml"));
            Assert.That(
                ussPath,
                Is.EqualTo("Assets/Generated/Uxml/Shop Screen.uss"));
            Assert.That(uxmlPath, Does.Not.Contain(".generated."));
            Assert.That(ussPath, Does.Not.Contain(".generated."));
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

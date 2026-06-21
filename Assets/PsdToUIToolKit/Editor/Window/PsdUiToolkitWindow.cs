using System;
using System.Collections.Generic;
using System.IO;
using PsdTools.Layers;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PsdTools.UIToolKit
{
    public sealed class PsdUiToolkitWindow : EditorWindow
    {
        private PsdImage _psd;
        private string _psdPath;
        private PsdUiToolkitExportConfigData _configData;
        private PsdUiToolkitLayerConfigMap _configMap;
        private Layer _selectedLayer;
        private Texture2D _selectedLayerPreview;
        private Texture2D _psdCompositePreview;

        private Label _statusLabel;
        private ScrollView _layerTreeScroll;
        private ScrollView _inspectorScroll;
        private readonly Dictionary<Layer, VisualElement> _layerRows = new Dictionary<Layer, VisualElement>();
        private Image _previewImage;
        private Label _previewDetailsLabel;
        private Label _canvasTitleLabel;
        private Label _canvasEmptyLabel;
        private VisualElement _canvasViewport;
        private VisualElement _canvasSurface;
        private VisualElement _canvasOverlay;
        private Image _canvasImage;
        private TextField _imageExportRootField;
        private TextField _uxmlExportRootField;
        private Toggle _autoImageNamingToggle;

        private Vector2 _lastCanvasClickPsdPosition = new Vector2(-99999f, -99999f);
        private readonly List<Layer> _canvasClickCandidates = new List<Layer>();
        private int _canvasClickCandidateIndex;
        private bool _canvasShowSelection = true;
        private float _canvasDrawWidth;
        private float _canvasDrawHeight;

        private const float CanvasSameClickThreshold = 5f;

        [MenuItem("Tools/PSD/UI Toolkit Editor")]
        public static void ShowWindow()
        {
            PsdUiToolkitWindow window = GetWindow<PsdUiToolkitWindow>("PSD UI Toolkit");
            window.minSize = new Vector2(1100f, 680f);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            rootVisualElement.Add(BuildToolbar());
            rootVisualElement.Add(BuildBody());
            rootVisualElement.Add(BuildStatusBar());

            RefreshView();
        }

        private VisualElement BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();

            ToolbarButton openButton = new ToolbarButton(OpenPsd) { text = "Open PSD" };
            ToolbarButton reloadButton = new ToolbarButton(ReloadPsd) { text = "Reload" };
            ToolbarButton exportButton = new ToolbarButton(ExportCurrentPsd) { text = "Export UXML + Images" };

            toolbar.Add(openButton);
            toolbar.Add(reloadButton);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(exportButton);

            return toolbar;
        }

        private VisualElement BuildBody()
        {
            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.flexDirection = FlexDirection.Row;

            VisualElement leftPanel = new VisualElement();
            leftPanel.style.width = 320f;
            leftPanel.style.flexShrink = 0f;
            leftPanel.style.borderRightWidth = 1f;
            leftPanel.style.borderRightColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            leftPanel.style.paddingLeft = 8f;
            leftPanel.style.paddingRight = 8f;
            leftPanel.style.paddingTop = 8f;
            leftPanel.style.paddingBottom = 8f;
            leftPanel.Add(new Label("Layer Tree") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            _layerTreeScroll = new ScrollView();
            _layerTreeScroll.style.flexGrow = 1f;
            _layerTreeScroll.style.marginTop = 6f;
            leftPanel.Add(_layerTreeScroll);

            VisualElement centerPanel = new VisualElement();
            centerPanel.style.flexGrow = 1f;
            centerPanel.style.paddingLeft = 8f;
            centerPanel.style.paddingRight = 8f;
            centerPanel.style.paddingTop = 8f;
            centerPanel.style.paddingBottom = 8f;

            _canvasTitleLabel = new Label("Canvas Preview") { style = { unityFontStyleAndWeight = FontStyle.Bold } };
            centerPanel.Add(_canvasTitleLabel);

            _canvasViewport = new VisualElement();
            _canvasViewport.style.flexGrow = 1f;
            _canvasViewport.style.marginTop = 6f;
            _canvasViewport.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
            _canvasViewport.style.overflow = Overflow.Hidden;
            _canvasViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateCanvasGeometry());
            centerPanel.Add(_canvasViewport);

            _canvasSurface = new VisualElement();
            _canvasSurface.style.position = Position.Absolute;
            _canvasSurface.style.backgroundColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            _canvasSurface.style.overflow = Overflow.Hidden;
            _canvasSurface.RegisterCallback<PointerDownEvent>(OnCanvasPointerDown);
            _canvasViewport.Add(_canvasSurface);

            _canvasImage = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _canvasImage.style.position = Position.Absolute;
            _canvasImage.style.left = 0f;
            _canvasImage.style.top = 0f;
            _canvasImage.style.right = 0f;
            _canvasImage.style.bottom = 0f;
            _canvasSurface.Add(_canvasImage);

            _canvasOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _canvasOverlay.style.position = Position.Absolute;
            _canvasOverlay.style.left = 0f;
            _canvasOverlay.style.top = 0f;
            _canvasOverlay.style.right = 0f;
            _canvasOverlay.style.bottom = 0f;
            _canvasSurface.Add(_canvasOverlay);

            _canvasEmptyLabel = new Label("Open a PSD to display the canvas.");
            _canvasEmptyLabel.style.position = Position.Absolute;
            _canvasEmptyLabel.style.left = 12f;
            _canvasEmptyLabel.style.top = 12f;
            _canvasViewport.Add(_canvasEmptyLabel);

            VisualElement rightPanel = new VisualElement();
            rightPanel.style.width = 360f;
            rightPanel.style.flexShrink = 0f;
            rightPanel.style.borderLeftWidth = 1f;
            rightPanel.style.borderLeftColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            rightPanel.style.paddingLeft = 8f;
            rightPanel.style.paddingRight = 8f;
            rightPanel.style.paddingTop = 8f;
            rightPanel.style.paddingBottom = 8f;
            rightPanel.Add(new Label("Inspector") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            _inspectorScroll = new ScrollView();
            _inspectorScroll.style.flexGrow = 1f;
            _inspectorScroll.style.marginTop = 6f;
            rightPanel.Add(_inspectorScroll);

            body.Add(leftPanel);
            body.Add(centerPanel);
            body.Add(rightPanel);
            return body;
        }

        private VisualElement BuildStatusBar()
        {
            VisualElement statusBar = new VisualElement();
            statusBar.style.flexDirection = FlexDirection.Row;
            statusBar.style.paddingLeft = 8f;
            statusBar.style.paddingRight = 8f;
            statusBar.style.paddingTop = 4f;
            statusBar.style.paddingBottom = 4f;
            statusBar.style.borderTopWidth = 1f;
            statusBar.style.borderTopColor = new Color(0.16f, 0.16f, 0.16f, 1f);

            _statusLabel = new Label("Open a PSD to begin.");
            _statusLabel.style.flexGrow = 1f;
            statusBar.Add(_statusLabel);
            return statusBar;
        }

        private void OpenPsd()
        {
            string path = EditorUtility.OpenFilePanel("Open PSD", string.IsNullOrEmpty(_psdPath) ? string.Empty : Path.GetDirectoryName(_psdPath), "psd,psb");
            if (string.IsNullOrEmpty(path))
                return;

            LoadPsd(path);
        }

        private void ReloadPsd()
        {
            if (string.IsNullOrEmpty(_psdPath))
                return;

            LoadPsd(_psdPath);
        }

        private void LoadPsd(string path)
        {
            ReleaseCurrentPsd();
            _psdPath = path;

            try
            {
                _psd = PsdImage.Open(path);
                _configData = PsdUiToolkitConfigStore.LoadAndSync(_psdPath, _psd);
                PsdUiToolkitConfigStore.ApplyToPsd(_psd, _configData);
                _configMap = new PsdUiToolkitLayerConfigMap(_configData);
                _selectedLayer = _psd.Children.Count > 0 ? _psd.Children[0] : null;
                _canvasShowSelection = _selectedLayer != null;
                _canvasClickCandidates.Clear();
                RefreshCompositePreview();
                UpdateStatus($"Loaded {Path.GetFileName(path)} ({_psd.Width}x{_psd.Height})");
                RefreshView();
            }
            catch (Exception ex)
            {
                ReleaseCurrentPsd();
                UpdateStatus($"Failed to load PSD: {ex.Message}");
                EditorUtility.DisplayDialog("PSD UI Toolkit", $"Failed to load PSD:\n\n{ex.Message}", "OK");
            }
        }

        private void ReleaseCurrentPsd()
        {
            DestroySelectedPreview();
            DestroyCompositePreview();
            _selectedLayer = null;
            _configMap = null;
            _configData = null;
            _psd?.ReleaseAllData();
            _psd = null;
            _canvasClickCandidates.Clear();
            if (_canvasImage != null)
                _canvasImage.image = null;
        }

        private void RefreshView()
        {
            RebuildLayerTree();
            RebuildInspector();
            UpdatePreview();
            UpdateCanvasGeometry();
        }

        private void RebuildLayerTree()
        {
            _layerTreeScroll?.Clear();
            _layerRows.Clear();
            if (_layerTreeScroll == null)
                return;

            if (_psd == null)
            {
                _layerTreeScroll.Add(new HelpBox("Open a PSD file to inspect and export it as UI Toolkit.", HelpBoxMessageType.Info));
                return;
            }

            if (_configMap != null && _configMap.GetAutoLayoutConfig().rebuildLayoutTree)
            {
                try
                {
                    string rootName = string.IsNullOrEmpty(_psdPath) ? "PSD" : Path.GetFileNameWithoutExtension(_psdPath);
                    PsdUiToolkitLayoutTree analysisTree = BuildCurrentAnalysisTree(rootName);
                    for (int i = 0; i < analysisTree.Children.Count; i++)
                        AddLayoutNodeRow(analysisTree.Children[i], 0);
                    return;
                }
                catch (Exception ex)
                {
                    _layerTreeScroll.Add(new HelpBox($"Rebuild analysis failed: {ex.Message}", HelpBoxMessageType.Error));
                    return;
                }
            }

            foreach (Layer child in _psd.Children)
                AddLayerRow(child, 0);
        }

        private void AddLayerRow(Layer layer, int depth)
        {
            Button row = new Button(() => SelectLayer(layer));
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.style.marginBottom = 2f;
            row.style.height = 24f;
            row.style.paddingLeft = 6f + depth * 14f;
            row.style.paddingRight = 6f;
            row.style.backgroundColor = _selectedLayer == layer ? new Color(0.18f, 0.35f, 0.58f, 0.55f) : new Color(0f, 0f, 0f, 0f);

            string layerKind = layer.Kind == LayerKind.Group ? "Group" : layer.Kind.ToString();
            string exportMarker = _configMap != null && !_configMap.IsExported(layer) ? "[Off] " : string.Empty;
            string visibilityMarker = layer.Visible ? string.Empty : "[Hidden] ";
            row.text = $"{exportMarker}{visibilityMarker}{new string(' ', depth * 2)}{layer.Name} ({layerKind})";
            _layerTreeScroll.Add(row);
            _layerRows[layer] = row;

            if (!layer.IsGroup)
                return;

            foreach (Layer child in layer.Children)
                AddLayerRow(child, depth + 1);
        }

        private void AddLayoutNodeRow(PsdUiToolkitLayoutNode node, int depth)
        {
            if (node == null)
                return;

            bool isSelected = node.SourceLayer != null && _selectedLayer == node.SourceLayer;
            bool containsSelection = node.IsSynthetic && NodeContainsSelectedLayer(node, _selectedLayer);
            Button row = node.SourceLayer == null
                ? new Button()
                : new Button(() => SelectLayer(node.SourceLayer));
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.style.marginBottom = 2f;
            row.style.height = 24f;
            row.style.paddingLeft = 6f + depth * 14f;
            row.style.paddingRight = 6f;
            row.style.backgroundColor = isSelected
                ? new Color(0.18f, 0.35f, 0.58f, 0.55f)
                : (containsSelection ? new Color(0.24f, 0.24f, 0.16f, 0.45f) : new Color(0f, 0f, 0f, 0f));

            string nodeName = string.IsNullOrEmpty(node.DisplayName)
                ? (node.SourceLayer?.Name ?? "Unnamed")
                : node.DisplayName;
            string prefix = node.IsSynthetic ? "[Auto] " : string.Empty;
            string exportMarker = node.SourceLayer != null && _configMap != null && !_configMap.IsExported(node.SourceLayer) ? "[Off] " : string.Empty;
            string visibilityMarker = node.SourceLayer != null && !node.SourceLayer.Visible ? "[Hidden] " : string.Empty;
            string kindLabel = node.IsSynthetic
                ? node.LayoutType.ToString()
                : (node.SourceLayer == null ? "Layout" : (node.SourceLayer.Kind == LayerKind.Group ? "Group" : node.SourceLayer.Kind.ToString()));
            row.text = $"{exportMarker}{visibilityMarker}{new string(' ', depth * 2)}{prefix}{nodeName} ({kindLabel})";
            row.tooltip = string.IsNullOrEmpty(node.RebuildReason)
                ? node.AnalysisSummary
                : $"{node.RebuildReason}\n{node.AnalysisSummary}";
            if (node.SourceLayer == null)
                row.SetEnabled(false);

            _layerTreeScroll.Add(row);
            if (node.SourceLayer != null && !_layerRows.ContainsKey(node.SourceLayer))
                _layerRows.Add(node.SourceLayer, row);

            for (int i = 0; i < node.Children.Count; i++)
                AddLayoutNodeRow(node.Children[i], depth + 1);
        }

        private static bool NodeContainsSelectedLayer(PsdUiToolkitLayoutNode node, Layer selectedLayer)
        {
            if (node == null || selectedLayer == null)
                return false;
            if (node.SourceLayer == selectedLayer)
                return true;

            for (int i = 0; i < node.Children.Count; i++)
            {
                if (NodeContainsSelectedLayer(node.Children[i], selectedLayer))
                    return true;
            }

            return false;
        }

        private void SelectLayer(Layer layer, bool scrollTree = false)
        {
            _selectedLayer = layer;
            _canvasShowSelection = layer != null;
            RefreshView();
            if (scrollTree)
                ScrollTreeToLayer(layer);
        }

        private void RebuildInspector()
        {
            _inspectorScroll?.Clear();
            if (_inspectorScroll == null)
                return;

            _previewImage = null;
            _previewDetailsLabel = null;

            if (_selectedLayer == null)
            {
                _inspectorScroll.Add(new HelpBox("Select a layer to edit export settings.", HelpBoxMessageType.Info));
                AddExportSettingsSection();
                return;
            }

            PsdUiToolkitLayerConfig config = GetOrCreateSelectedLayerConfig();
            _inspectorScroll.Add(new Label("Selected Layer") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            _previewImage = new Image
            {
                image = _selectedLayerPreview,
                scaleMode = ScaleMode.ScaleToFit,
            };
            _previewImage.style.height = 132f;
            _previewImage.style.marginTop = 6f;
            _previewImage.style.marginBottom = 4f;
            _previewImage.style.backgroundColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            _inspectorScroll.Add(_previewImage);

            _previewDetailsLabel = new Label(GetPreviewDetailsText());
            _previewDetailsLabel.style.whiteSpace = WhiteSpace.Normal;
            _previewDetailsLabel.style.marginBottom = 6f;
            _inspectorScroll.Add(_previewDetailsLabel);

            _inspectorScroll.Add(new Label($"Kind: {_selectedLayer.Kind}"));
            _inspectorScroll.Add(new Label($"Layer ID: {_selectedLayer.LayerId}"));

            PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(_selectedLayer);
            _inspectorScroll.Add(new Label($"Bounds: {bounds.Left}, {bounds.Top}, {bounds.Width}, {bounds.Height}"));

            TextField nameField = new TextField("Name") { value = config.name };
            nameField.RegisterValueChangedCallback(evt =>
            {
                config.name = evt.newValue ?? string.Empty;
                _selectedLayer.Name = config.name;
                PersistConfig();
                RebuildLayerTree();
                RebuildCanvasOverlays();
            });
            _inspectorScroll.Add(nameField);

            Toggle exportedToggle = new Toggle("Export") { value = config.exported };
            exportedToggle.RegisterValueChangedCallback(evt =>
            {
                config.exported = evt.newValue;
                PersistConfig();
                RebuildLayerTree();
            });
            _inspectorScroll.Add(exportedToggle);

            Toggle visibleToggle = new Toggle("Visible") { value = config.visible };
            visibleToggle.RegisterValueChangedCallback(evt =>
            {
                config.visible = evt.newValue;
                _selectedLayer.Visible = evt.newValue;
                PersistConfig();
                RefreshCompositePreview();
                RefreshView();
            });
            _inspectorScroll.Add(visibleToggle);

            if (_selectedLayer.IsGroup)
            {
                Toggle mergeToggle = new Toggle("Merge export") { value = config.merge };
                mergeToggle.RegisterValueChangedCallback(evt =>
                {
                    config.merge = evt.newValue;
                    PersistConfig();
                });
                _inspectorScroll.Add(mergeToggle);
            }

            if (_selectedLayer.Kind != LayerKind.Type)
            {
                Toggle customImageToggle = new Toggle("Use custom image") { value = config.useCustomImage };
                customImageToggle.RegisterValueChangedCallback(evt =>
                {
                    config.useCustomImage = evt.newValue;
                    if (!evt.newValue)
                        config.customImagePath = string.Empty;
                    PersistConfig();
                    RebuildInspector();
                });
                _inspectorScroll.Add(customImageToggle);

                if (config.useCustomImage)
                {
                    Sprite currentSprite = string.IsNullOrEmpty(config.customImagePath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Sprite>(config.customImagePath);
                    ObjectField spriteField = new ObjectField("Custom Sprite")
                    {
                        objectType = typeof(Sprite),
                        allowSceneObjects = false,
                        value = currentSprite,
                    };
                    spriteField.RegisterValueChangedCallback(evt =>
                    {
                        config.customImagePath = evt.newValue != null ? AssetDatabase.GetAssetPath(evt.newValue) : string.Empty;
                        PersistConfig();
                    });
                    _inspectorScroll.Add(spriteField);
                }
                else
                {
                    Toggle sliceToggle = new Toggle("Slice / nine-slice") { value = config.sliceImage };
                    sliceToggle.RegisterValueChangedCallback(evt =>
                    {
                        config.sliceImage = evt.newValue;
                        PersistConfig();
                        RebuildInspector();
                    });
                    _inspectorScroll.Add(sliceToggle);

                    Toggle localDedupToggle = new Toggle("Participate local dedup") { value = config.participateLocalDedup };
                    localDedupToggle.RegisterValueChangedCallback(evt =>
                    {
                        config.participateLocalDedup = evt.newValue;
                        PersistConfig();
                    });
                    _inspectorScroll.Add(localDedupToggle);

                    Toggle commonDedupToggle = new Toggle("Participate common dedup") { value = config.participateCommonDedup };
                    commonDedupToggle.RegisterValueChangedCallback(evt =>
                    {
                        config.participateCommonDedup = evt.newValue;
                        PersistConfig();
                    });
                    _inspectorScroll.Add(commonDedupToggle);

                    if (config.sliceImage)
                    {
                        Toggle customNineSliceToggle = new Toggle("Override nine-slice params") { value = config.useCustomNineSliceParams };
                        customNineSliceToggle.RegisterValueChangedCallback(evt =>
                        {
                            config.useCustomNineSliceParams = evt.newValue;
                            PersistConfig();
                            RebuildInspector();
                        });
                        _inspectorScroll.Add(customNineSliceToggle);

                        if (config.useCustomNineSliceParams)
                        {
                            IntegerField borderInsetField = new IntegerField("Border inset") { value = config.nineSliceBorderInset };
                            borderInsetField.RegisterValueChangedCallback(evt =>
                            {
                                config.nineSliceBorderInset = evt.newValue;
                                PersistConfig();
                            });
                            _inspectorScroll.Add(borderInsetField);

                            IntegerField pixelThresholdField = new IntegerField("Pixel threshold") { value = config.nineSlicePixelThreshold };
                            pixelThresholdField.RegisterValueChangedCallback(evt =>
                            {
                                config.nineSlicePixelThreshold = evt.newValue;
                                PersistConfig();
                            });
                            _inspectorScroll.Add(pixelThresholdField);

                            IntegerField minCenterColsField = new IntegerField("Min center cols") { value = config.nineSliceMinCenterCols };
                            minCenterColsField.RegisterValueChangedCallback(evt =>
                            {
                                config.nineSliceMinCenterCols = evt.newValue;
                                PersistConfig();
                            });
                            _inspectorScroll.Add(minCenterColsField);

                            IntegerField minCenterRowsField = new IntegerField("Min center rows") { value = config.nineSliceMinCenterRows };
                            minCenterRowsField.RegisterValueChangedCallback(evt =>
                            {
                                config.nineSliceMinCenterRows = evt.newValue;
                                PersistConfig();
                            });
                            _inspectorScroll.Add(minCenterRowsField);

                            IntegerField minSameZoneField = new IntegerField("Min same-zone") { value = config.nineSliceMinSameZone };
                            minSameZoneField.RegisterValueChangedCallback(evt =>
                            {
                                config.nineSliceMinSameZone = evt.newValue;
                                PersistConfig();
                            });
                            _inspectorScroll.Add(minSameZoneField);
                        }
                    }
                }
            }

            if (_selectedLayer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)_selectedLayer;
                Label textSummary = new Label($"Text: {typeLayer.Text}") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6f } };
                _inspectorScroll.Add(textSummary);
                _inspectorScroll.Add(new Label($"Font: {typeLayer.PsdFontName}"));
                _inspectorScroll.Add(new Label($"Size: {typeLayer.EffectiveFontSize:0.##}"));
            }

            AddAutoLayoutInspectorSection(config);
            AddExportSettingsSection();
        }

        private void AddExportSettingsSection()
        {
            _inspectorScroll.Add(new Label("Export Settings") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12f } });

            _imageExportRootField = new TextField("Image Root") { value = PsdUiToolkitEditorPrefs.ImageExportRoot };
            _imageExportRootField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitEditorPrefs.ImageExportRoot = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(evt.newValue);
            });
            _inspectorScroll.Add(_imageExportRootField);

            _uxmlExportRootField = new TextField("UXML Root") { value = PsdUiToolkitEditorPrefs.UxmlExportRoot };
            _uxmlExportRootField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitEditorPrefs.UxmlExportRoot = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(evt.newValue);
            });
            _inspectorScroll.Add(_uxmlExportRootField);

            _autoImageNamingToggle = new Toggle("Auto image naming") { value = PsdUiToolkitEditorPrefs.AutoImageNaming };
            _autoImageNamingToggle.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitEditorPrefs.AutoImageNaming = evt.newValue;
            });
            _inspectorScroll.Add(_autoImageNamingToggle);

            if (_psd == null || string.IsNullOrEmpty(_psdPath))
            {
                _inspectorScroll.Add(new HelpBox("Open a PSD to configure PSD-scoped auto-layout defaults.", HelpBoxMessageType.Info));
                return;
            }

            PsdUiToolkitExportConfigData data = EnsureConfigData();
            PsdUiToolkitAutoLayoutGlobalConfig autoLayout = data.autoLayout.GetValidated();

            _inspectorScroll.Add(new Label("Auto Layout (PSD)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10f } });

            Toggle enabledToggle = new Toggle("Enable auto layout") { value = autoLayout.enabled };
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.enabled = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(enabledToggle);

            EnumField modeField = new EnumField("Detection mode", autoLayout.detectionMode);
            modeField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.detectionMode = (PsdUiToolkitAutoLayoutMode)evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(modeField);

            Slider confidenceSlider = new Slider("Min confidence", 0f, 1f) { value = autoLayout.minimumConfidence };
            confidenceSlider.showInputField = true;
            confidenceSlider.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.minimumConfidence = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(confidenceSlider);

            IntegerField alignmentToleranceField = new IntegerField("Alignment tolerance") { value = autoLayout.alignmentTolerance };
            alignmentToleranceField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.alignmentTolerance = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(alignmentToleranceField);

            IntegerField gapToleranceField = new IntegerField("Gap tolerance") { value = autoLayout.gapTolerance };
            gapToleranceField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.gapTolerance = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(gapToleranceField);

            Toggle rebuildTreeToggle = new Toggle("Rebuild layout tree") { value = autoLayout.rebuildLayoutTree };
            rebuildTreeToggle.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.rebuildLayoutTree = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(rebuildTreeToggle);

            Toggle virtualContainerToggle = new Toggle("Allow virtual containers") { value = autoLayout.allowVirtualContainers };
            virtualContainerToggle.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.allowVirtualContainers = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(virtualContainerToggle);

            Toggle backgroundToggle = new Toggle("Detect background containers") { value = autoLayout.detectBackgroundContainers };
            backgroundToggle.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.detectBackgroundContainers = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(backgroundToggle);

            IntegerField nestingField = new IntegerField("Max nesting depth") { value = autoLayout.maxNestingDepth };
            nestingField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitExportConfigData current = EnsureConfigData();
                current.autoLayout.maxNestingDepth = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(nestingField);

            EnumField fallbackField = new EnumField("Fallback", autoLayout.fallbackMode);
            fallbackField.SetEnabled(false);
            _inspectorScroll.Add(fallbackField);

            _inspectorScroll.Add(new HelpBox("Auto-layout remains opt-in and falls back to absolute positioning whenever analysis is disabled or confidence is too low. Rebuild layout tree inserts a separate layout-tree pass while leaving raster export unchanged.", HelpBoxMessageType.Info));
        }

        private void AddAutoLayoutInspectorSection(PsdUiToolkitLayerConfig config)
        {
            if (config == null)
                return;

            _inspectorScroll.Add(new Label("Auto Layout") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10f } });

            Toggle participateToggle = new Toggle("Participate in auto layout") { value = config.participateInAutoLayout };
            participateToggle.RegisterValueChangedCallback(evt =>
            {
                config.participateInAutoLayout = evt.newValue;
                PersistConfigAndRebuildInspector();
            });
            _inspectorScroll.Add(participateToggle);
        }

        private PsdUiToolkitLayoutTree BuildCurrentAnalysisTree(string rootName)
        {
            return _configMap != null && _configMap.GetAutoLayoutConfig().rebuildLayoutTree
                ? PsdUiToolkitLayoutTreeRebuilder.AnalyzeForInspector(_psd, _configMap, rootName)
                : PsdUiToolkitAutoLayoutAnalyzer.AnalyzeForInspector(_psd, _configMap, rootName);
        }

        private PsdUiToolkitExportConfigData EnsureConfigData()
        {
            _configData ??= new PsdUiToolkitExportConfigData();
            _configData.autoLayout = _configData.autoLayout.GetValidated();
            _configData.layers ??= Array.Empty<PsdUiToolkitLayerConfig>();
            return _configData;
        }

        private PsdUiToolkitLayerConfig GetOrCreateSelectedLayerConfig()
        {
            if (_selectedLayer?.LayerId == null)
                return null;

            PsdUiToolkitExportConfigData data = EnsureConfigData();

            foreach (PsdUiToolkitLayerConfig entry in data.layers)
            {
                if (entry != null && entry.id == _selectedLayer.LayerId.Value)
                {
                    entry.Sanitize();
                    return entry;
                }
            }

            List<PsdUiToolkitLayerConfig> layers = new List<PsdUiToolkitLayerConfig>(data.layers);
            PsdUiToolkitLayerConfig config = PsdUiToolkitLayerConfig.CreateDefault(_selectedLayer);
            layers.Add(config);
            data.layers = layers.ToArray();
            _configMap = new PsdUiToolkitLayerConfigMap(_configData);
            return config;
        }

        private void PersistConfig()
        {
            if (_psd == null || string.IsNullOrEmpty(_psdPath) || _configData == null)
                return;

            _configData = PsdUiToolkitConfigStore.Synchronize(_psd, _configData);
            _configMap = new PsdUiToolkitLayerConfigMap(_configData);
            PsdUiToolkitConfigStore.Save(_psdPath, _configData);
        }

        private void PersistConfigAndRebuildInspector()
        {
            PersistConfig();
            RebuildLayerTree();
            RebuildInspector();
        }

        private void UpdatePreview()
        {
            DestroySelectedPreview();

            if (_psd == null || _selectedLayer == null)
            {
                if (_previewImage != null)
                    _previewImage.image = null;
                if (_previewDetailsLabel != null)
                    _previewDetailsLabel.text = "No selection.";
                return;
            }

            try
            {
                _selectedLayerPreview = PsdUiToolkitRasterExporter.CreatePreviewTexture(_psd, _selectedLayer);
                if (_selectedLayerPreview != null)
                    _selectedLayerPreview.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkit] Failed to build layer preview: {ex.Message}");
            }
            finally
            {
                _psd.ClearDecompressedCaches();
            }

            if (_previewImage != null)
                _previewImage.image = _selectedLayerPreview;
            if (_previewDetailsLabel != null)
                _previewDetailsLabel.text = GetPreviewDetailsText();
        }

        private string GetPreviewDetailsText()
        {
            if (_selectedLayer == null)
                return "No selection.";
            if (_selectedLayer.Kind == LayerKind.Type)
                return ((TypeLayer)_selectedLayer).Text;
            return _selectedLayerPreview == null ? "No raster preview available for this layer." : string.Empty;
        }

        private void DestroySelectedPreview()
        {
            if (_selectedLayerPreview != null)
            {
                Object.DestroyImmediate(_selectedLayerPreview);
                _selectedLayerPreview = null;
            }
        }

        private void RefreshCompositePreview()
        {
            DestroyCompositePreview();
            if (_psd == null)
                return;

            try
            {
                _psdCompositePreview = _psd.CompositeFromLayersOnly();
                if (_psdCompositePreview != null)
                    _psdCompositePreview.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkit] Failed to build PSD composite preview: {ex.Message}");
            }
            finally
            {
                _psd.ClearDecompressedCaches();
            }

            if (_canvasImage != null)
                _canvasImage.image = _psdCompositePreview;
            UpdateCanvasGeometry();
        }

        private void DestroyCompositePreview()
        {
            if (_psdCompositePreview == null)
                return;
            Object.DestroyImmediate(_psdCompositePreview);
            _psdCompositePreview = null;
        }

        private void UpdateCanvasGeometry()
        {
            if (_canvasViewport == null || _canvasSurface == null)
                return;

            bool hasPsd = _psd != null && _psd.Width > 0 && _psd.Height > 0;
            if (_canvasEmptyLabel != null)
                _canvasEmptyLabel.style.display = hasPsd ? DisplayStyle.None : DisplayStyle.Flex;
            _canvasSurface.style.display = hasPsd ? DisplayStyle.Flex : DisplayStyle.None;
            if (_canvasTitleLabel != null)
                _canvasTitleLabel.text = hasPsd ? $"Canvas Preview  {_psd.Width} x {_psd.Height}" : "Canvas Preview";
            if (!hasPsd)
            {
                _canvasOverlay?.Clear();
                return;
            }

            Rect viewport = _canvasViewport.contentRect;
            if (viewport.width <= 0f || viewport.height <= 0f || float.IsNaN(viewport.width) || float.IsNaN(viewport.height))
                return;

            float scale = Mathf.Min(viewport.width / _psd.Width, viewport.height / _psd.Height);
            _canvasDrawWidth = _psd.Width * scale;
            _canvasDrawHeight = _psd.Height * scale;
            _canvasSurface.style.left = (viewport.width - _canvasDrawWidth) * 0.5f;
            _canvasSurface.style.top = (viewport.height - _canvasDrawHeight) * 0.5f;
            _canvasSurface.style.width = _canvasDrawWidth;
            _canvasSurface.style.height = _canvasDrawHeight;
            if (_canvasImage != null)
                _canvasImage.image = _psdCompositePreview;

            RebuildCanvasOverlays();
        }

        private void RebuildCanvasOverlays()
        {
            _canvasOverlay?.Clear();
            if (_canvasOverlay == null || _psd == null || _canvasDrawWidth <= 0f || _canvasDrawHeight <= 0f)
                return;

            AddVisibleLayerOutlines(_psd.Root);
            if (_selectedLayer == null || !_canvasShowSelection)
                return;

            PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(_selectedLayer);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            Rect selectedRect = GetCanvasRect(bounds);
            VisualElement selection = CreateCanvasOutline(selectedRect, new Color(0.2f, 0.5f, 1f, 1f), 2f);
            selection.style.backgroundColor = new Color(0.2f, 0.5f, 1f, 0.12f);
            _canvasOverlay.Add(selection);

            Label label = new Label($"{_selectedLayer.Name} ({bounds.Width}x{bounds.Height})")
            {
                pickingMode = PickingMode.Ignore,
            };
            label.style.position = Position.Absolute;
            label.style.left = Mathf.Clamp(selectedRect.x, 0f, Mathf.Max(0f, _canvasDrawWidth - 220f));
            label.style.top = Mathf.Clamp(selectedRect.yMax + 2f, 0f, Mathf.Max(0f, _canvasDrawHeight - 18f));
            label.style.color = new Color(0.55f, 0.75f, 1f, 1f);
            label.style.backgroundColor = new Color(0.06f, 0.06f, 0.06f, 0.78f);
            label.style.paddingLeft = 3f;
            label.style.paddingRight = 3f;
            label.style.fontSize = 10f;
            _canvasOverlay.Add(label);
        }

        private void AddVisibleLayerOutlines(Layer parent)
        {
            foreach (Layer child in parent.Children)
            {
                if (!child.Visible || child.IsClipped)
                    continue;

                if (child.IsGroup)
                {
                    AddVisibleLayerOutlines(child);
                    continue;
                }

                PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(child);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;
                _canvasOverlay.Add(CreateCanvasOutline(GetCanvasRect(bounds), new Color(0.65f, 0.65f, 0.65f, 0.35f), 1f));
            }
        }

        private Rect GetCanvasRect(PsdUiToolkitLayerBounds bounds)
        {
            float scaleX = _canvasDrawWidth / _psd.Width;
            float scaleY = _canvasDrawHeight / _psd.Height;
            return new Rect(
                bounds.Left * scaleX,
                bounds.Top * scaleY,
                Mathf.Max(1f, bounds.Width * scaleX),
                Mathf.Max(1f, bounds.Height * scaleY));
        }

        private static VisualElement CreateCanvasOutline(Rect rect, Color color, float width)
        {
            VisualElement outline = new VisualElement { pickingMode = PickingMode.Ignore };
            outline.style.position = Position.Absolute;
            outline.style.left = rect.x;
            outline.style.top = rect.y;
            outline.style.width = rect.width;
            outline.style.height = rect.height;
            outline.style.borderLeftWidth = width;
            outline.style.borderRightWidth = width;
            outline.style.borderTopWidth = width;
            outline.style.borderBottomWidth = width;
            outline.style.borderLeftColor = color;
            outline.style.borderRightColor = color;
            outline.style.borderTopColor = color;
            outline.style.borderBottomColor = color;
            return outline;
        }

        private readonly struct CanvasLayerHit
        {
            public readonly Layer Layer;
            public readonly int Depth;
            public readonly float DistanceSquared;

            public CanvasLayerHit(Layer layer, int depth, float distanceSquared)
            {
                Layer = layer;
                Depth = depth;
                DistanceSquared = distanceSquared;
            }
        }

        private void OnCanvasPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _psd == null || _canvasDrawWidth <= 0f || _canvasDrawHeight <= 0f)
                return;

            Vector2 localPosition = evt.localPosition;
            Vector2 psdPosition = new Vector2(
                localPosition.x * _psd.Width / _canvasDrawWidth,
                localPosition.y * _psd.Height / _canvasDrawHeight);

            bool samePosition = (psdPosition - _lastCanvasClickPsdPosition).sqrMagnitude
                < CanvasSameClickThreshold * CanvasSameClickThreshold;
            if (samePosition && _canvasClickCandidates.Count > 0)
            {
                _canvasClickCandidateIndex = (_canvasClickCandidateIndex + 1) % _canvasClickCandidates.Count;
            }
            else
            {
                _lastCanvasClickPsdPosition = psdPosition;
                _canvasClickCandidates.Clear();
                _canvasClickCandidates.AddRange(FindLayersAtCanvasPosition(psdPosition.x, psdPosition.y));
                _canvasClickCandidateIndex = 0;
            }

            if (_canvasClickCandidates.Count == 0)
            {
                _canvasShowSelection = false;
                RebuildCanvasOverlays();
            }
            else
            {
                SelectLayer(_canvasClickCandidates[_canvasClickCandidateIndex], true);
            }

            evt.StopPropagation();
        }

        private List<Layer> FindLayersAtCanvasPosition(float psdX, float psdY)
        {
            List<CanvasLayerHit> hits = new List<CanvasLayerHit>();
            CollectCanvasLayerHits(_psd.Root, 0, psdX, psdY, hits);
            hits.Sort((left, right) =>
            {
                int depthCompare = right.Depth.CompareTo(left.Depth);
                return depthCompare != 0 ? depthCompare : left.DistanceSquared.CompareTo(right.DistanceSquared);
            });

            List<Layer> layers = new List<Layer>(hits.Count);
            foreach (CanvasLayerHit hit in hits)
                layers.Add(hit.Layer);
            return layers;
        }

        private static void CollectCanvasLayerHits(Layer parent, int depth, float psdX, float psdY, List<CanvasLayerHit> hits)
        {
            foreach (Layer child in parent.Children)
            {
                if (!child.Visible)
                    continue;

                int childDepth = depth + 1;
                PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(child);
                if (bounds.Width > 0 && bounds.Height > 0
                    && psdX >= bounds.Left && psdX < bounds.Left + bounds.Width
                    && psdY >= bounds.Top && psdY < bounds.Top + bounds.Height)
                {
                    float deltaX = psdX - (bounds.Left + bounds.Width * 0.5f);
                    float deltaY = psdY - (bounds.Top + bounds.Height * 0.5f);
                    hits.Add(new CanvasLayerHit(child, childDepth, deltaX * deltaX + deltaY * deltaY));
                }

                if (child.IsGroup)
                    CollectCanvasLayerHits(child, childDepth, psdX, psdY, hits);
            }
        }

        private void ScrollTreeToLayer(Layer layer)
        {
            if (layer == null || _layerTreeScroll == null || !_layerRows.TryGetValue(layer, out VisualElement row))
                return;

            _layerTreeScroll.schedule.Execute(() =>
            {
                if (row.panel != null)
                    _layerTreeScroll.ScrollTo(row);
            });
        }

        private void ExportCurrentPsd()
        {
            if (_psd == null || string.IsNullOrEmpty(_psdPath))
            {
                EditorUtility.DisplayDialog("PSD UI Toolkit", "Open a PSD before exporting.", "OK");
                return;
            }

            try
            {
                PersistConfig();
                string imageRoot = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(PsdUiToolkitEditorPrefs.ImageExportRoot);
                string uxmlRoot = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(PsdUiToolkitEditorPrefs.UxmlExportRoot);
                PsdUiToolkitEditorPrefs.ImageExportRoot = imageRoot;
                PsdUiToolkitEditorPrefs.UxmlExportRoot = uxmlRoot;

                PsdUiToolkitExportArtifacts artifacts = PsdUiToolkitExporter.Export(_psdPath, imageRoot, uxmlRoot, PsdUiToolkitEditorPrefs.AutoImageNaming);
                UpdateStatus($"Exported {Path.GetFileNameWithoutExtension(_psdPath)} to {artifacts.UxmlAssetPath}");

                Object exportedAsset = AssetDatabase.LoadAssetAtPath<Object>(artifacts.UxmlAssetPath);
                if (exportedAsset != null)
                {
                    Selection.activeObject = exportedAsset;
                    EditorUtility.FocusProjectWindow();
                    EditorGUIUtility.PingObject(exportedAsset);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Export failed: {ex.Message}");
                EditorUtility.DisplayDialog("PSD UI Toolkit", $"Export failed:\n\n{ex.Message}", "OK");
            }
        }

        private void UpdateStatus(string message)
        {
            if (_statusLabel != null)
                _statusLabel.text = message;
        }

        private void OnDisable()
        {
            ReleaseCurrentPsd();
        }
    }
}

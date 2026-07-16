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
        private PsdUiToolkitVirtualGroupConfig _selectedVirtualGroup;
        private readonly HashSet<Layer> _selectedLayers = new HashSet<Layer>();
        private List<PsdUiToolkitLayoutSuggestion> _layoutSuggestions = new List<PsdUiToolkitLayoutSuggestion>();
        private PsdUiToolkitLayoutTree _currentLayoutTree;
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
            ToolbarButton exportButton = new ToolbarButton(ExportCurrentPsd) { text = "Update Generated Draft" };
            ToolbarButton editableButton = new ToolbarButton(() => CreateOrOpenEditable(false)) { text = "Create / Open Editable" };
            ToolbarButton recreateButton = new ToolbarButton(() => CreateOrOpenEditable(true)) { text = "Recreate Editable" };

            toolbar.Add(openButton);
            toolbar.Add(reloadButton);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(exportButton);
            toolbar.Add(editableButton);
            toolbar.Add(recreateButton);

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
                _selectedLayers.Clear();
                if (_selectedLayer != null)
                    _selectedLayers.Add(_selectedLayer);
                _selectedVirtualGroup = null;
                _canvasShowSelection = _selectedLayer != null;
                _canvasClickCandidates.Clear();
                RefreshLayoutSuggestions();
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
            _selectedLayers.Clear();
            _selectedVirtualGroup = null;
            _layoutSuggestions.Clear();
            _currentLayoutTree = null;
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

            try
            {
                string rootName = string.IsNullOrEmpty(_psdPath) ? "PSD" : Path.GetFileNameWithoutExtension(_psdPath);
                _currentLayoutTree = BuildCurrentAnalysisTree(rootName);
                for (int i = 0; i < _currentLayoutTree.Children.Count; i++)
                    AddLayoutNodeRow(_currentLayoutTree.Children[i], 0);
                for (int i = 0; i < _currentLayoutTree.Warnings.Count; i++)
                    _layerTreeScroll.Add(new HelpBox(_currentLayoutTree.Warnings[i], HelpBoxMessageType.Warning));
            }
            catch (Exception ex)
            {
                _layerTreeScroll.Add(new HelpBox($"Layout tree failed: {ex.Message}", HelpBoxMessageType.Error));
            }
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

            bool isSelected = node.SourceLayer != null
                ? _selectedLayers.Contains(node.SourceLayer)
                : _selectedVirtualGroup != null && _selectedVirtualGroup.id == node.VirtualGroupId;
            bool containsSelection = node.IsSynthetic && NodeContainsAnySelectedLayer(node);
            Button row = new Button();
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (node.SourceLayer != null)
                    SelectLayer(node.SourceLayer, false, evt.ctrlKey || evt.commandKey);
                else if (!string.IsNullOrEmpty(node.VirtualGroupId))
                    SelectVirtualGroup(node.VirtualGroupId);
                evt.StopPropagation();
            });
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
            string prefix = node.IsSynthetic ? "[Layout] " : string.Empty;
            string exportMarker = node.SourceLayer != null && _configMap != null && !_configMap.IsExported(node.SourceLayer) ? "[Off] " : string.Empty;
            string visibilityMarker = node.SourceLayer != null && !node.SourceLayer.Visible ? "[Hidden] " : string.Empty;
            string kindLabel = node.IsSynthetic
                ? node.LayoutType.ToString()
                : (node.SourceLayer == null
                    ? "Layout"
                    : (node.SourceLayer.Kind == LayerKind.Group
                        ? (node.LayoutType == PsdUiToolkitLayoutType.Row || node.LayoutType == PsdUiToolkitLayoutType.Column
                            ? $"Group/{node.LayoutType}"
                            : "Group")
                        : node.SourceLayer.Kind.ToString()));
            PsdUiToolkitLayoutSuggestion suggestion = node.SourceLayer?.LayerId == null
                ? null
                : FindLayerSuggestion(node.SourceLayer.LayerId.Value);
            string suggestionMarker = suggestion == null ? string.Empty : $" [Suggested {suggestion.Layout}]";
            row.text = $"{exportMarker}{visibilityMarker}{new string(' ', depth * 2)}{prefix}{nodeName} ({kindLabel}){suggestionMarker}";
            row.tooltip = string.IsNullOrEmpty(node.RebuildReason)
                ? node.AnalysisSummary
                : $"{node.RebuildReason}\n{node.AnalysisSummary}";
            if (node.SourceLayer == null && string.IsNullOrEmpty(node.VirtualGroupId))
                row.SetEnabled(false);

            _layerTreeScroll.Add(row);
            if (node.SourceLayer != null && !_layerRows.ContainsKey(node.SourceLayer))
                _layerRows.Add(node.SourceLayer, row);

            for (int i = 0; i < node.Children.Count; i++)
                AddLayoutNodeRow(node.Children[i], depth + 1);
        }

        private bool NodeContainsAnySelectedLayer(PsdUiToolkitLayoutNode node)
        {
            if (node == null || _selectedLayers.Count == 0)
                return false;
            if (node.SourceLayer != null && _selectedLayers.Contains(node.SourceLayer))
                return true;

            for (int i = 0; i < node.Children.Count; i++)
            {
                if (NodeContainsAnySelectedLayer(node.Children[i]))
                    return true;
            }

            return false;
        }

        private void SelectLayer(Layer layer, bool scrollTree = false, bool additive = false)
        {
            _selectedVirtualGroup = null;
            if (!additive)
                _selectedLayers.Clear();
            if (layer != null)
            {
                if (additive && _selectedLayers.Contains(layer))
                    _selectedLayers.Remove(layer);
                else
                    _selectedLayers.Add(layer);
            }
            _selectedLayer = layer;
            if (_selectedLayers.Count == 0)
            {
                _selectedLayer = null;
            }
            else if (!_selectedLayers.Contains(_selectedLayer))
            {
                foreach (Layer selected in _selectedLayers)
                {
                    _selectedLayer = selected;
                    break;
                }
            }
            _canvasShowSelection = _selectedLayer != null;
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

            if (_selectedVirtualGroup != null)
            {
                AddVirtualGroupInspector(_selectedVirtualGroup);
                AddExportSettingsSection();
                return;
            }

            if (_selectedLayers.Count > 1)
            {
                AddMultiSelectionInspector();
                AddExportSettingsSection();
                return;
            }

            if (_selectedLayer == null)
            {
                _inspectorScroll.Add(new HelpBox("Select a layer to edit export settings.", HelpBoxMessageType.Info));
                AddSuggestionInspectorSection();
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
                RefreshLayoutSuggestions();
                RebuildLayerTree();
            });
            _inspectorScroll.Add(exportedToggle);

            Toggle visibleToggle = new Toggle("Visible") { value = config.visible };
            visibleToggle.RegisterValueChangedCallback(evt =>
            {
                config.visible = evt.newValue;
                _selectedLayer.Visible = evt.newValue;
                PersistConfig();
                RefreshLayoutSuggestions();
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
                    PersistLayoutConfigAndRefreshSuggestions();
                });
                _inspectorScroll.Add(mergeToggle);
            }

            if (_selectedLayer.Kind != LayerKind.Type)
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

            if (_selectedLayer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)_selectedLayer;
                Label textSummary = new Label($"Text: {typeLayer.Text}") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6f } };
                _inspectorScroll.Add(textSummary);
                _inspectorScroll.Add(new Label($"Font: {typeLayer.PsdFontName}"));
                _inspectorScroll.Add(new Label($"Size: {typeLayer.EffectiveFontSize:0.##}"));
            }

            AddManualLayoutInspectorSection(config);
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
                _inspectorScroll.Add(new HelpBox("Open a PSD to configure layout intent.", HelpBoxMessageType.Info));
                return;
            }

            _inspectorScroll.Add(new HelpBox(
                "Layout detection only provides suggestions. Export uses Absolute unless you explicitly choose Row or Column.",
                HelpBoxMessageType.Info));
            AddSuggestionInspectorSection();
            AddConfiguredGroupsSection();
        }

        private void AddManualLayoutInspectorSection(PsdUiToolkitLayerConfig config)
        {
            if (config == null)
                return;

            _inspectorScroll.Add(new Label("Layout Intent") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10f } });

            EnumField itemRoleField = new EnumField("In parent", config.itemRole);
            itemRoleField.RegisterValueChangedCallback(evt =>
            {
                config.itemRole = (PsdUiToolkitItemRole)evt.newValue;
                PersistLayoutConfigAndRefreshSuggestions();
            });
            _inspectorScroll.Add(itemRoleField);

            EnumField childrenLayoutField = new EnumField("Arrange children", config.childrenLayout);
            childrenLayoutField.SetEnabled(_selectedLayer != null && _selectedLayer.IsGroup && !config.merge);
            childrenLayoutField.RegisterValueChangedCallback(evt =>
            {
                config.childrenLayout = (PsdUiToolkitContainerLayout)evt.newValue;
                PersistLayoutConfigAndRefreshSuggestions();
            });
            _inspectorScroll.Add(childrenLayoutField);

            PsdUiToolkitLayoutSuggestion suggestion = _selectedLayer?.LayerId == null
                ? null
                : FindLayerSuggestion(_selectedLayer.LayerId.Value);
            if (suggestion != null)
            {
                Button applySuggestion = new Button(() => ApplyLayerSuggestion(suggestion))
                {
                    text = $"Apply suggested {suggestion.Layout}",
                    tooltip = suggestion.Summary,
                };
                _inspectorScroll.Add(applySuggestion);
            }
        }

        private void AddMultiSelectionInspector()
        {
            _inspectorScroll.Add(new Label("Multiple Selection") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _inspectorScroll.Add(new Label($"{_selectedLayers.Count} layers selected."));
            _inspectorScroll.Add(new HelpBox(
                "Only sibling layers can be wrapped in a layout group. Hold Ctrl or Cmd while clicking tree or canvas nodes.",
                HelpBoxMessageType.Info));

            Button createRow = new Button(() => CreateVirtualGroupFromLayers(_selectedLayers, PsdUiToolkitContainerLayout.Row))
            {
                text = "Create Row Group",
            };
            Button createColumn = new Button(() => CreateVirtualGroupFromLayers(_selectedLayers, PsdUiToolkitContainerLayout.Column))
            {
                text = "Create Column Group",
            };
            _inspectorScroll.Add(createRow);
            _inspectorScroll.Add(createColumn);
        }

        private void AddVirtualGroupInspector(PsdUiToolkitVirtualGroupConfig group)
        {
            _inspectorScroll.Add(new Label("Virtual Layout Group") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            TextField nameField = new TextField("Name") { value = group.name };
            nameField.RegisterValueChangedCallback(evt =>
            {
                group.name = evt.newValue ?? string.Empty;
                PersistConfig();
                RebuildLayerTree();
            });
            _inspectorScroll.Add(nameField);
            _inspectorScroll.Add(new Label($"Members: {group.memberLayerIds.Length}"));
            _inspectorScroll.Add(new Label($"Current layout: {group.layout}"));

            VisualElement layoutButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Button rowButton = new Button(() => SetVirtualGroupLayout(group, PsdUiToolkitContainerLayout.Row))
            {
                text = "Use Row",
            };
            Button columnButton = new Button(() => SetVirtualGroupLayout(group, PsdUiToolkitContainerLayout.Column))
            {
                text = "Use Column",
            };
            rowButton.style.flexGrow = 1f;
            columnButton.style.flexGrow = 1f;
            layoutButtons.Add(rowButton);
            layoutButtons.Add(columnButton);
            _inspectorScroll.Add(layoutButtons);

            Button dissolveButton = new Button(() => DissolveVirtualGroup(group))
            {
                text = "Dissolve Group",
            };
            dissolveButton.style.marginTop = 8f;
            _inspectorScroll.Add(dissolveButton);
        }

        private void AddSuggestionInspectorSection()
        {
            List<PsdUiToolkitLayoutSuggestion> virtualSuggestions = new List<PsdUiToolkitLayoutSuggestion>();
            for (int i = 0; i < _layoutSuggestions.Count; i++)
            {
                if (_layoutSuggestions[i].IsVirtualGroup)
                    virtualSuggestions.Add(_layoutSuggestions[i]);
            }

            if (virtualSuggestions.Count == 0)
                return;

            _inspectorScroll.Add(new Label("Layout Suggestions") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10f } });
            for (int i = 0; i < virtualSuggestions.Count; i++)
            {
                PsdUiToolkitLayoutSuggestion suggestion = virtualSuggestions[i];
                Button applyButton = new Button(() => ApplyVirtualSuggestion(suggestion))
                {
                    text = $"Create suggested {suggestion.Layout} group ({suggestion.MemberLayerIds.Length} items)",
                    tooltip = suggestion.Summary,
                };
                _inspectorScroll.Add(applyButton);
            }
        }

        private void AddConfiguredGroupsSection()
        {
            PsdUiToolkitVirtualGroupConfig[] groups = EnsureConfigData().virtualGroups;
            if (groups.Length == 0)
                return;

            _inspectorScroll.Add(new Label("Configured Layout Groups") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10f } });
            for (int i = 0; i < groups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[i];
                if (group == null)
                    continue;
                Button selectButton = new Button(() => SelectVirtualGroup(group.id))
                {
                    text = $"{group.name} ({group.layout}, {group.memberLayerIds.Length} items)",
                };
                _inspectorScroll.Add(selectButton);
            }
        }

        private void RefreshLayoutSuggestions()
        {
            _layoutSuggestions.Clear();
            if (_psd == null || _configData == null)
                return;

            try
            {
                string rootName = string.IsNullOrEmpty(_psdPath) ? "PSD" : Path.GetFileNameWithoutExtension(_psdPath);
                _layoutSuggestions = PsdUiToolkitLayoutSuggestionService.Analyze(_psd, _configData, rootName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PsdUiToolkit] Layout suggestion analysis failed: {ex.Message}");
            }
        }

        private PsdUiToolkitLayoutSuggestion FindLayerSuggestion(int layerId)
        {
            for (int i = 0; i < _layoutSuggestions.Count; i++)
            {
                if (_layoutSuggestions[i].TargetLayerId == layerId)
                    return _layoutSuggestions[i];
            }

            return null;
        }

        private void ApplyLayerSuggestion(PsdUiToolkitLayoutSuggestion suggestion)
        {
            Layer layer = FindLayerById(suggestion?.TargetLayerId ?? -1);
            if (layer == null)
                return;

            PsdUiToolkitLayerConfig config = GetOrCreateLayerConfig(layer);
            config.childrenLayout = suggestion.Layout;
            PersistLayoutConfigAndRefreshSuggestions();
        }

        private void ApplyVirtualSuggestion(PsdUiToolkitLayoutSuggestion suggestion)
        {
            if (suggestion == null)
                return;

            List<Layer> layers = new List<Layer>();
            for (int i = 0; i < suggestion.MemberLayerIds.Length; i++)
            {
                Layer layer = FindLayerById(suggestion.MemberLayerIds[i]);
                if (layer == null)
                    return;
                layers.Add(layer);
            }

            CreateVirtualGroupFromLayers(layers, suggestion.Layout);
        }

        private void CreateVirtualGroupFromLayers(
            IEnumerable<Layer> selectedLayers,
            PsdUiToolkitContainerLayout layout)
        {
            List<Layer> layers = new List<Layer>();
            foreach (Layer layer in selectedLayers)
            {
                if (layer?.LayerId != null)
                    layers.Add(layer);
            }

            if (layers.Count < 2)
            {
                EditorUtility.DisplayDialog("Layout Group", "Select at least two sibling layers.", "OK");
                return;
            }

            if (!TryGetParentLayerId(layers[0], out int parentLayerId))
            {
                EditorUtility.DisplayDialog("Layout Group", "Could not resolve the selected layer parent.", "OK");
                return;
            }

            for (int i = 1; i < layers.Count; i++)
            {
                if (!TryGetParentLayerId(layers[i], out int currentParentId) || currentParentId != parentLayerId)
                {
                    EditorUtility.DisplayDialog("Layout Group", "All selected layers must share the same direct parent.", "OK");
                    return;
                }
            }

            PsdUiToolkitExportConfigData data = EnsureConfigData();
            for (int i = 0; i < data.virtualGroups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig existingGroup = data.virtualGroups[i];
                if (existingGroup == null)
                    continue;
                for (int j = 0; j < existingGroup.memberLayerIds.Length; j++)
                {
                    for (int k = 0; k < layers.Count; k++)
                    {
                        if (existingGroup.memberLayerIds[j] == layers[k].LayerId.Value)
                        {
                            EditorUtility.DisplayDialog(
                                "Layout Group",
                                $"'{layers[k].Name}' already belongs to layout group '{existingGroup.name}'.",
                                "OK");
                            return;
                        }
                    }
                }
            }

            layers.Sort(layout == PsdUiToolkitContainerLayout.Column
                ? (Comparison<Layer>)CompareLayersByTopThenLeft
                : CompareLayersByLeftThenTop);
            int[] memberIds = new int[layers.Count];
            for (int i = 0; i < layers.Count; i++)
                memberIds[i] = layers[i].LayerId.Value;

            PsdUiToolkitVirtualGroupConfig group = new PsdUiToolkitVirtualGroupConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = $"{layout} Group {data.virtualGroups.Length + 1}",
                parentLayerId = parentLayerId,
                memberLayerIds = memberIds,
                layout = layout == PsdUiToolkitContainerLayout.Column
                    ? PsdUiToolkitContainerLayout.Column
                    : PsdUiToolkitContainerLayout.Row,
            };

            List<PsdUiToolkitVirtualGroupConfig> groups = new List<PsdUiToolkitVirtualGroupConfig>(data.virtualGroups)
            {
                group,
            };
            data.virtualGroups = groups.ToArray();
            _selectedLayers.Clear();
            _selectedLayer = null;
            _selectedVirtualGroup = group;
            PersistLayoutConfigAndRefreshSuggestions();
        }

        private void SetVirtualGroupLayout(
            PsdUiToolkitVirtualGroupConfig group,
            PsdUiToolkitContainerLayout layout)
        {
            group.layout = layout == PsdUiToolkitContainerLayout.Column
                ? PsdUiToolkitContainerLayout.Column
                : PsdUiToolkitContainerLayout.Row;
            PersistLayoutConfigAndRefreshSuggestions();
        }

        private void DissolveVirtualGroup(PsdUiToolkitVirtualGroupConfig group)
        {
            PsdUiToolkitExportConfigData data = EnsureConfigData();
            List<PsdUiToolkitVirtualGroupConfig> groups = new List<PsdUiToolkitVirtualGroupConfig>();
            for (int i = 0; i < data.virtualGroups.Length; i++)
            {
                if (data.virtualGroups[i] != group && data.virtualGroups[i]?.id != group.id)
                    groups.Add(data.virtualGroups[i]);
            }

            data.virtualGroups = groups.ToArray();
            _selectedVirtualGroup = null;
            _selectedLayers.Clear();
            _selectedLayer = group.memberLayerIds.Length == 0 ? null : FindLayerById(group.memberLayerIds[0]);
            if (_selectedLayer != null)
                _selectedLayers.Add(_selectedLayer);
            PersistLayoutConfigAndRefreshSuggestions();
        }

        private void SelectVirtualGroup(string groupId)
        {
            _selectedVirtualGroup = FindVirtualGroup(groupId);
            if (_selectedVirtualGroup == null)
                return;

            _selectedLayer = null;
            _selectedLayers.Clear();
            _canvasShowSelection = false;
            RefreshView();
        }

        private PsdUiToolkitVirtualGroupConfig FindVirtualGroup(string groupId)
        {
            PsdUiToolkitVirtualGroupConfig[] groups = EnsureConfigData().virtualGroups;
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null && groups[i].id == groupId)
                    return groups[i];
            }

            return null;
        }

        private void PersistLayoutConfigAndRefreshSuggestions()
        {
            PersistConfig();
            RefreshLayoutSuggestions();
            RefreshView();
        }

        private PsdUiToolkitLayoutTree BuildCurrentAnalysisTree(string rootName)
        {
            return PsdUiToolkitManualLayoutBuilder.BuildForInspector(_psd, _configMap, rootName);
        }

        private PsdUiToolkitExportConfigData EnsureConfigData()
        {
            _configData ??= new PsdUiToolkitExportConfigData();
            _configData = PsdUiToolkitConfigStore.MigrateToCurrentVersion(_configData);
            _configData.autoLayout = _configData.autoLayout.GetValidated();
            _configData.layers ??= Array.Empty<PsdUiToolkitLayerConfig>();
            _configData.virtualGroups ??= Array.Empty<PsdUiToolkitVirtualGroupConfig>();
            return _configData;
        }

        private PsdUiToolkitLayerConfig GetOrCreateSelectedLayerConfig()
        {
            return GetOrCreateLayerConfig(_selectedLayer);
        }

        private PsdUiToolkitLayerConfig GetOrCreateLayerConfig(Layer layer)
        {
            if (layer?.LayerId == null)
                return null;

            PsdUiToolkitExportConfigData data = EnsureConfigData();

            foreach (PsdUiToolkitLayerConfig entry in data.layers)
            {
                if (entry != null && entry.id == layer.LayerId.Value)
                {
                    entry.Sanitize();
                    return entry;
                }
            }

            List<PsdUiToolkitLayerConfig> layers = new List<PsdUiToolkitLayerConfig>(data.layers);
            PsdUiToolkitLayerConfig config = PsdUiToolkitLayerConfig.CreateDefault(layer);
            layers.Add(config);
            data.layers = layers.ToArray();
            _configMap = new PsdUiToolkitLayerConfigMap(_configData);
            return config;
        }

        private Layer FindLayerById(int layerId)
        {
            if (_psd == null)
                return null;
            return FindLayerByIdRecursive(_psd.Root, layerId);
        }

        private static Layer FindLayerByIdRecursive(Layer parent, int layerId)
        {
            if (parent == null)
                return null;
            foreach (Layer child in parent.Children)
            {
                if (child.LayerId == layerId)
                    return child;
                Layer nested = FindLayerByIdRecursive(child, layerId);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private bool TryGetParentLayerId(Layer layer, out int parentLayerId)
        {
            parentLayerId = -1;
            if (_psd == null || layer == null)
                return false;
            return TryFindParentLayerId(_psd.Root, -1, layer, out parentLayerId);
        }

        private static bool TryFindParentLayerId(
            Layer parent,
            int parentId,
            Layer target,
            out int resolvedParentId)
        {
            foreach (Layer child in parent.Children)
            {
                if (child == target)
                {
                    resolvedParentId = parentId;
                    return true;
                }

                int childId = child.LayerId ?? -1;
                if (TryFindParentLayerId(child, childId, target, out resolvedParentId))
                    return true;
            }

            resolvedParentId = -1;
            return false;
        }

        private static int CompareLayersByLeftThenTop(Layer left, Layer right)
        {
            PsdUiToolkitLayerBounds leftBounds = PsdUiToolkitRasterExporter.GetLayerBounds(left);
            PsdUiToolkitLayerBounds rightBounds = PsdUiToolkitRasterExporter.GetLayerBounds(right);
            int compare = leftBounds.Left.CompareTo(rightBounds.Left);
            return compare != 0 ? compare : leftBounds.Top.CompareTo(rightBounds.Top);
        }

        private static int CompareLayersByTopThenLeft(Layer left, Layer right)
        {
            PsdUiToolkitLayerBounds leftBounds = PsdUiToolkitRasterExporter.GetLayerBounds(left);
            PsdUiToolkitLayerBounds rightBounds = PsdUiToolkitRasterExporter.GetLayerBounds(right);
            int compare = leftBounds.Top.CompareTo(rightBounds.Top);
            return compare != 0 ? compare : leftBounds.Left.CompareTo(rightBounds.Left);
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
            if (_selectedVirtualGroup != null
                && TryFindVirtualLayoutNode(_currentLayoutTree?.Children, _selectedVirtualGroup.id, out PsdUiToolkitLayoutNode virtualNode))
            {
                Rect groupRect = GetCanvasRect(virtualNode.Bounds);
                VisualElement groupSelection = CreateCanvasOutline(groupRect, new Color(0.3f, 0.85f, 0.55f, 1f), 2f);
                groupSelection.style.backgroundColor = new Color(0.3f, 0.85f, 0.55f, 0.12f);
                _canvasOverlay.Add(groupSelection);
                return;
            }

            if (_selectedLayers.Count == 0 || !_canvasShowSelection)
                return;

            Rect primaryRect = default;
            PsdUiToolkitLayerBounds primaryBounds = default;
            foreach (Layer selectedLayer in _selectedLayers)
            {
                PsdUiToolkitLayerBounds bounds = PsdUiToolkitRasterExporter.GetLayerBounds(selectedLayer);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;

                bool isPrimary = selectedLayer == _selectedLayer;
                Rect selectedRect = GetCanvasRect(bounds);
                Color color = isPrimary
                    ? new Color(0.2f, 0.5f, 1f, 1f)
                    : new Color(1f, 0.72f, 0.2f, 1f);
                VisualElement selection = CreateCanvasOutline(selectedRect, color, 2f);
                selection.style.backgroundColor = new Color(color.r, color.g, color.b, 0.12f);
                _canvasOverlay.Add(selection);
                if (isPrimary)
                {
                    primaryRect = selectedRect;
                    primaryBounds = bounds;
                }
            }

            if (_selectedLayer == null)
                return;

            Label label = new Label($"{_selectedLayer.Name} ({primaryBounds.Width}x{primaryBounds.Height})")
            {
                pickingMode = PickingMode.Ignore,
            };
            label.style.position = Position.Absolute;
            label.style.left = Mathf.Clamp(primaryRect.x, 0f, Mathf.Max(0f, _canvasDrawWidth - 220f));
            label.style.top = Mathf.Clamp(primaryRect.yMax + 2f, 0f, Mathf.Max(0f, _canvasDrawHeight - 18f));
            label.style.color = new Color(0.55f, 0.75f, 1f, 1f);
            label.style.backgroundColor = new Color(0.06f, 0.06f, 0.06f, 0.78f);
            label.style.paddingLeft = 3f;
            label.style.paddingRight = 3f;
            label.style.fontSize = 10f;
            _canvasOverlay.Add(label);
        }

        private static bool TryFindVirtualLayoutNode(
            List<PsdUiToolkitLayoutNode> nodes,
            string virtualGroupId,
            out PsdUiToolkitLayoutNode result)
        {
            result = null;
            if (nodes == null || string.IsNullOrEmpty(virtualGroupId))
                return false;

            for (int i = 0; i < nodes.Count; i++)
            {
                PsdUiToolkitLayoutNode node = nodes[i];
                if (node.VirtualGroupId == virtualGroupId)
                {
                    result = node;
                    return true;
                }

                if (TryFindVirtualLayoutNode(node.Children, virtualGroupId, out result))
                    return true;
            }

            return false;
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
                SelectLayer(_canvasClickCandidates[_canvasClickCandidateIndex], true, evt.ctrlKey || evt.commandKey);
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
                UpdateStatus($"Updated generated draft: {artifacts.GeneratedUxmlAssetPath}");

                Object exportedAsset = AssetDatabase.LoadAssetAtPath<Object>(artifacts.GeneratedUxmlAssetPath);
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

        private void CreateOrOpenEditable(bool recreate)
        {
            if (_psd == null || string.IsNullOrEmpty(_psdPath))
            {
                EditorUtility.DisplayDialog("PSD UI Toolkit", "Open a PSD before creating an editable UXML.", "OK");
                return;
            }

            GetCurrentUxmlPaths(out string generatedPath, out string editablePath);
            Object existingEditable = AssetDatabase.LoadAssetAtPath<Object>(editablePath);
            if (existingEditable != null && !recreate)
            {
                Selection.activeObject = existingEditable;
                EditorUtility.FocusProjectWindow();
                EditorGUIUtility.PingObject(existingEditable);
                AssetDatabase.OpenAsset(existingEditable);
                UpdateStatus($"Opened editable UXML: {editablePath}");
                return;
            }

            if (recreate && existingEditable != null
                && !EditorUtility.DisplayDialog(
                    "Recreate Editable UXML",
                    $"Replace '{editablePath}' with the current generated draft? UI Builder changes in this file will be lost.",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            try
            {
                string resultPath = PsdUiToolkitExporter.CreateEditableCopy(generatedPath, editablePath, recreate);
                Object editableAsset = AssetDatabase.LoadAssetAtPath<Object>(resultPath);
                if (editableAsset != null)
                {
                    Selection.activeObject = editableAsset;
                    EditorUtility.FocusProjectWindow();
                    EditorGUIUtility.PingObject(editableAsset);
                    AssetDatabase.OpenAsset(editableAsset);
                }

                UpdateStatus(existingEditable == null
                    ? $"Created editable UXML: {resultPath}"
                    : $"Recreated editable UXML: {resultPath}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Editable UXML failed: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "PSD UI Toolkit",
                    $"Could not create the editable UXML.\n\n{ex.Message}",
                    "OK");
            }
        }

        private void GetCurrentUxmlPaths(out string generatedPath, out string editablePath)
        {
            string root = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(PsdUiToolkitEditorPrefs.UxmlExportRoot);
            string psdName = Path.GetFileNameWithoutExtension(_psdPath);
            generatedPath = PsdUiToolkitAssetPathUtility.CombineAssetsPath(root, psdName + ".generated.uxml");
            editablePath = PsdUiToolkitAssetPathUtility.CombineAssetsPath(root, psdName + ".uxml");
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

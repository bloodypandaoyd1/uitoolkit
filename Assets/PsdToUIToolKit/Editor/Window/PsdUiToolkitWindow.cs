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
        private enum CanvasPreviewMode
        {
            Psd,
            Layout,
            Split,
        }

        private PsdImage _psd;
        private string _psdPath;
        private PsdUiToolkitExportConfigData _configData;
        private PsdUiToolkitLayerConfigMap _configMap;
        private Layer _selectedLayer;
        private PsdUiToolkitVirtualGroupConfig _selectedVirtualGroup;
        private readonly HashSet<Layer> _selectedLayers = new HashSet<Layer>();
        private PsdUiToolkitLayoutTree _currentLayoutTree;
        private readonly PsdUiToolkitLayoutEditHistory _layoutHistory =
            new PsdUiToolkitLayoutEditHistory();
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
        private VisualElement _psdCanvasViewport;
        private VisualElement _layoutCanvasViewport;
        private VisualElement _canvasSurface;
        private VisualElement _canvasOverlay;
        private Image _canvasImage;
        private VisualElement _layoutCanvasSurface;
        private VisualElement _layoutCanvasRoot;
        private readonly Dictionary<int, Texture2D> _layoutPreviewTextures =
            new Dictionary<int, Texture2D>();
        private readonly Dictionary<PsdUiToolkitNodeReference, Vector2>
            _layoutPreviewSizeOverrides =
                new Dictionary<PsdUiToolkitNodeReference, Vector2>();
        private CanvasPreviewMode _canvasPreviewMode = CanvasPreviewMode.Psd;
        private ToolbarButton _psdPreviewButton;
        private ToolbarButton _layoutPreviewButton;
        private ToolbarButton _splitPreviewButton;
        private TextField _imageExportRootField;
        private TextField _uxmlExportRootField;
        private Toggle _autoImageNamingToggle;
        private ToolbarButton _undoButton;
        private ToolbarButton _redoButton;
        private PsdUiToolkitButtonVisualState _previewButtonState =
            PsdUiToolkitButtonVisualState.Normal;
        private PsdUiToolkitNodeReference _dragCandidateReference;
        private Vector2 _dragCandidateStart;
        private bool _dragCandidateArmed;

        private Vector2 _lastCanvasClickPsdPosition = new Vector2(-99999f, -99999f);
        private readonly List<Layer> _canvasClickCandidates = new List<Layer>();
        private int _canvasClickCandidateIndex;
        private bool _canvasShowSelection = true;
        private float _canvasDrawWidth;
        private float _canvasDrawHeight;
        private float _layoutCanvasDrawWidth;
        private float _layoutCanvasDrawHeight;

        private const float CanvasSameClickThreshold = 5f;
        private static readonly string[] ItemRoleChoices =
        {
            "Follow parent layout",
            "Keep original position",
            "Use as background",
        };
        private static readonly string[] ContainerLayoutChoices =
        {
            "Keep absolute",
            "Row",
            "Column",
        };
        private static readonly string[] MainAxisChoices =
        {
            "Preserve PSD spacing",
            "Start",
            "Center",
            "End",
            "Space between",
            "Space around",
        };
        private static readonly string[] CrossAxisChoices =
        {
            "Preserve PSD offset",
            "Start",
            "Center",
            "End",
        };
        private static readonly string[] WrapChoices =
        {
            "No wrap",
            "Wrap",
        };
        private static readonly string[] MultiLineChoices =
        {
            "Preserve PSD lines",
            "Start",
            "Center",
            "End",
        };

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
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnRootKeyDown);

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
            _undoButton = new ToolbarButton(UndoLayoutEdit) { text = "Undo" };
            _redoButton = new ToolbarButton(RedoLayoutEdit) { text = "Redo" };
            ToolbarButton exportButton = new ToolbarButton(ExportCurrentPsd) { text = "Update Generated Draft" };
            ToolbarButton editableButton = new ToolbarButton(() => CreateOrOpenEditable(false)) { text = "Create / Open Editable" };
            ToolbarButton recreateButton = new ToolbarButton(() => CreateOrOpenEditable(true)) { text = "Recreate Editable" };

            toolbar.Add(openButton);
            toolbar.Add(reloadButton);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(_undoButton);
            toolbar.Add(_redoButton);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(exportButton);
            toolbar.Add(editableButton);
            toolbar.Add(recreateButton);
            UpdateHistoryButtons();

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

            Toolbar previewToolbar = new Toolbar();
            _psdPreviewButton = new ToolbarButton(() => SetCanvasPreviewMode(CanvasPreviewMode.Psd))
            {
                text = "PSD",
            };
            _layoutPreviewButton = new ToolbarButton(() => SetCanvasPreviewMode(CanvasPreviewMode.Layout))
            {
                text = "Layout",
            };
            _splitPreviewButton = new ToolbarButton(() => SetCanvasPreviewMode(CanvasPreviewMode.Split))
            {
                text = "Split",
            };
            previewToolbar.Add(_psdPreviewButton);
            previewToolbar.Add(_layoutPreviewButton);
            previewToolbar.Add(_splitPreviewButton);
            previewToolbar.style.marginTop = 4f;
            centerPanel.Add(previewToolbar);

            _canvasViewport = new VisualElement();
            _canvasViewport.style.flexGrow = 1f;
            _canvasViewport.style.marginTop = 6f;
            _canvasViewport.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
            _canvasViewport.style.overflow = Overflow.Hidden;
            _canvasViewport.style.flexDirection = FlexDirection.Row;
            _canvasViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateCanvasGeometry());
            centerPanel.Add(_canvasViewport);

            _psdCanvasViewport = CreatePreviewViewport();
            _psdCanvasViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateCanvasGeometry());
            _canvasViewport.Add(_psdCanvasViewport);

            _canvasSurface = new VisualElement();
            _canvasSurface.style.position = Position.Absolute;
            _canvasSurface.style.backgroundColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            _canvasSurface.style.overflow = Overflow.Hidden;
            _canvasSurface.RegisterCallback<PointerDownEvent>(OnCanvasPointerDown);
            _psdCanvasViewport.Add(_canvasSurface);

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

            _layoutCanvasViewport = CreatePreviewViewport();
            _layoutCanvasViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateCanvasGeometry());
            _canvasViewport.Add(_layoutCanvasViewport);

            _layoutCanvasSurface = new VisualElement();
            _layoutCanvasSurface.style.position = Position.Absolute;
            _layoutCanvasSurface.style.backgroundColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            _layoutCanvasSurface.style.overflow = Overflow.Hidden;
            _layoutCanvasViewport.Add(_layoutCanvasSurface);

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
            UpdatePreviewModeButtons();
            return body;
        }

        private static VisualElement CreatePreviewViewport()
        {
            VisualElement viewport = new VisualElement();
            viewport.style.flexGrow = 1f;
            viewport.style.overflow = Overflow.Hidden;
            viewport.style.position = Position.Relative;
            return viewport;
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
                _layoutHistory.Reset(_configData);
                _selectedLayer = _psd.Children.Count > 0 ? _psd.Children[0] : null;
                _selectedLayers.Clear();
                if (_selectedLayer != null)
                    _selectedLayers.Add(_selectedLayer);
                _selectedVirtualGroup = null;
                _previewButtonState = PsdUiToolkitButtonVisualState.Normal;
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
            DestroyLayoutPreviewCache();
            _selectedLayer = null;
            _selectedLayers.Clear();
            _selectedVirtualGroup = null;
            _previewButtonState = PsdUiToolkitButtonVisualState.Normal;
            _currentLayoutTree = null;
            _layoutPreviewSizeOverrides.Clear();
            _layoutHistory.Clear();
            _configMap = null;
            _configData = null;
            _psd?.ReleaseAllData();
            _psd = null;
            _canvasClickCandidates.Clear();
            if (_canvasImage != null)
                _canvasImage.image = null;
            _layoutCanvasSurface?.Clear();
        }

        private void RefreshView()
        {
            RebuildLayerTree();
            RebuildInspector();
            UpdatePreview();
            UpdateCanvasGeometry();
            UpdateHistoryButtons();
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
                AddVirtualGroupDetachDropTarget();
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
            string warningMarker = NodeHasWarning(node, nodeName) ? " [!]" : string.Empty;
            string roleMarker = node.ItemRole == PsdUiToolkitItemRole.Background
                ? " [BG]"
                : (node.ItemRole == PsdUiToolkitItemRole.KeepAbsolute ? " [FLOAT]" : string.Empty);
            string layoutMarker = node.LayoutType == PsdUiToolkitLayoutType.Row
                ? (node.WrapMode == PsdUiToolkitWrapMode.Wrap ? "[ROW/WRAP] " : "[ROW] ")
                : (node.LayoutType == PsdUiToolkitLayoutType.Column
                    ? (node.WrapMode == PsdUiToolkitWrapMode.Wrap ? "[COL/WRAP] " : "[COL] ")
                    : "[ABS] ");
            row.text =
                $"{exportMarker}{visibilityMarker}{new string(' ', depth * 2)}{layoutMarker}{prefix}{nodeName} ({kindLabel}){roleMarker}{warningMarker}";
            if (node.SourceLayer == null && string.IsNullOrEmpty(node.VirtualGroupId))
                row.SetEnabled(false);
            else
                RegisterLayoutNodeDragSource(row, node.Reference);
            if (!string.IsNullOrEmpty(node.VirtualGroupId))
            {
                RegisterVirtualGroupDropTarget(
                    row,
                    PsdUiToolkitNodeReference.VirtualGroup(node.VirtualGroupId));
            }

            _layerTreeScroll.Add(row);
            if (node.SourceLayer != null && !_layerRows.ContainsKey(node.SourceLayer))
                _layerRows.Add(node.SourceLayer, row);

            for (int i = 0; i < node.Children.Count; i++)
                AddLayoutNodeRow(node.Children[i], depth + 1);
        }

        private void RegisterLayoutNodeDragSource(
            VisualElement element,
            PsdUiToolkitNodeReference reference)
        {
            if (element == null || !reference.IsValid)
                return;
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                _dragCandidateReference = reference;
                _dragCandidateStart = evt.position;
                _dragCandidateArmed = true;
            }, TrickleDown.TrickleDown);
            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_dragCandidateArmed
                    || !_dragCandidateReference.Equals(reference)
                    || (evt.pressedButtons & 1) == 0
                    || ((Vector2)evt.position - _dragCandidateStart).sqrMagnitude < 25f)
                {
                    return;
                }

                _dragCandidateArmed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(
                    "PsdUiToolkit.NodeReference",
                    reference);
                DragAndDrop.StartDrag(GetReferenceDisplayName(reference));
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            element.RegisterCallback<PointerUpEvent>(_ =>
                _dragCandidateArmed = false,
                TrickleDown.TrickleDown);
        }

        private void RegisterVirtualGroupDropTarget(
            VisualElement element,
            PsdUiToolkitNodeReference targetReference)
        {
            if (element == null
                || targetReference.kind
                    != PsdUiToolkitNodeReferenceKind.VirtualGroup)
            {
                return;
            }
            element.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!TryGetDraggedNodeReference(out PsdUiToolkitNodeReference source))
                    return;
                PsdUiToolkitVirtualGroupConfig target =
                    FindVirtualGroup(targetReference.virtualGroupId);
                DragAndDrop.visualMode = CanMoveReferenceIntoGroup(
                    source,
                    target,
                    out _)
                        ? DragAndDropVisualMode.Move
                        : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            element.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!TryGetDraggedNodeReference(out PsdUiToolkitNodeReference source))
                    return;
                PsdUiToolkitVirtualGroupConfig target =
                    FindVirtualGroup(targetReference.virtualGroupId);
                if (!CanMoveReferenceIntoGroup(source, target, out _))
                    return;
                DragAndDrop.AcceptDrag();
                MoveReferenceIntoGroup(source, target);
                evt.StopPropagation();
            });
        }

        private void AddVirtualGroupDetachDropTarget()
        {
            Label target = new Label(
                "PSD hierarchy — drop here to remove from a virtual group");
            target.style.marginBottom = 5f;
            target.style.paddingLeft = 6f;
            target.style.paddingTop = 4f;
            target.style.paddingBottom = 4f;
            target.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!TryGetDraggedNodeReference(out PsdUiToolkitNodeReference source))
                    return;
                DragAndDrop.visualMode = IsOwnedByVirtualGroup(source)
                    ? DragAndDropVisualMode.Move
                    : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!TryGetDraggedNodeReference(out PsdUiToolkitNodeReference source)
                    || !IsOwnedByVirtualGroup(source))
                {
                    return;
                }
                DragAndDrop.AcceptDrag();
                DetachReferenceFromVirtualGroups(source);
                evt.StopPropagation();
            });
            _layerTreeScroll.Add(target);
        }

        private static bool TryGetDraggedNodeReference(
            out PsdUiToolkitNodeReference reference)
        {
            object value = DragAndDrop.GetGenericData(
                "PsdUiToolkit.NodeReference");
            if (value is PsdUiToolkitNodeReference dragged
                && dragged.IsValid)
            {
                reference = dragged;
                return true;
            }
            reference = default;
            return false;
        }

        private bool CanMoveReferenceIntoGroup(
            PsdUiToolkitNodeReference source,
            PsdUiToolkitVirtualGroupConfig target,
            out string reason)
        {
            reason = string.Empty;
            if (!source.IsValid || target == null)
            {
                reason = "The source or destination no longer exists.";
                return false;
            }
            PsdUiToolkitNodeReference targetReference =
                PsdUiToolkitNodeReference.VirtualGroup(target.id);
            if (source.Equals(targetReference))
            {
                reason = "A group cannot contain itself.";
                return false;
            }
            for (int i = 0; i < target.members.Length; i++)
            {
                if (target.members[i].Equals(source))
                {
                    reason = "The item is already a direct member of this group.";
                    return false;
                }
            }

            if (source.kind == PsdUiToolkitNodeReferenceKind.Layer)
            {
                Layer layer = FindLayerById(source.layerId);
                if (layer == null
                    || !TryGetParentLayerId(layer, out int parentId)
                    || parentId != target.hostParentLayerId)
                {
                    reason = "Only direct siblings from the same PSD parent can be regrouped.";
                    return false;
                }
            }
            else
            {
                PsdUiToolkitVirtualGroupConfig sourceGroup =
                    FindVirtualGroup(source.virtualGroupId);
                if (sourceGroup == null
                    || sourceGroup.hostParentLayerId
                        != target.hostParentLayerId)
                {
                    reason = "Nested groups must share the same PSD host parent.";
                    return false;
                }
                if (GroupContains(
                    sourceGroup.id,
                    target.id,
                    new HashSet<string>()))
                {
                    reason = "This move would create a virtual-group cycle.";
                    return false;
                }
            }
            return true;
        }

        private bool IsOwnedByVirtualGroup(PsdUiToolkitNodeReference source)
        {
            PsdUiToolkitVirtualGroupConfig[] groups =
                EnsureConfigData().virtualGroups;
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[groupIndex];
                if (group == null)
                    continue;
                for (int memberIndex = 0;
                    memberIndex < group.members.Length;
                    memberIndex++)
                {
                    if (group.members[memberIndex].Equals(source))
                        return true;
                }
            }
            return false;
        }

        private void MoveReferenceIntoGroup(
            PsdUiToolkitNodeReference source,
            PsdUiToolkitVirtualGroupConfig target)
        {
            ApplyLayoutMutation(() =>
            {
                RemoveReferenceFromAllVirtualGroups(source);
                List<PsdUiToolkitNodeReference> members =
                    new List<PsdUiToolkitNodeReference>(target.members);
                members.Add(source);
                target.members = members.ToArray();
                _selectedVirtualGroup = target;
                _selectedLayer = null;
                _selectedLayers.Clear();
            });
        }

        private void DetachReferenceFromVirtualGroups(
            PsdUiToolkitNodeReference source)
        {
            ApplyLayoutMutation(() => RemoveReferenceFromAllVirtualGroups(source));
        }

        private void RemoveReferenceFromAllVirtualGroups(
            PsdUiToolkitNodeReference source)
        {
            PsdUiToolkitVirtualGroupConfig[] groups =
                EnsureConfigData().virtualGroups;
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[groupIndex];
                if (group == null)
                    continue;
                List<PsdUiToolkitNodeReference> members =
                    new List<PsdUiToolkitNodeReference>(group.members);
                members.RemoveAll(item => item.Equals(source));
                group.members = members.ToArray();
            }
        }

        private bool NodeHasWarning(PsdUiToolkitLayoutNode node, string nodeName)
        {
            if (_currentLayoutTree == null)
                return false;

            string groupId = node?.VirtualGroupId;
            int layerId = node?.SourceLayer?.LayerId ?? -1;
            for (int i = 0; i < _currentLayoutTree.Diagnostics.Count; i++)
            {
                PsdUiToolkitLayoutDiagnostic diagnostic =
                    _currentLayoutTree.Diagnostics[i];
                if ((layerId >= 0 && diagnostic.LayerId == layerId)
                    || (!string.IsNullOrEmpty(groupId)
                        && diagnostic.VirtualGroupId == groupId))
                {
                    return true;
                }
            }

            for (int i = 0; i < _currentLayoutTree.Warnings.Count; i++)
            {
                string warning = _currentLayoutTree.Warnings[i] ?? string.Empty;
                if (!string.IsNullOrEmpty(nodeName)
                    && warning.IndexOf(nodeName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(groupId)
                    && warning.IndexOf(groupId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLayoutDiagnostics(int layerId = -1, string virtualGroupId = null)
        {
            if (_currentLayoutTree == null)
                return;

            for (int i = 0; i < _currentLayoutTree.Diagnostics.Count; i++)
            {
                PsdUiToolkitLayoutDiagnostic diagnostic =
                    _currentLayoutTree.Diagnostics[i];
                if ((layerId >= 0 && diagnostic.LayerId == layerId)
                    || (!string.IsNullOrEmpty(virtualGroupId)
                        && diagnostic.VirtualGroupId == virtualGroupId))
                {
                    _inspectorScroll.Add(new HelpBox(
                        diagnostic.Message,
                        HelpBoxMessageType.Warning));
                }
            }
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
                    DestroyLayoutPreviewCache();
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
                    RefreshView();
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
            if (_selectedLayer.LayerId.HasValue)
                AddSemanticInspector(PsdUiToolkitNodeReference.Layer(_selectedLayer.LayerId.Value));
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
                "Export uses only the layout intent selected here. Fine spacing remains editable in UI Builder.",
                HelpBoxMessageType.Info));
            AddConfiguredGroupsSection();
        }

        private void AddManualLayoutInspectorSection(PsdUiToolkitLayerConfig config)
        {
            if (config == null)
                return;

            _inspectorScroll.Add(new Label("Layout Intent") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10f } });
            AddLayoutDiagnostics(_selectedLayer?.LayerId ?? -1);

            DropdownField itemRoleField = new DropdownField(
                "In parent",
                new List<string>(ItemRoleChoices),
                (int)config.itemRole);
            itemRoleField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitItemRole value =
                    (PsdUiToolkitItemRole)itemRoleField.choices.IndexOf(evt.newValue);
                ApplyLayoutMutation(() => config.itemRole = value);
            });
            _inspectorScroll.Add(itemRoleField);

            DropdownField childrenLayoutField = new DropdownField(
                "Arrange children",
                new List<string>(ContainerLayoutChoices),
                GetLayoutChoiceIndex(config.childrenLayout));
            bool canArrangeChildren =
                _selectedLayer != null && _selectedLayer.IsGroup && !config.merge;
            childrenLayoutField.SetEnabled(canArrangeChildren);
            childrenLayoutField.RegisterValueChangedCallback(evt =>
            {
                PsdUiToolkitContainerLayout value =
                    GetLayoutFromChoiceIndex(childrenLayoutField.choices.IndexOf(evt.newValue));
                ApplyLayoutMutation(() => config.childrenLayout = value);
            });
            _inspectorScroll.Add(childrenLayoutField);

            if (_selectedLayer == null || !_selectedLayer.IsGroup)
            {
                _inspectorScroll.Add(new HelpBox(
                    "This node is a leaf and has no editable children. Select a PSD Group to arrange children.",
                    HelpBoxMessageType.Info));
            }
            else if (config.merge)
            {
                _inspectorScroll.Add(new HelpBox(
                    "Merge export turns this Group into one image. Turn off Merge export to arrange its children.",
                    HelpBoxMessageType.Warning));
            }

            bool canConfigureAxes = canArrangeChildren
                && (config.childrenLayout == PsdUiToolkitContainerLayout.Row
                    || config.childrenLayout == PsdUiToolkitContainerLayout.Column);
            AddAxisLayoutFields(
                config.mainAxisDistribution,
                config.crossAxisAlignment,
                canConfigureAxes,
                main => ApplyLayoutMutation(() => config.mainAxisDistribution = main),
                cross => ApplyLayoutMutation(() => config.crossAxisAlignment = cross));
            AddWrapLayoutFields(
                config.wrapMode,
                config.multiLineDistribution,
                canConfigureAxes,
                wrap => ApplyLayoutMutation(() => config.wrapMode = wrap),
                lines => ApplyLayoutMutation(() => config.multiLineDistribution = lines));
        }

        private void AddSemanticInspector(PsdUiToolkitNodeReference owner)
        {
            PsdUiToolkitLayoutNode ownerNode = FindLayoutNode(owner);
            if (ownerNode == null)
                return;

            PsdUiToolkitExportConfigData data = EnsureConfigData();
            PsdUiToolkitButtonSemanticConfig button = FindButton(owner);

            _inspectorScroll.Add(new Label("Control Semantics")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 12f,
                },
            });

            Toggle buttonToggle = new Toggle("Button")
            {
                value = button != null,
            };
            buttonToggle.SetEnabled(ownerNode.Children.Count > 0 || button != null);
            buttonToggle.RegisterValueChangedCallback(evt =>
            {
                ApplyLayoutMutation(() =>
                {
                    List<PsdUiToolkitButtonSemanticConfig> buttons =
                        new List<PsdUiToolkitButtonSemanticConfig>(
                            data.buttons ?? Array.Empty<PsdUiToolkitButtonSemanticConfig>());
                    buttons.RemoveAll(item => item != null && item.owner.Equals(owner));
                    if (evt.newValue)
                    {
                        buttons.Add(new PsdUiToolkitButtonSemanticConfig
                        {
                            owner = owner,
                            states = Array.Empty<PsdUiToolkitButtonStateBinding>(),
                        });
                    }
                    data.buttons = buttons.ToArray();
                });
            });
            _inspectorScroll.Add(buttonToggle);
            if (ownerNode.Children.Count == 0 && button == null)
            {
                _inspectorScroll.Add(new HelpBox(
                    "Button semantics require a container with descendant visuals.",
                    HelpBoxMessageType.Info));
            }

            if (button != null)
            {
                List<PsdUiToolkitLayoutNode> descendants =
                    CollectLayoutDescendants(ownerNode);
                for (int stateIndex = 0;
                    stateIndex < Enum.GetValues(typeof(PsdUiToolkitButtonVisualState)).Length;
                    stateIndex++)
                {
                    AddButtonStateField(
                        button,
                        (PsdUiToolkitButtonVisualState)stateIndex,
                        descendants);
                }

                DropdownField previewState = new DropdownField(
                    "Preview state",
                    new List<string>
                    {
                        "Normal",
                        "Hover",
                        "Pressed",
                        "Disabled",
                        "Focused",
                    },
                    (int)_previewButtonState);
                previewState.RegisterValueChangedCallback(evt =>
                {
                    _previewButtonState =
                        (PsdUiToolkitButtonVisualState)previewState.choices.IndexOf(
                            evt.newValue);
                    RebuildLayoutPreview();
                });
                _inspectorScroll.Add(previewState);
                if (!button.TryGetState(
                    PsdUiToolkitButtonVisualState.Normal,
                    out _))
                {
                    _inspectorScroll.Add(new HelpBox(
                        "Normal is required. Until it is assigned this node exports as a regular container.",
                        HelpBoxMessageType.Warning));
                }
            }

        }

        private void AddButtonStateField(
            PsdUiToolkitButtonSemanticConfig button,
            PsdUiToolkitButtonVisualState state,
            List<PsdUiToolkitLayoutNode> descendants)
        {
            List<string> choices = new List<string> { "None" };
            List<PsdUiToolkitNodeReference> references =
                new List<PsdUiToolkitNodeReference> { default };
            int selectedIndex = 0;
            button.TryGetState(state, out PsdUiToolkitNodeReference selected);
            for (int i = 0; i < descendants.Count; i++)
            {
                PsdUiToolkitLayoutNode node = descendants[i];
                if (!node.Reference.IsValid)
                    continue;
                choices.Add(GetReferenceDisplayName(node.Reference));
                references.Add(node.Reference);
                if (node.Reference.Equals(selected))
                    selectedIndex = choices.Count - 1;
            }

            DropdownField field = new DropdownField(
                state.ToString(),
                choices,
                selectedIndex);
            field.RegisterValueChangedCallback(evt =>
            {
                int index = field.choices.IndexOf(evt.newValue);
                PsdUiToolkitNodeReference next = index <= 0
                    ? default
                    : references[index];
                ApplyLayoutMutation(() =>
                {
                    List<PsdUiToolkitButtonStateBinding> states =
                        new List<PsdUiToolkitButtonStateBinding>(
                            button.states
                            ?? Array.Empty<PsdUiToolkitButtonStateBinding>());
                    states.RemoveAll(item =>
                        item == null
                        || item.state == state
                        || (next.IsValid && item.source.Equals(next)));
                    if (next.IsValid)
                    {
                        states.Add(new PsdUiToolkitButtonStateBinding
                        {
                            state = state,
                            source = next,
                        });
                    }
                    button.states = states.ToArray();
                });
            });
            _inspectorScroll.Add(field);
        }

        private PsdUiToolkitButtonSemanticConfig FindButton(
            PsdUiToolkitNodeReference owner)
        {
            PsdUiToolkitButtonSemanticConfig[] buttons =
                EnsureConfigData().buttons
                ?? Array.Empty<PsdUiToolkitButtonSemanticConfig>();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].owner.Equals(owner))
                    return buttons[i];
            }
            return null;
        }

        private PsdUiToolkitLayoutNode FindLayoutNode(
            PsdUiToolkitNodeReference reference)
        {
            return _currentLayoutTree == null
                ? null
                : FindLayoutNodeRecursive(_currentLayoutTree.Children, reference);
        }

        private static PsdUiToolkitLayoutNode FindLayoutNodeRecursive(
            List<PsdUiToolkitLayoutNode> nodes,
            PsdUiToolkitNodeReference reference)
        {
            if (nodes == null)
                return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                PsdUiToolkitLayoutNode node = nodes[i];
                if (node.Reference.Equals(reference))
                    return node;
                PsdUiToolkitLayoutNode nested =
                    FindLayoutNodeRecursive(node.Children, reference);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static List<PsdUiToolkitLayoutNode> CollectLayoutDescendants(
            PsdUiToolkitLayoutNode root)
        {
            List<PsdUiToolkitLayoutNode> result =
                new List<PsdUiToolkitLayoutNode>();
            CollectLayoutDescendantsRecursive(root, result);
            return result;
        }

        private static void CollectLayoutDescendantsRecursive(
            PsdUiToolkitLayoutNode root,
            List<PsdUiToolkitLayoutNode> result)
        {
            if (root == null)
                return;
            for (int i = 0; i < root.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = root.Children[i];
                if (child.Reference.IsValid)
                    result.Add(child);
                CollectLayoutDescendantsRecursive(child, result);
            }
        }

        private string GetReferenceDisplayName(PsdUiToolkitNodeReference reference)
        {
            PsdUiToolkitLayoutNode node = FindLayoutNode(reference);
            if (node != null)
            {
                string label = string.IsNullOrWhiteSpace(node.DisplayName)
                    ? reference.StableKey
                    : node.DisplayName;
                return $"{label} ({reference.StableKey})";
            }
            return $"Missing {reference.StableKey}";
        }

        private static int GetLayoutChoiceIndex(PsdUiToolkitContainerLayout layout)
        {
            switch (layout)
            {
                case PsdUiToolkitContainerLayout.Row:
                    return 1;
                case PsdUiToolkitContainerLayout.Column:
                    return 2;
                default:
                    return 0;
            }
        }

        private static PsdUiToolkitContainerLayout GetLayoutFromChoiceIndex(int index)
        {
            switch (index)
            {
                case 1:
                    return PsdUiToolkitContainerLayout.Row;
                case 2:
                    return PsdUiToolkitContainerLayout.Column;
                default:
                    return PsdUiToolkitContainerLayout.Absolute;
            }
        }

        private void AddAxisLayoutFields(
            PsdUiToolkitMainAxisDistribution mainAxis,
            PsdUiToolkitCrossAxisAlignment crossAxis,
            bool enabled,
            Action<PsdUiToolkitMainAxisDistribution> onMainChanged,
            Action<PsdUiToolkitCrossAxisAlignment> onCrossChanged)
        {
            DropdownField mainAxisField = new DropdownField(
                "Main axis",
                new List<string>(MainAxisChoices),
                (int)mainAxis);
            mainAxisField.SetEnabled(enabled);
            mainAxisField.RegisterValueChangedCallback(evt =>
            {
                int index = mainAxisField.choices.IndexOf(evt.newValue);
                onMainChanged?.Invoke((PsdUiToolkitMainAxisDistribution)Math.Max(0, index));
            });
            _inspectorScroll.Add(mainAxisField);

            DropdownField crossAxisField = new DropdownField(
                "Cross axis",
                new List<string>(CrossAxisChoices),
                (int)crossAxis);
            crossAxisField.SetEnabled(enabled);
            crossAxisField.RegisterValueChangedCallback(evt =>
            {
                int index = crossAxisField.choices.IndexOf(evt.newValue);
                onCrossChanged?.Invoke((PsdUiToolkitCrossAxisAlignment)Math.Max(0, index));
            });
            _inspectorScroll.Add(crossAxisField);

            if (!enabled)
            {
                _inspectorScroll.Add(new HelpBox(
                    "Main-axis distribution and cross-axis alignment are available after choosing Row or Column.",
                    HelpBoxMessageType.Info));
            }
        }

        private void AddWrapLayoutFields(
            PsdUiToolkitWrapMode wrapMode,
            PsdUiToolkitMultiLineDistribution multiLineDistribution,
            bool enabled,
            Action<PsdUiToolkitWrapMode> onWrapChanged,
            Action<PsdUiToolkitMultiLineDistribution> onMultiLineChanged)
        {
            DropdownField wrapField = new DropdownField(
                "Wrap",
                new List<string>(WrapChoices),
                (int)wrapMode);
            wrapField.SetEnabled(enabled);
            _inspectorScroll.Add(wrapField);

            DropdownField lineField = new DropdownField(
                "Multiple lines",
                new List<string>(MultiLineChoices),
                (int)multiLineDistribution);
            lineField.SetEnabled(enabled && wrapMode == PsdUiToolkitWrapMode.Wrap);
            _inspectorScroll.Add(lineField);

            wrapField.RegisterValueChangedCallback(evt =>
            {
                int index = Math.Max(0, wrapField.choices.IndexOf(evt.newValue));
                onWrapChanged?.Invoke((PsdUiToolkitWrapMode)index);
            });
            lineField.RegisterValueChangedCallback(evt =>
            {
                int index = Math.Max(0, lineField.choices.IndexOf(evt.newValue));
                onMultiLineChanged?.Invoke((PsdUiToolkitMultiLineDistribution)index);
            });
        }

        private void AddMultiSelectionInspector()
        {
            _inspectorScroll.Add(new Label("Multiple Selection") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _inspectorScroll.Add(new Label($"{_selectedLayers.Count} layers selected."));
            _inspectorScroll.Add(new HelpBox(
                "Only sibling layers can be wrapped in a layout group. Hold Ctrl or Cmd while clicking tree or canvas nodes.",
                HelpBoxMessageType.Info));

            string validationError = GetVirtualGroupSelectionError(_selectedLayers);
            if (!string.IsNullOrEmpty(validationError))
                _inspectorScroll.Add(new HelpBox(validationError, HelpBoxMessageType.Warning));

            Button createRow = new Button(() => CreateVirtualGroupFromLayers(
                _selectedLayers,
                PsdUiToolkitContainerLayout.Row))
            {
                text = "Create Row Group",
            };
            Button createColumn = new Button(() => CreateVirtualGroupFromLayers(_selectedLayers, PsdUiToolkitContainerLayout.Column))
            {
                text = "Create Column Group",
            };
            createRow.SetEnabled(string.IsNullOrEmpty(validationError));
            createColumn.SetEnabled(string.IsNullOrEmpty(validationError));
            _inspectorScroll.Add(createRow);
            _inspectorScroll.Add(createColumn);

            AddCompatibleVirtualGroupTargets();
        }

        private string GetVirtualGroupSelectionError(
            IEnumerable<Layer> selectedLayers,
            PsdUiToolkitVirtualGroupConfig targetGroup = null)
        {
            List<Layer> layers = new List<Layer>();
            HashSet<int> ids = new HashSet<int>();
            foreach (Layer layer in selectedLayers)
            {
                if (layer?.LayerId != null && ids.Add(layer.LayerId.Value))
                    layers.Add(layer);
            }

            int minimum = targetGroup == null ? 2 : 1;
            if (layers.Count < minimum)
                return targetGroup == null
                    ? "Select at least two sibling layers."
                    : "Select at least one sibling layer to add.";

            if (!TryGetParentLayerId(layers[0], out int parentLayerId))
                return "Could not resolve the selected layer parent.";
            if (targetGroup != null && targetGroup.hostParentLayerId != parentLayerId)
                return "Selected layers must share the layout group's direct parent.";

            for (int i = 1; i < layers.Count; i++)
            {
                if (!TryGetParentLayerId(layers[i], out int currentParentId)
                    || currentParentId != parentLayerId)
                {
                    return "All selected layers must share the same direct parent.";
                }
            }

            PsdUiToolkitVirtualGroupConfig[] groups = EnsureConfigData().virtualGroups;
            for (int i = 0; i < groups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig existing = groups[i];
                if (existing == null || existing.id == targetGroup?.id)
                    continue;

                for (int memberIndex = 0; memberIndex < existing.members.Length; memberIndex++)
                {
                    PsdUiToolkitNodeReference member = existing.members[memberIndex];
                    if (member.kind == PsdUiToolkitNodeReferenceKind.Layer
                        && ids.Contains(member.layerId))
                    {
                        return $"A selected layer already belongs to layout group '{existing.name}'.";
                    }
                }
            }

            return string.Empty;
        }

        private void AddCompatibleVirtualGroupTargets()
        {
            PsdUiToolkitVirtualGroupConfig[] groups = EnsureConfigData().virtualGroups;
            bool addedHeader = false;
            for (int i = 0; i < groups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[i];
                if (group == null
                    || !string.IsNullOrEmpty(GetVirtualGroupSelectionError(_selectedLayers, group))
                    || SelectionAlreadyInGroup(_selectedLayers, group))
                {
                    continue;
                }

                if (!addedHeader)
                {
                    _inspectorScroll.Add(new Label("Add to existing group")
                    {
                        style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8f },
                    });
                    addedHeader = true;
                }

                Button addButton = new Button(() => AddMembersToVirtualGroup(group, _selectedLayers))
                {
                    text = group.name,
                };
                _inspectorScroll.Add(addButton);
            }
        }

        private static bool SelectionAlreadyInGroup(
            IEnumerable<Layer> selectedLayers,
            PsdUiToolkitVirtualGroupConfig group)
        {
            HashSet<int> members = new HashSet<int>();
            for (int i = 0; i < group.members.Length; i++)
            {
                if (group.members[i].kind == PsdUiToolkitNodeReferenceKind.Layer)
                    members.Add(group.members[i].layerId);
            }
            foreach (Layer layer in selectedLayers)
            {
                if (layer?.LayerId != null && !members.Contains(layer.LayerId.Value))
                    return false;
            }

            return true;
        }

        private void AddMembersToVirtualGroup(
            PsdUiToolkitVirtualGroupConfig group,
            IEnumerable<Layer> selectedLayers)
        {
            string error = GetVirtualGroupSelectionError(selectedLayers, group);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog("Layout Group", error, "OK");
                return;
            }

            List<PsdUiToolkitNodeReference> members =
                new List<PsdUiToolkitNodeReference>(group.members);
            HashSet<PsdUiToolkitNodeReference> seen =
                new HashSet<PsdUiToolkitNodeReference>(members);
            foreach (Layer layer in selectedLayers)
            {
                if (layer?.LayerId != null)
                {
                    PsdUiToolkitNodeReference reference =
                        PsdUiToolkitNodeReference.Layer(layer.LayerId.Value);
                    if (seen.Add(reference))
                        members.Add(reference);
                }
            }

            ApplyLayoutMutation(() =>
            {
                group.members = members.ToArray();
                _selectedVirtualGroup = group;
                _selectedLayers.Clear();
                _selectedLayer = null;
            });
        }

        private void RemoveMemberFromVirtualGroup(
            PsdUiToolkitVirtualGroupConfig group,
            PsdUiToolkitNodeReference memberReference)
        {
            List<PsdUiToolkitNodeReference> members =
                new List<PsdUiToolkitNodeReference>();
            for (int i = 0; i < group.members.Length; i++)
            {
                if (!group.members[i].Equals(memberReference))
                    members.Add(group.members[i]);
            }
            ApplyLayoutMutation(() => group.members = members.ToArray());
        }

        private void AddVirtualGroupInspector(PsdUiToolkitVirtualGroupConfig group)
        {
            _inspectorScroll.Add(new Label("Virtual Layout Group") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            AddLayoutDiagnostics(virtualGroupId: group.id);
            TextField nameField = new TextField("Name")
            {
                value = group.name,
                isDelayed = true,
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                string value = evt.newValue ?? string.Empty;
                ApplyLayoutMutation(() => group.name = value);
            });
            _inspectorScroll.Add(nameField);

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

            AddAxisLayoutFields(
                group.mainAxisDistribution,
                group.crossAxisAlignment,
                true,
                main => ApplyLayoutMutation(() => group.mainAxisDistribution = main),
                cross => ApplyLayoutMutation(() => group.crossAxisAlignment = cross));
            AddWrapLayoutFields(
                group.wrapMode,
                group.multiLineDistribution,
                true,
                wrap => ApplyLayoutMutation(() => group.wrapMode = wrap),
                lines => ApplyLayoutMutation(() => group.multiLineDistribution = lines));

            _inspectorScroll.Add(new Label("Members")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8f },
            });
            for (int i = 0; i < group.members.Length; i++)
            {
                PsdUiToolkitNodeReference memberReference = group.members[i];
                Layer member = memberReference.kind == PsdUiToolkitNodeReferenceKind.Layer
                    ? FindLayerById(memberReference.layerId)
                    : null;
                PsdUiToolkitVirtualGroupConfig childGroup =
                    memberReference.kind == PsdUiToolkitNodeReferenceKind.VirtualGroup
                        ? FindVirtualGroup(memberReference.virtualGroupId)
                        : null;
                VisualElement memberRow = new VisualElement
                {
                    style = { flexDirection = FlexDirection.Row },
                };
                Label memberLabel = new Label(memberReference.kind
                    == PsdUiToolkitNodeReferenceKind.Layer
                        ? (member == null
                            ? $"Missing layer #{memberReference.layerId}"
                            : member.Name)
                        : (childGroup == null
                            ? $"Missing group {memberReference.virtualGroupId}"
                            : $"[Group] {childGroup.name}"));
                memberLabel.style.flexGrow = 1f;
                Button removeButton = new Button(
                    () => RemoveMemberFromVirtualGroup(group, memberReference))
                {
                    text = "Remove",
                };
                memberRow.Add(memberLabel);
                memberRow.Add(removeButton);
                _inspectorScroll.Add(memberRow);
            }

            _inspectorScroll.Add(new Label("Nesting")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8f },
            });
            VisualElement parentButtons = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row },
            };
            Button parentRow = new Button(
                () => CreateParentVirtualGroup(group, PsdUiToolkitContainerLayout.Row))
            {
                text = "Wrap in Row",
            };
            Button parentColumn = new Button(
                () => CreateParentVirtualGroup(group, PsdUiToolkitContainerLayout.Column))
            {
                text = "Wrap in Column",
            };
            parentRow.style.flexGrow = 1f;
            parentColumn.style.flexGrow = 1f;
            parentButtons.Add(parentRow);
            parentButtons.Add(parentColumn);
            _inspectorScroll.Add(parentButtons);

            PsdUiToolkitVirtualGroupConfig[] allGroups = EnsureConfigData().virtualGroups;
            for (int i = 0; i < allGroups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig target = allGroups[i];
                if (target == null
                    || target.id == group.id
                    || target.hostParentLayerId != group.hostParentLayerId
                    || GroupContains(group.id, target.id, new HashSet<string>()))
                {
                    continue;
                }
                Button moveButton = new Button(() => MoveVirtualGroupInto(group, target))
                {
                    text = $"Move into {target.name}",
                };
                _inspectorScroll.Add(moveButton);
            }

            AddSemanticInspector(PsdUiToolkitNodeReference.VirtualGroup(group.id));

            Button dissolveButton = new Button(() => DissolveVirtualGroup(group))
            {
                text = "Dissolve Group",
            };
            dissolveButton.style.marginTop = 8f;
            _inspectorScroll.Add(dissolveButton);
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
                    text = $"{group.name} ({group.layout}, {group.members.Length} items)",
                };
                _inspectorScroll.Add(selectButton);
            }
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
                for (int j = 0; j < existingGroup.members.Length; j++)
                {
                    PsdUiToolkitNodeReference existingMember =
                        existingGroup.members[j];
                    if (existingMember.kind != PsdUiToolkitNodeReferenceKind.Layer)
                        continue;
                    for (int k = 0; k < layers.Count; k++)
                    {
                        if (existingMember.layerId == layers[k].LayerId.Value)
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
            PsdUiToolkitNodeReference[] memberReferences =
                new PsdUiToolkitNodeReference[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                memberReferences[i] =
                    PsdUiToolkitNodeReference.Layer(layers[i].LayerId.Value);
            }

            PsdUiToolkitVirtualGroupConfig group = new PsdUiToolkitVirtualGroupConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = $"{layout} Group {data.virtualGroups.Length + 1}",
                hostParentLayerId = parentLayerId,
                members = memberReferences,
                layout = layout == PsdUiToolkitContainerLayout.Column
                    ? PsdUiToolkitContainerLayout.Column
                    : PsdUiToolkitContainerLayout.Row,
            };

            List<PsdUiToolkitVirtualGroupConfig> groups = new List<PsdUiToolkitVirtualGroupConfig>(data.virtualGroups)
            {
                group,
            };
            ApplyLayoutMutation(() =>
            {
                data.virtualGroups = groups.ToArray();
                _selectedLayers.Clear();
                _selectedLayer = null;
                _selectedVirtualGroup = group;
            });
        }

        private void SetVirtualGroupLayout(
            PsdUiToolkitVirtualGroupConfig group,
            PsdUiToolkitContainerLayout layout)
        {
            ApplyLayoutMutation(() =>
            {
                group.layout = layout == PsdUiToolkitContainerLayout.Column
                    ? PsdUiToolkitContainerLayout.Column
                    : PsdUiToolkitContainerLayout.Row;
            });
        }

        private void CreateParentVirtualGroup(
            PsdUiToolkitVirtualGroupConfig child,
            PsdUiToolkitContainerLayout layout)
        {
            PsdUiToolkitExportConfigData data = EnsureConfigData();
            PsdUiToolkitVirtualGroupConfig parent = new PsdUiToolkitVirtualGroupConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = $"{layout} Group {data.virtualGroups.Length + 1}",
                hostParentLayerId = child.hostParentLayerId,
                members = new[] { PsdUiToolkitNodeReference.VirtualGroup(child.id) },
                layout = layout == PsdUiToolkitContainerLayout.Column
                    ? PsdUiToolkitContainerLayout.Column
                    : PsdUiToolkitContainerLayout.Row,
            };
            ApplyLayoutMutation(() =>
            {
                PsdUiToolkitVirtualGroupConfig owner = FindGroupOwner(child.id);
                if (owner != null)
                    ReplaceGroupReference(owner, child.id, parent.id);
                List<PsdUiToolkitVirtualGroupConfig> groups =
                    new List<PsdUiToolkitVirtualGroupConfig>(data.virtualGroups)
                    {
                        parent,
                    };
                data.virtualGroups = groups.ToArray();
                _selectedVirtualGroup = parent;
            });
        }

        private void MoveVirtualGroupInto(
            PsdUiToolkitVirtualGroupConfig source,
            PsdUiToolkitVirtualGroupConfig target)
        {
            if (source == null
                || target == null
                || source.hostParentLayerId != target.hostParentLayerId
                || GroupContains(source.id, target.id, new HashSet<string>()))
            {
                return;
            }

            ApplyLayoutMutation(() =>
            {
                PsdUiToolkitVirtualGroupConfig owner = FindGroupOwner(source.id);
                if (owner != null)
                    RemoveGroupReference(owner, source.id);
                List<PsdUiToolkitNodeReference> members =
                    new List<PsdUiToolkitNodeReference>(target.members);
                PsdUiToolkitNodeReference reference =
                    PsdUiToolkitNodeReference.VirtualGroup(source.id);
                if (!members.Contains(reference))
                    members.Add(reference);
                target.members = members.ToArray();
                _selectedVirtualGroup = target;
            });
        }

        private PsdUiToolkitVirtualGroupConfig FindGroupOwner(string childGroupId)
        {
            PsdUiToolkitVirtualGroupConfig[] groups = EnsureConfigData().virtualGroups;
            for (int i = 0; i < groups.Length; i++)
            {
                PsdUiToolkitVirtualGroupConfig group = groups[i];
                if (group == null)
                    continue;
                for (int memberIndex = 0; memberIndex < group.members.Length; memberIndex++)
                {
                    PsdUiToolkitNodeReference member = group.members[memberIndex];
                    if (member.kind == PsdUiToolkitNodeReferenceKind.VirtualGroup
                        && member.virtualGroupId == childGroupId)
                    {
                        return group;
                    }
                }
            }
            return null;
        }

        private static void ReplaceGroupReference(
            PsdUiToolkitVirtualGroupConfig owner,
            string oldId,
            string newId)
        {
            for (int i = 0; i < owner.members.Length; i++)
            {
                if (owner.members[i].kind == PsdUiToolkitNodeReferenceKind.VirtualGroup
                    && owner.members[i].virtualGroupId == oldId)
                {
                    owner.members[i] = PsdUiToolkitNodeReference.VirtualGroup(newId);
                }
            }
        }

        private static void RemoveGroupReference(
            PsdUiToolkitVirtualGroupConfig owner,
            string groupId)
        {
            List<PsdUiToolkitNodeReference> members =
                new List<PsdUiToolkitNodeReference>();
            for (int i = 0; i < owner.members.Length; i++)
            {
                if (owner.members[i].kind != PsdUiToolkitNodeReferenceKind.VirtualGroup
                    || owner.members[i].virtualGroupId != groupId)
                {
                    members.Add(owner.members[i]);
                }
            }
            owner.members = members.ToArray();
        }

        private bool GroupContains(
            string rootGroupId,
            string soughtGroupId,
            HashSet<string> visited)
        {
            if (!visited.Add(rootGroupId))
                return false;
            PsdUiToolkitVirtualGroupConfig root = FindVirtualGroup(rootGroupId);
            if (root == null)
                return false;
            for (int i = 0; i < root.members.Length; i++)
            {
                PsdUiToolkitNodeReference member = root.members[i];
                if (member.kind != PsdUiToolkitNodeReferenceKind.VirtualGroup)
                    continue;
                if (member.virtualGroupId == soughtGroupId
                    || GroupContains(member.virtualGroupId, soughtGroupId, visited))
                {
                    return true;
                }
            }
            return false;
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

            ApplyLayoutMutation(() =>
            {
                for (int i = 0; i < data.virtualGroups.Length; i++)
                {
                    PsdUiToolkitVirtualGroupConfig parent = data.virtualGroups[i];
                    if (parent == null || parent == group)
                        continue;
                    List<PsdUiToolkitNodeReference> parentMembers =
                        new List<PsdUiToolkitNodeReference>();
                    for (int memberIndex = 0; memberIndex < parent.members.Length; memberIndex++)
                    {
                        PsdUiToolkitNodeReference reference = parent.members[memberIndex];
                        if (reference.kind == PsdUiToolkitNodeReferenceKind.VirtualGroup
                            && reference.virtualGroupId == group.id)
                        {
                            parentMembers.AddRange(group.members);
                        }
                        else
                        {
                            parentMembers.Add(reference);
                        }
                    }
                    parent.members = parentMembers.ToArray();
                }
                data.virtualGroups = groups.ToArray();
                _selectedVirtualGroup = null;
                _selectedLayers.Clear();
                _selectedLayer = FindFirstLayerMember(group);
                if (_selectedLayer != null)
                    _selectedLayers.Add(_selectedLayer);
            });
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

        private Layer FindFirstLayerMember(PsdUiToolkitVirtualGroupConfig group)
        {
            return FindFirstLayerMember(group, new HashSet<string>());
        }

        private Layer FindFirstLayerMember(
            PsdUiToolkitVirtualGroupConfig group,
            HashSet<string> visited)
        {
            if (group == null || !visited.Add(group.id))
                return null;
            for (int i = 0; i < group.members.Length; i++)
            {
                PsdUiToolkitNodeReference member = group.members[i];
                if (member.kind == PsdUiToolkitNodeReferenceKind.Layer)
                {
                    Layer layer = FindLayerById(member.layerId);
                    if (layer != null)
                        return layer;
                }
                else
                {
                    Layer nested = FindFirstLayerMember(
                        FindVirtualGroup(member.virtualGroupId),
                        visited);
                    if (nested != null)
                        return nested;
                }
            }
            return null;
        }

        private void ApplyLayoutMutation(Action mutation)
        {
            if (mutation == null || _configData == null)
                return;

            mutation();
            PersistConfig();
            _layoutHistory.Record(_configData);
            RefreshView();
        }

        private void UndoLayoutEdit()
        {
            RestoreLayoutHistory(true);
        }

        private void RedoLayoutEdit()
        {
            RestoreLayoutHistory(false);
        }

        private void RestoreLayoutHistory(bool undo)
        {
            if (_configData == null)
                return;

            string selectedGroupId = _selectedVirtualGroup?.id;
            bool changed = undo
                ? _layoutHistory.Undo(_configData)
                : _layoutHistory.Redo(_configData);
            if (!changed)
                return;

            _selectedVirtualGroup = string.IsNullOrEmpty(selectedGroupId)
                ? null
                : FindVirtualGroup(selectedGroupId);
            PersistConfig();
            RefreshView();
            UpdateStatus(undo ? "Undid layout edit." : "Redid layout edit.");
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Z || !(evt.ctrlKey || evt.commandKey))
                return;
            if (rootVisualElement.focusController?.focusedElement is TextField)
                return;

            if (evt.shiftKey)
                RedoLayoutEdit();
            else
                UndoLayoutEdit();
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void UpdateHistoryButtons()
        {
            _undoButton?.SetEnabled(_layoutHistory.CanUndo);
            _redoButton?.SetEnabled(_layoutHistory.CanRedo);
        }

        private PsdUiToolkitLayoutTree BuildCurrentAnalysisTree(string rootName)
        {
            return PsdUiToolkitManualLayoutBuilder.BuildForInspector(_psd, _configMap, rootName);
        }

        private PsdUiToolkitExportConfigData EnsureConfigData()
        {
            _configData ??= new PsdUiToolkitExportConfigData();
            _configData = PsdUiToolkitConfigStore.MigrateToCurrentVersion(_configData);
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

        private void SetCanvasPreviewMode(CanvasPreviewMode mode)
        {
            if (_canvasPreviewMode == mode)
                return;

            _canvasPreviewMode = mode;
            UpdatePreviewModeButtons();
            UpdateCanvasGeometry();
            _canvasViewport?.schedule.Execute(UpdateCanvasGeometry);
        }

        private void UpdatePreviewModeButtons()
        {
            Color selected = new Color(0.18f, 0.35f, 0.58f, 0.75f);
            Color normal = new Color(0f, 0f, 0f, 0f);
            if (_psdPreviewButton != null)
                _psdPreviewButton.style.backgroundColor =
                    _canvasPreviewMode == CanvasPreviewMode.Psd ? selected : normal;
            if (_layoutPreviewButton != null)
                _layoutPreviewButton.style.backgroundColor =
                    _canvasPreviewMode == CanvasPreviewMode.Layout ? selected : normal;
            if (_splitPreviewButton != null)
                _splitPreviewButton.style.backgroundColor =
                    _canvasPreviewMode == CanvasPreviewMode.Split ? selected : normal;
        }

        private void UpdateCanvasGeometry()
        {
            if (_canvasViewport == null
                || _psdCanvasViewport == null
                || _layoutCanvasViewport == null
                || _canvasSurface == null
                || _layoutCanvasSurface == null)
            {
                return;
            }

            bool hasPsd = _psd != null && _psd.Width > 0 && _psd.Height > 0;
            if (_canvasEmptyLabel != null)
                _canvasEmptyLabel.style.display = hasPsd ? DisplayStyle.None : DisplayStyle.Flex;
            bool showPsd = hasPsd && _canvasPreviewMode != CanvasPreviewMode.Layout;
            bool showLayout = hasPsd && _canvasPreviewMode != CanvasPreviewMode.Psd;
            _psdCanvasViewport.style.display = showPsd ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutCanvasViewport.style.display = showLayout ? DisplayStyle.Flex : DisplayStyle.None;
            _canvasSurface.style.display = showPsd ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutCanvasSurface.style.display = showLayout ? DisplayStyle.Flex : DisplayStyle.None;
            if (_canvasTitleLabel != null)
            {
                _canvasTitleLabel.text = hasPsd
                    ? $"Canvas Preview  {_psd.Width} x {_psd.Height}  ({_canvasPreviewMode})"
                    : "Canvas Preview";
            }
            if (!hasPsd)
            {
                _canvasOverlay?.Clear();
                _layoutCanvasSurface.Clear();
                return;
            }

            if (showPsd)
            {
                FitPreviewSurface(
                    _psdCanvasViewport,
                    _canvasSurface,
                    out _canvasDrawWidth,
                    out _canvasDrawHeight);
                if (_canvasImage != null)
                    _canvasImage.image = _psdCompositePreview;
            }
            else
            {
                _canvasDrawWidth = 0f;
                _canvasDrawHeight = 0f;
            }

            RebuildCanvasOverlays();

            if (showLayout)
            {
                FitPreviewSurface(
                    _layoutCanvasViewport,
                    _layoutCanvasSurface,
                    out _layoutCanvasDrawWidth,
                    out _layoutCanvasDrawHeight);
                RebuildLayoutPreview();
            }
            else
            {
                _layoutCanvasDrawWidth = 0f;
                _layoutCanvasDrawHeight = 0f;
                _layoutCanvasSurface.Clear();
            }
        }

        private void FitPreviewSurface(
            VisualElement viewport,
            VisualElement surface,
            out float drawWidth,
            out float drawHeight)
        {
            drawWidth = 0f;
            drawHeight = 0f;
            Rect rect = viewport.contentRect;
            if (rect.width <= 0f
                || rect.height <= 0f
                || float.IsNaN(rect.width)
                || float.IsNaN(rect.height))
            {
                return;
            }

            float scale = Mathf.Min(rect.width / _psd.Width, rect.height / _psd.Height);
            drawWidth = _psd.Width * scale;
            drawHeight = _psd.Height * scale;
            surface.style.left = (rect.width - drawWidth) * 0.5f;
            surface.style.top = (rect.height - drawHeight) * 0.5f;
            surface.style.width = drawWidth;
            surface.style.height = drawHeight;
        }

        private void RebuildLayoutPreview()
        {
            _layoutCanvasSurface?.Clear();
            _layoutCanvasRoot = null;
            if (_layoutCanvasSurface == null
                || _currentLayoutTree == null
                || _configMap == null
                || _layoutCanvasDrawWidth <= 0f
                || _layoutCanvasDrawHeight <= 0f)
            {
                return;
            }

            float scale = _layoutCanvasDrawWidth / Math.Max(1, _currentLayoutTree.Width);
            _layoutCanvasRoot = new VisualElement();
            _layoutCanvasRoot.style.position = Position.Absolute;
            _layoutCanvasRoot.style.left = 0f;
            _layoutCanvasRoot.style.top = 0f;
            _layoutCanvasRoot.style.width = _layoutCanvasDrawWidth;
            _layoutCanvasRoot.style.height = _layoutCanvasDrawHeight;
            _layoutCanvasRoot.style.overflow = Overflow.Hidden;
            _layoutCanvasSurface.Add(_layoutCanvasRoot);

            for (int i = 0; i < _currentLayoutTree.Children.Count; i++)
            {
                VisualElement child = BuildLayoutPreviewNode(
                    _currentLayoutTree.Children[i],
                    0,
                    0,
                    PsdUiToolkitFlowChildPlacement.Absolute,
                    scale);
                if (child != null)
                    _layoutCanvasRoot.Add(child);
            }
        }

        private VisualElement BuildLayoutPreviewNode(
            PsdUiToolkitLayoutNode node,
            int parentLeft,
            int parentTop,
            PsdUiToolkitFlowChildPlacement placement,
            float scale)
        {
            if (node == null)
                return null;

            Layer layer = node.SourceLayer;
            VisualElement element = new VisualElement
            {
                name = string.IsNullOrEmpty(node.DisplayName)
                    ? (layer?.Name ?? "LayoutNode")
                    : node.DisplayName,
            };
            PsdUiToolkitLayerBounds bounds = node.Bounds;
            Vector2 previewSize = _layoutPreviewSizeOverrides.TryGetValue(
                node.Reference,
                out Vector2 overriddenSize)
                    ? overriddenSize
                    : new Vector2(bounds.Width, bounds.Height);
            element.style.width = Math.Max(0f, previewSize.x * scale);
            element.style.height = Math.Max(0f, previewSize.y * scale);

            if (placement.UseFlow)
            {
                element.style.position = Position.Relative;
                element.style.marginLeft = placement.MarginLeft * scale;
                element.style.marginTop = placement.MarginTop * scale;
                element.style.flexShrink = 0f;
            }
            else
            {
                element.style.position = Position.Absolute;
                element.style.left = (bounds.Left - parentLeft) * scale;
                element.style.top = (bounds.Top - parentTop) * scale;
            }

            if (layer != null)
            {
                element.style.opacity = layer.OpacityFloat;
                element.style.display = _configMap.IsVisible(layer)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
            if (!ShouldShowButtonPreviewNode(node.Reference))
                element.style.display = DisplayStyle.None;

            PsdUiToolkitFlowContainerPlan flowPlan =
                PsdUiToolkitFlowLayoutResolver.Resolve(node, _configMap);
            ApplyLayoutPreviewContainerStyle(element, flowPlan, scale);
            AddLayoutPreviewContent(element, node);

            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                if (node.SourceLayer != null)
                    SelectLayer(node.SourceLayer, true, evt.ctrlKey || evt.commandKey);
                else if (!string.IsNullOrEmpty(node.VirtualGroupId))
                    SelectVirtualGroup(node.VirtualGroupId);
                evt.StopPropagation();
            });

            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode childNode = node.Children[i];
                PsdUiToolkitFlowChildPlacement childPlacement =
                    flowPlan.UseFlow
                    && flowPlan.Placements.TryGetValue(
                        childNode,
                        out PsdUiToolkitFlowChildPlacement resolved)
                        ? resolved
                        : PsdUiToolkitFlowChildPlacement.Absolute;
                VisualElement child = BuildLayoutPreviewNode(
                    childNode,
                    bounds.Left,
                    bounds.Top,
                    childPlacement,
                    scale);
                if (child != null)
                    element.Add(child);
            }

            ApplyLayoutPreviewOutline(element, node);
            AddLayoutRoleBadge(element, node);
            if (flowPlan.UseFlow
                && flowPlan.WrapMode == PsdUiToolkitWrapMode.Wrap)
            {
                AddLayoutPreviewResizeHandle(
                    element,
                    node.Reference,
                    previewSize,
                    scale);
            }
            return element;
        }

        private void AddLayoutPreviewResizeHandle(
            VisualElement container,
            PsdUiToolkitNodeReference reference,
            Vector2 initialSize,
            float scale)
        {
            if (container == null || !reference.IsValid || scale <= 0f)
                return;

            VisualElement handle = new VisualElement
            {
                tooltip = "Drag to resize this Wrap preview container. This is session-only and is not saved.",
            };
            handle.style.position = Position.Absolute;
            handle.style.right = 0f;
            handle.style.bottom = 0f;
            handle.style.width = 12f;
            handle.style.height = 12f;
            handle.style.backgroundColor = new Color(1f, 0.62f, 0.12f, 0.95f);
            handle.style.borderLeftWidth = 1f;
            handle.style.borderTopWidth = 1f;
            handle.style.borderLeftColor = Color.black;
            handle.style.borderTopColor = Color.black;

            Vector2 pointerStart = Vector2.zero;
            Vector2 sizeStart = initialSize;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                pointerStart = evt.position;
                sizeStart = _layoutPreviewSizeOverrides.TryGetValue(
                    reference,
                    out Vector2 current)
                        ? current
                        : initialSize;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!handle.HasPointerCapture(evt.pointerId))
                    return;
                Vector2 delta = ((Vector2)evt.position - pointerStart) / scale;
                Vector2 next = new Vector2(
                    Mathf.Max(16f, sizeStart.x + delta.x),
                    Mathf.Max(16f, sizeStart.y + delta.y));
                _layoutPreviewSizeOverrides[reference] = next;
                container.style.width = next.x * scale;
                container.style.height = next.y * scale;
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!handle.HasPointerCapture(evt.pointerId))
                    return;
                handle.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
            container.Add(handle);
        }

        private bool ShouldShowButtonPreviewNode(
            PsdUiToolkitNodeReference reference)
        {
            if (!reference.IsValid || _configData?.buttons == null)
                return true;

            for (int buttonIndex = 0;
                buttonIndex < _configData.buttons.Length;
                buttonIndex++)
            {
                PsdUiToolkitButtonSemanticConfig button =
                    _configData.buttons[buttonIndex];
                if (button == null || button.states == null)
                    continue;

                bool isBoundState = false;
                for (int stateIndex = 0;
                    stateIndex < button.states.Length;
                    stateIndex++)
                {
                    PsdUiToolkitButtonStateBinding binding =
                        button.states[stateIndex];
                    if (binding != null && binding.source.Equals(reference))
                    {
                        isBoundState = true;
                        break;
                    }
                }
                if (!isBoundState)
                    continue;

                if (!button.TryGetState(
                    _previewButtonState,
                    out PsdUiToolkitNodeReference active)
                    && !button.TryGetState(
                        PsdUiToolkitButtonVisualState.Normal,
                        out active))
                {
                    return false;
                }
                return active.Equals(reference);
            }

            return true;
        }

        private static void ApplyLayoutPreviewContainerStyle(
            VisualElement element,
            PsdUiToolkitFlowContainerPlan plan,
            float scale)
        {
            if (element == null || plan == null || !plan.UseFlow)
                return;

            element.style.flexDirection = plan.LayoutType == PsdUiToolkitLayoutType.Row
                ? FlexDirection.Row
                : FlexDirection.Column;
            element.style.justifyContent = ResolvePreviewJustify(plan.MainAxisDistribution);
            element.style.alignItems = ResolvePreviewAlign(plan.CrossAxisAlignment);
            element.style.flexWrap = plan.WrapMode == PsdUiToolkitWrapMode.Wrap
                ? UnityEngine.UIElements.Wrap.Wrap
                : UnityEngine.UIElements.Wrap.NoWrap;
            element.style.alignContent = ResolvePreviewMultiLine(
                plan.MultiLineDistribution);
            element.style.paddingLeft = plan.PaddingLeft * scale;
            element.style.paddingTop = plan.PaddingTop * scale;
            element.style.paddingRight = plan.PaddingRight * scale;
            element.style.paddingBottom = plan.PaddingBottom * scale;
        }

        private static Justify ResolvePreviewJustify(
            PsdUiToolkitMainAxisDistribution distribution)
        {
            switch (distribution)
            {
                case PsdUiToolkitMainAxisDistribution.Center:
                    return Justify.Center;
                case PsdUiToolkitMainAxisDistribution.End:
                    return Justify.FlexEnd;
                case PsdUiToolkitMainAxisDistribution.SpaceBetween:
                    return Justify.SpaceBetween;
                case PsdUiToolkitMainAxisDistribution.SpaceAround:
                    return Justify.SpaceAround;
                default:
                    return Justify.FlexStart;
            }
        }

        private static Align ResolvePreviewAlign(PsdUiToolkitCrossAxisAlignment alignment)
        {
            switch (alignment)
            {
                case PsdUiToolkitCrossAxisAlignment.Center:
                    return Align.Center;
                case PsdUiToolkitCrossAxisAlignment.End:
                    return Align.FlexEnd;
                default:
                    return Align.FlexStart;
            }
        }

        private static Align ResolvePreviewMultiLine(
            PsdUiToolkitMultiLineDistribution distribution)
        {
            switch (distribution)
            {
                case PsdUiToolkitMultiLineDistribution.Center:
                    return Align.Center;
                case PsdUiToolkitMultiLineDistribution.End:
                    return Align.FlexEnd;
                default:
                    return Align.FlexStart;
            }
        }

        private void AddLayoutPreviewContent(
            VisualElement element,
            PsdUiToolkitLayoutNode node)
        {
            Layer layer = node.SourceLayer;
            if (layer == null || node.Children.Count > 0)
                return;

            if (layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                Label text = new Label(typeLayer.Text ?? string.Empty)
                {
                    pickingMode = PickingMode.Ignore,
                };
                text.style.position = Position.Absolute;
                text.style.left = 0f;
                text.style.top = 0f;
                text.style.right = 0f;
                text.style.bottom = 0f;
                text.style.unityTextAlign = TextAnchor.MiddleCenter;
                text.style.whiteSpace = WhiteSpace.NoWrap;
                text.style.fontSize = Math.Max(6f, typeLayer.EffectiveFontSize
                    * _layoutCanvasDrawWidth / Math.Max(1, _psd.Width));
                element.Add(text);
                return;
            }

            Texture2D texture = GetLayoutPreviewTexture(layer);
            if (texture != null)
            {
                Image image = new Image
                {
                    image = texture,
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore,
                };
                image.style.position = Position.Absolute;
                image.style.left = 0f;
                image.style.top = 0f;
                image.style.right = 0f;
                image.style.bottom = 0f;
                element.Add(image);
                return;
            }

            Label placeholder = new Label(node.DisplayName)
            {
                pickingMode = PickingMode.Ignore,
            };
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 0f;
            placeholder.style.top = 0f;
            placeholder.style.right = 0f;
            placeholder.style.bottom = 0f;
            placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            placeholder.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.45f);
            element.Add(placeholder);
        }

        private Texture2D GetLayoutPreviewTexture(Layer layer)
        {
            if (layer?.LayerId == null || _psd == null)
                return null;
            int layerId = layer.LayerId.Value;
            if (_layoutPreviewTextures.TryGetValue(layerId, out Texture2D cached))
                return cached;

            Texture2D texture = null;
            try
            {
                texture = PsdUiToolkitRasterExporter.CreatePreviewTexture(_psd, layer);
                if (texture != null)
                    texture.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[PsdUiToolkit] Failed to build layout preview for '{layer.Name}': {ex.Message}");
            }
            finally
            {
                _psd.ClearDecompressedCaches();
            }

            _layoutPreviewTextures[layerId] = texture;
            return texture;
        }

        private void ApplyLayoutPreviewOutline(
            VisualElement element,
            PsdUiToolkitLayoutNode node)
        {
            Color color = Color.clear;
            float width = 0f;
            bool selected = node.SourceLayer != null
                ? _selectedLayers.Contains(node.SourceLayer)
                : _selectedVirtualGroup?.id == node.VirtualGroupId;
            if (selected)
            {
                color = new Color(0.2f, 0.5f, 1f, 1f);
                width = 2f;
            }
            else if (NodeHasWarning(node, node.DisplayName))
            {
                color = new Color(1f, 0.68f, 0.15f, 1f);
                width = 2f;
            }
            else if (node.IsSynthetic)
            {
                color = new Color(0.3f, 0.85f, 0.55f, 0.9f);
                width = 1f;
            }

            if (width <= 0f)
                return;

            VisualElement outline = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            outline.style.position = Position.Absolute;
            outline.style.left = 0f;
            outline.style.top = 0f;
            outline.style.right = 0f;
            outline.style.bottom = 0f;
            outline.style.borderLeftWidth = width;
            outline.style.borderRightWidth = width;
            outline.style.borderTopWidth = width;
            outline.style.borderBottomWidth = width;
            outline.style.borderLeftColor = color;
            outline.style.borderRightColor = color;
            outline.style.borderTopColor = color;
            outline.style.borderBottomColor = color;
            element.Add(outline);
        }

        private static void AddLayoutRoleBadge(
            VisualElement element,
            PsdUiToolkitLayoutNode node)
        {
            string text = node.ItemRole == PsdUiToolkitItemRole.Background
                ? "BG"
                : (node.ItemRole == PsdUiToolkitItemRole.KeepAbsolute ? "FLOAT" : string.Empty);
            if (string.IsNullOrEmpty(text))
                return;

            Label badge = new Label(text)
            {
                pickingMode = PickingMode.Ignore,
            };
            badge.style.position = Position.Absolute;
            badge.style.left = 1f;
            badge.style.top = 1f;
            badge.style.fontSize = 8f;
            badge.style.color = Color.white;
            badge.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.78f);
            element.Add(badge);
        }

        private void DestroyLayoutPreviewCache()
        {
            foreach (KeyValuePair<int, Texture2D> entry in _layoutPreviewTextures)
            {
                if (entry.Value != null)
                    Object.DestroyImmediate(entry.Value);
            }

            _layoutPreviewTextures.Clear();
            _layoutCanvasRoot = null;
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

            Dictionary<string, string> copyPlan = null;
            try
            {
                copyPlan = PsdUiToolkitExporter.BuildEditableCopyPlan(
                    generatedPath,
                    editablePath);
            }
            catch
            {
                // The exporter below reports the actionable missing-draft error.
            }
            List<string> existingFiles = new List<string>();
            if (copyPlan != null)
            {
                foreach (string target in copyPlan.Values)
                {
                    if (File.Exists(PsdUiToolkitAssetPathUtility.GetDiskPath(target)))
                        existingFiles.Add(target);
                }
            }
            if (!recreate && existingFiles.Count > 0 && existingEditable == null)
            {
                EditorUtility.DisplayDialog(
                    "Editable Assets Already Exist",
                    "An editable UXML or USS file already exists, so no files were overwritten. Use Recreate Editable to replace the complete asset family.",
                    "OK");
                return;
            }
            if (recreate && existingFiles.Count > 0
                && !EditorUtility.DisplayDialog(
                    "Recreate Editable Asset Family",
                    "The following UI Builder files will be replaced:\n\n"
                    + string.Join("\n", existingFiles)
                    + "\n\nAll changes in these files will be lost.",
                    "Replace All",
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

                UpdateStatus(existingFiles.Count == 0
                    ? $"Created editable asset family: {resultPath}"
                    : $"Recreated editable asset family: {resultPath}");
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

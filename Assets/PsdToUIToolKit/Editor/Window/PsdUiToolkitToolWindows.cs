using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PsdTools.UIToolKit
{
    internal sealed class PsdUiToolkitDedupSettingsWindow : EditorWindow
    {
        private Slider _maeThresholdField;
        private SliderInt _fingerprintSizeField;

        [MenuItem("Tools/PSD/UI Toolkit/Dedup settings...")]
        public static void Open()
        {
            PsdUiToolkitDedupSettingsWindow window = GetWindow<PsdUiToolkitDedupSettingsWindow>("UI Toolkit dedup settings");
            window.minSize = new Vector2(440f, 330f);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10f;
            rootVisualElement.style.paddingRight = 10f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;

            PsdUiToolkitDedupConfigData data = PsdUiToolkitImageExportConfig.LoadDedup(true);
            rootVisualElement.Add(CreateTitle("Image deduplication"));
            rootVisualElement.Add(new HelpBox(
                "These values are used by the PsdToUIToolKit exporter. MAE compares premultiplied RGBA fingerprints; lower values are stricter.",
                HelpBoxMessageType.Info));

            _maeThresholdField = new Slider(
                "MAE threshold",
                PsdUiToolkitDedupConfigData.MinMaeThreshold,
                PsdUiToolkitDedupConfigData.MaxMaeThreshold)
            {
                value = data.maeThreshold,
                showInputField = true,
            };
            _maeThresholdField.style.marginTop = 10f;
            _maeThresholdField.RegisterValueChangedCallback(_ => Save());
            rootVisualElement.Add(_maeThresholdField);

            _fingerprintSizeField = new SliderInt(
                "Fingerprint size (N x N)",
                PsdUiToolkitDedupConfigData.MinFingerprintSize,
                PsdUiToolkitDedupConfigData.MaxFingerprintSize)
            {
                value = data.fingerprintSize,
                showInputField = true,
            };
            _fingerprintSizeField.style.marginTop = 8f;
            _fingerprintSizeField.RegisterValueChangedCallback(_ => Save());
            rootVisualElement.Add(_fingerprintSizeField);
            rootVisualElement.Add(new HelpBox(
                "Larger fingerprints preserve more detail but cost more comparison time. Changes apply on the next export.",
                HelpBoxMessageType.None));

            Button restoreButton = new Button(RestoreDefaults) { text = "Restore defaults" };
            restoreButton.style.height = 28f;
            restoreButton.style.marginTop = 12f;
            rootVisualElement.Add(restoreButton);
            rootVisualElement.Add(CreatePathLabel(PsdUiToolkitImageExportConfig.DedupConfigPath));
        }

        private void Save()
        {
            PsdUiToolkitImageExportConfig.SaveDedup(new PsdUiToolkitDedupConfigData
            {
                maeThreshold = _maeThresholdField.value,
                fingerprintSize = _fingerprintSizeField.value,
            });
        }

        private void RestoreDefaults()
        {
            _maeThresholdField.SetValueWithoutNotify(PsdUiToolkitDedupConfigData.DefaultMaeThreshold);
            _fingerprintSizeField.SetValueWithoutNotify(PsdUiToolkitDedupConfigData.DefaultFingerprintSize);
            Save();
        }

        private static Label CreateTitle(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14f,
                    marginBottom = 6f,
                },
            };
        }

        private static Label CreatePathLabel(string path)
        {
            return new Label($"Config: {path}")
            {
                style =
                {
                    fontSize = 10f,
                    whiteSpace = WhiteSpace.Normal,
                    marginTop = 8f,
                    opacity = 0.65f,
                },
            };
        }

        internal static Label ToolTitle(string text) => CreateTitle(text);
        internal static Label ConfigPathLabel(string path) => CreatePathLabel(path);
    }

    internal sealed class PsdUiToolkitNineSliceSettingsWindow : EditorWindow
    {
        private IntegerField _borderInsetField;
        private IntegerField _pixelThresholdField;
        private IntegerField _minCenterColsField;
        private IntegerField _minCenterRowsField;
        private IntegerField _minSameZoneField;

        [MenuItem("Tools/PSD/UI Toolkit/Nine-slice settings...")]
        public static void Open()
        {
            PsdUiToolkitNineSliceSettingsWindow window = GetWindow<PsdUiToolkitNineSliceSettingsWindow>("UI Toolkit nine-slice settings");
            window.minSize = new Vector2(460f, 430f);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10f;
            rootVisualElement.style.paddingRight = 10f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;

            PsdUiToolkitNineSliceConfigData data = PsdUiToolkitImageExportConfig.LoadNineSlice(true);
            rootVisualElement.Add(PsdUiToolkitDedupSettingsWindow.ToolTitle("Nine-slice detection and export"));

            _borderInsetField = AddIntegerField("Border inset", data.borderInset);
            rootVisualElement.Add(new HelpBox("Keeps cut lines away from color boundaries.", HelpBoxMessageType.None));
            _pixelThresholdField = AddIntegerField("Adjacent pixel threshold", data.pixelThreshold);
            rootVisualElement.Add(new HelpBox("Maximum per-channel RGBA difference (0-255) for adjacent rows or columns to remain cuttable.", HelpBoxMessageType.None));
            _minSameZoneField = AddIntegerField("Minimum contiguous cut zone", data.minSameZone);
            _minCenterColsField = AddIntegerField("Maximum center columns", data.minCenterCols);
            _minCenterRowsField = AddIntegerField("Maximum center rows", data.minCenterRows);
            rootVisualElement.Add(new HelpBox("The exported center block is capped by these row and column values.", HelpBoxMessageType.None));

            Button restoreButton = new Button(RestoreDefaults) { text = "Restore defaults" };
            restoreButton.style.height = 28f;
            restoreButton.style.marginTop = 12f;
            rootVisualElement.Add(restoreButton);
            rootVisualElement.Add(PsdUiToolkitDedupSettingsWindow.ConfigPathLabel(PsdUiToolkitImageExportConfig.NineSliceConfigPath));
        }

        private IntegerField AddIntegerField(string label, int value)
        {
            IntegerField field = new IntegerField(label) { value = value };
            field.style.marginTop = 6f;
            field.RegisterValueChangedCallback(_ => Save());
            rootVisualElement.Add(field);
            return field;
        }

        private void Save()
        {
            PsdUiToolkitNineSliceConfigData data = new PsdUiToolkitNineSliceConfigData
            {
                borderInset = _borderInsetField.value,
                pixelThreshold = _pixelThresholdField.value,
                minCenterCols = _minCenterColsField.value,
                minCenterRows = _minCenterRowsField.value,
                minSameZone = _minSameZoneField.value,
            };
            PsdUiToolkitImageExportConfig.SaveNineSlice(data);
            _borderInsetField.SetValueWithoutNotify(data.borderInset);
            _pixelThresholdField.SetValueWithoutNotify(data.pixelThreshold);
            _minCenterColsField.SetValueWithoutNotify(data.minCenterCols);
            _minCenterRowsField.SetValueWithoutNotify(data.minCenterRows);
            _minSameZoneField.SetValueWithoutNotify(data.minSameZone);
        }

        private void RestoreDefaults()
        {
            _borderInsetField.SetValueWithoutNotify(PsdUiToolkitNineSliceConfigData.DefaultBorderInset);
            _pixelThresholdField.SetValueWithoutNotify(PsdUiToolkitNineSliceConfigData.DefaultPixelThreshold);
            _minCenterColsField.SetValueWithoutNotify(PsdUiToolkitNineSliceConfigData.DefaultMinCenterCols);
            _minCenterRowsField.SetValueWithoutNotify(PsdUiToolkitNineSliceConfigData.DefaultMinCenterRows);
            _minSameZoneField.SetValueWithoutNotify(PsdUiToolkitNineSliceConfigData.DefaultMinSameZone);
            Save();
        }
    }

    internal sealed class PsdUiToolkitFontMappingWindow : EditorWindow
    {
        private readonly List<PsdUiToolkitFontMappingEntry> _entries = new List<PsdUiToolkitFontMappingEntry>();
        private ScrollView _rowsContainer;
        private Label _statusLabel;

        [MenuItem("Tools/PSD/UI Toolkit/Font mapping...")]
        public static void Open()
        {
            PsdUiToolkitFontMappingWindow window = GetWindow<PsdUiToolkitFontMappingWindow>("UI Toolkit font mapping");
            window.minSize = new Vector2(620f, 320f);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10f;
            rootVisualElement.style.paddingRight = 10f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;

            rootVisualElement.Add(PsdUiToolkitDedupSettingsWindow.ToolTitle("PSD font mapping for UI Toolkit"));
            rootVisualElement.Add(new HelpBox(
                "Map Photoshop font names to a Unity Font or TextCore FontAsset. Exports use -unity-font-definition; blank mappings use the UI Toolkit default font.",
                HelpBoxMessageType.Info));

            VisualElement toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8f, marginBottom = 6f } };
            toolbar.Add(new Button(Reload) { text = "Reload from disk" });
            Button addButton = new Button(AddRow) { text = "Add row" };
            addButton.style.marginLeft = 6f;
            toolbar.Add(addButton);
            rootVisualElement.Add(toolbar);

            VisualElement header = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Label nameHeader = new Label("PSD font name") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1f, flexBasis = 0f } };
            Label assetHeader = new Label("UI Toolkit Font / FontAsset") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1.4f, flexBasis = 0f, marginLeft = 8f } };
            header.Add(nameHeader);
            header.Add(assetHeader);
            header.Add(new VisualElement { style = { width = 58f } });
            rootVisualElement.Add(header);

            _rowsContainer = new ScrollView();
            _rowsContainer.style.flexGrow = 1f;
            _rowsContainer.style.marginTop = 4f;
            rootVisualElement.Add(_rowsContainer);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 4f;
            rootVisualElement.Add(_statusLabel);
            rootVisualElement.Add(PsdUiToolkitDedupSettingsWindow.ConfigPathLabel(PsdUiToolkitFontMappingConfig.ConfigPath));

            Reload();
        }

        private void Reload()
        {
            _entries.Clear();
            PsdUiToolkitFontMappingData data = PsdUiToolkitFontMappingConfig.Load(true);
            if (data.entries != null)
            {
                foreach (PsdUiToolkitFontMappingEntry entry in data.entries)
                {
                    if (entry == null)
                        continue;
                    _entries.Add(new PsdUiToolkitFontMappingEntry
                    {
                        psdFontName = entry.psdFontName,
                        fontAssetPath = entry.fontAssetPath,
                    });
                }
            }

            RebuildRows();
            UpdateStatus("Loaded from disk.");
        }

        private void AddRow()
        {
            _entries.Add(new PsdUiToolkitFontMappingEntry());
            SaveAndRebuild();
        }

        private void RebuildRows()
        {
            _rowsContainer?.Clear();
            if (_rowsContainer == null)
                return;

            foreach (PsdUiToolkitFontMappingEntry entry in _entries)
            {
                VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4f } };

                TextField nameField = new TextField { value = entry.psdFontName };
                nameField.style.flexGrow = 1f;
                nameField.style.flexBasis = 0f;
                nameField.RegisterValueChangedCallback(evt =>
                {
                    entry.psdFontName = evt.newValue ?? string.Empty;
                    Save();
                });
                row.Add(nameField);

                Object currentAsset = PsdUiToolkitFontMappingConfig.LoadSupportedFontAsset(entry.fontAssetPath);
                ObjectField fontField = new ObjectField
                {
                    objectType = typeof(Object),
                    allowSceneObjects = false,
                    value = currentAsset,
                };
                fontField.style.flexGrow = 1.4f;
                fontField.style.flexBasis = 0f;
                fontField.style.marginLeft = 8f;
                fontField.RegisterValueChangedCallback(evt =>
                {
                    if (!PsdUiToolkitFontMappingConfig.IsSupportedFontAsset(evt.newValue))
                    {
                        fontField.SetValueWithoutNotify(evt.previousValue);
                        UpdateStatus("Only Unity Font and TextCore FontAsset assets are supported.", true);
                        return;
                    }

                    entry.fontAssetPath = evt.newValue == null ? string.Empty : AssetDatabase.GetAssetPath(evt.newValue);
                    Save();
                });
                row.Add(fontField);

                Button deleteButton = new Button(() =>
                {
                    _entries.Remove(entry);
                    SaveAndRebuild();
                })
                {
                    text = "Delete",
                };
                deleteButton.style.width = 56f;
                deleteButton.style.marginLeft = 4f;
                row.Add(deleteButton);
                _rowsContainer.Add(row);
            }
        }

        private void SaveAndRebuild()
        {
            Save();
            RebuildRows();
        }

        private void Save()
        {
            PsdUiToolkitFontMappingConfig.Save(new PsdUiToolkitFontMappingData { entries = _entries.ToArray() });
            UpdateStatus($"Saved {_entries.Count} mapping(s).");
        }

        private void UpdateStatus(string message, bool error = false)
        {
            if (_statusLabel == null)
                return;
            _statusLabel.text = message;
            if (error)
                _statusLabel.style.color = new Color(0.95f, 0.4f, 0.35f);
            else
                _statusLabel.style.color = StyleKeyword.Null;
        }
    }

    internal sealed class PsdUiToolkitDedupTestWindow : EditorWindow
    {
        private string _pathA = string.Empty;
        private string _pathB = string.Empty;
        private Texture2D _textureA;
        private Texture2D _textureB;
        private Image _previewA;
        private Image _previewB;
        private Button _chooseAButton;
        private Button _chooseBButton;
        private Button _compareButton;
        private VisualElement _resultContainer;

        [MenuItem("Tools/PSD/UI Toolkit/Test: Dedup two images...")]
        public static void Open()
        {
            PsdUiToolkitDedupTestWindow window = GetWindow<PsdUiToolkitDedupTestWindow>("UI Toolkit dedup test");
            window.minSize = new Vector2(560f, 520f);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10f;
            rootVisualElement.style.paddingRight = 10f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;

            rootVisualElement.Add(PsdUiToolkitDedupSettingsWindow.ToolTitle("Image dedup test"));
            rootVisualElement.Add(new HelpBox(
                "Compares two PNG files with the same fingerprint and MAE implementation used by the PsdToUIToolKit exporter.",
                HelpBoxMessageType.Info));

            VisualElement pickers = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8f } };
            pickers.Add(BuildPicker("Image A", true));
            pickers.Add(BuildPicker("Image B", false));
            rootVisualElement.Add(pickers);

            _compareButton = new Button(Compare) { text = "Compare" };
            _compareButton.style.height = 30f;
            _compareButton.style.marginTop = 10f;
            _compareButton.SetEnabled(false);
            rootVisualElement.Add(_compareButton);

            _resultContainer = new ScrollView();
            _resultContainer.style.flexGrow = 1f;
            _resultContainer.style.marginTop = 8f;
            rootVisualElement.Add(_resultContainer);
        }

        private VisualElement BuildPicker(string label, bool isFirst)
        {
            VisualElement panel = new VisualElement();
            panel.style.flexGrow = 1f;
            panel.style.flexBasis = 0f;
            panel.style.marginRight = isFirst ? 4f : 0f;
            panel.style.marginLeft = isFirst ? 0f : 4f;
            panel.style.paddingLeft = 6f;
            panel.style.paddingRight = 6f;
            panel.style.paddingTop = 6f;
            panel.style.paddingBottom = 6f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            panel.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            panel.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            panel.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);

            panel.Add(new Label(label) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            Button chooseButton = new Button(() => PickImage(isFirst)) { text = "Choose PNG..." };
            chooseButton.style.marginTop = 4f;
            panel.Add(chooseButton);

            Image preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.style.height = 120f;
            preview.style.marginTop = 6f;
            preview.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            panel.Add(preview);

            if (isFirst)
            {
                _chooseAButton = chooseButton;
                _previewA = preview;
            }
            else
            {
                _chooseBButton = chooseButton;
                _previewB = preview;
            }

            return panel;
        }

        private void PickImage(bool isFirst)
        {
            string path = EditorUtility.OpenFilePanel("Select PNG", Application.dataPath, "png");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                Texture2D texture = LoadPng(path);
                if (isFirst)
                {
                    DestroyTexture(ref _textureA);
                    _textureA = texture;
                    _pathA = path;
                    _previewA.image = texture;
                    _chooseAButton.text = Path.GetFileName(path);
                }
                else
                {
                    DestroyTexture(ref _textureB);
                    _textureB = texture;
                    _pathB = path;
                    _previewB.image = texture;
                    _chooseBButton.text = Path.GetFileName(path);
                }

                _resultContainer.Clear();
                _compareButton.SetEnabled(_textureA != null && _textureB != null);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void Compare()
        {
            _resultContainer.Clear();
            try
            {
                PsdUiToolkitDedupConfigData config = PsdUiToolkitImageExportConfig.LoadDedup(true);
                float[] fingerprintA = PsdUiToolkitRasterExporter.ComputeFingerprint(_textureA, config.fingerprintSize);
                float[] fingerprintB = PsdUiToolkitRasterExporter.ComputeFingerprint(_textureB, config.fingerprintSize);
                float mae = PsdUiToolkitRasterExporter.CalculateFingerprintMae(fingerprintA, fingerprintB);
                bool wouldDedup = mae <= config.maeThreshold;

                _resultContainer.Add(new HelpBox(
                    wouldDedup ? "WOULD DEDUP — images are treated as identical." : "WOULD NOT DEDUP — images are distinct.",
                    wouldDedup ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
                _resultContainer.Add(new Label(
                    $"Config: MAE threshold={config.maeThreshold:F4}, fingerprint={config.fingerprintSize}x{config.fingerprintSize}\n" +
                    $"Image A: {_textureA.width}x{_textureA.height} ({_pathA})\n" +
                    $"Image B: {_textureB.width}x{_textureB.height} ({_pathB})\n" +
                    $"MAE: {mae:F6} — {(wouldDedup ? "pass" : "fail")}")
                {
                    style = { whiteSpace = WhiteSpace.Normal, marginTop = 6f },
                });

                float[] differences = new float[fingerprintA.Length];
                int[] order = new int[fingerprintA.Length];
                for (int i = 0; i < fingerprintA.Length; i++)
                {
                    differences[i] = Mathf.Abs(fingerprintA[i] - fingerprintB[i]);
                    order[i] = i;
                }
                Array.Sort(order, (left, right) => differences[right].CompareTo(differences[left]));

                _resultContainer.Add(new Label("Largest fingerprint channel differences")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8f },
                });
                string[] channels = { "R", "G", "B", "A" };
                for (int rank = 0; rank < Mathf.Min(8, order.Length); rank++)
                {
                    int index = order[rank];
                    int pixel = index / 4;
                    int x = pixel % config.fingerprintSize;
                    int y = pixel / config.fingerprintSize;
                    _resultContainer.Add(new Label(
                        $"[{index:D4}] pixel({x},{y}) {channels[index % 4]}  " +
                        $"A={fingerprintA[index]:F4}  B={fingerprintB[index]:F4}  |diff|={differences[index]:F4}"));
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ShowError(string message)
        {
            _resultContainer?.Clear();
            _resultContainer?.Add(new HelpBox(message, HelpBoxMessageType.Error));
        }

        private static Texture2D LoadPng(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (texture.LoadImage(File.ReadAllBytes(path)))
                return texture;

            DestroyImmediate(texture);
            throw new InvalidOperationException($"Failed to load image: {path}");
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
                return;
            DestroyImmediate(texture);
            texture = null;
        }

        private void OnDisable()
        {
            DestroyTexture(ref _textureA);
            DestroyTexture(ref _textureB);
        }
    }
}

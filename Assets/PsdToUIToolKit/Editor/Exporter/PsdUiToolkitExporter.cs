using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PsdTools.Layers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PsdTools.UIToolKit
{
    internal readonly struct PsdUiToolkitLayerBounds
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Width;
        public readonly int Height;

        public PsdUiToolkitLayerBounds(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
    }

    internal sealed class PsdUiToolkitRasterAssetInfo
    {
        public int LayerId;
        public string AssetPath;
        public string StyleImageUri;
        public int Width;
        public int Height;
        public Vector4? SliceBorder;
    }

    internal sealed class PsdUiToolkitRasterExportResult
    {
        public Dictionary<int, PsdUiToolkitRasterAssetInfo> AssetsByLayerId { get; } = new Dictionary<int, PsdUiToolkitRasterAssetInfo>();
        public HashSet<int> SuppressedLayerIds { get; } = new HashSet<int>();
        public HashSet<int> CompositeLeafLayerIds { get; } = new HashSet<int>();
    }

    public sealed class PsdUiToolkitExportArtifacts
    {
        public string ImageFolderAssetPath { get; internal set; }
        public string UxmlAssetPath { get; internal set; }
    }

    internal static class PsdUiToolkitAssetPathUtility
    {
        public static string NormalizeAssetsPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return "Assets";

            string normalized = assetPath.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                normalized = "Assets/" + normalized.TrimStart('/');

            while (normalized.Contains("//", StringComparison.Ordinal))
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

            return normalized.TrimEnd('/');
        }

        public static string CombineAssetsPath(string left, string right)
        {
            string normalizedLeft = NormalizeAssetsPath(left);
            string normalizedRight = (right ?? string.Empty).Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(normalizedRight)
                ? normalizedLeft
                : normalizedLeft + "/" + normalizedRight;
        }

        public static string GetDiskPath(string assetPath)
        {
            string normalized = NormalizeAssetsPath(assetPath);
            if (string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
                return Application.dataPath;

            string suffix = normalized.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Application.dataPath, suffix);
        }

        public static bool TryConvertDiskPathToAssetPath(string diskPath, out string assetPath)
        {
            assetPath = null;
            if (string.IsNullOrWhiteSpace(diskPath))
                return false;

            string fullPath = Path.GetFullPath(diskPath).Replace('\\', '/');
            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!fullPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(fullPath, dataPath, StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets";
                return true;
            }

            assetPath = NormalizeAssetsPath("Assets" + fullPath.Substring(dataPath.Length));
            return true;
        }

        public static void EnsureAssetDirectoryExists(string assetDirectoryPath)
        {
            string diskPath = GetDiskPath(assetDirectoryPath);
            if (!Directory.Exists(diskPath))
                Directory.CreateDirectory(diskPath);
        }

        public static void EnsureParentDirectoryForFile(string assetFilePath)
        {
            string directory = Path.GetDirectoryName(NormalizeAssetsPath(assetFilePath))?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory))
                EnsureAssetDirectoryExists(directory);
        }

        public static string BuildProjectDatabaseUri(Object asset)
        {
            if (asset == null)
                return string.Empty;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                return string.Empty;

            string escapedPath = string.Join("/", assetPath.Split('/').Select(Uri.EscapeDataString));
            string escapedFragment = Uri.EscapeDataString(asset.name ?? string.Empty);
            return $"project://database/{escapedPath}?fileID={localId}&guid={guid}&type=3#{escapedFragment}";
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "layer";

            StringBuilder builder = new StringBuilder(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char ch in value)
            {
                if (invalid.Contains(ch))
                    builder.Append('_');
                else if (char.IsWhiteSpace(ch))
                    builder.Append('_');
                else
                    builder.Append(ch);
            }

            string sanitized = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(sanitized) ? "layer" : sanitized;
        }
    }

    internal sealed class PsdUiToolkitRasterExporter
    {
        private readonly struct CommonDirImageEntry
        {
            public readonly string AssetPath;
            public readonly float[] Fingerprint;
            public readonly int Width;
            public readonly int Height;

            public CommonDirImageEntry(string assetPath, float[] fingerprint, int width, int height)
            {
                AssetPath = assetPath;
                Fingerprint = fingerprint;
                Width = width;
                Height = height;
            }
        }

        private readonly struct LocalDedupExportSpec
        {
            public readonly bool SliceEnabled;
            public readonly bool ParticipateCommonDedup;
            public readonly int BorderInset;
            public readonly int PixelThreshold;
            public readonly int MinCenterCols;
            public readonly int MinCenterRows;
            public readonly int MinSameZone;

            public LocalDedupExportSpec(bool sliceEnabled, bool participateCommonDedup, int borderInset, int pixelThreshold, int minCenterCols, int minCenterRows, int minSameZone)
            {
                SliceEnabled = sliceEnabled;
                ParticipateCommonDedup = participateCommonDedup;
                BorderInset = borderInset;
                PixelThreshold = pixelThreshold;
                MinCenterCols = minCenterCols;
                MinCenterRows = minCenterRows;
                MinSameZone = minSameZone;
            }
        }

        private sealed class LocalDedupUnionFind
        {
            private readonly int[] _parent;

            public LocalDedupUnionFind(int count)
            {
                _parent = new int[count];
                for (int i = 0; i < count; i++)
                    _parent[i] = i;
            }

            public int Find(int index)
            {
                if (_parent[index] != index)
                    _parent[index] = Find(_parent[index]);
                return _parent[index];
            }

            public void Union(int left, int right)
            {
                int rootLeft = Find(left);
                int rootRight = Find(right);
                if (rootLeft != rootRight)
                    _parent[rootRight] = rootLeft;
            }

            public List<List<int>> BuildGroups(int count)
            {
                Dictionary<int, List<int>> buckets = new Dictionary<int, List<int>>();
                for (int i = 0; i < count; i++)
                {
                    int root = Find(i);
                    if (!buckets.TryGetValue(root, out List<int> bucket))
                    {
                        bucket = new List<int>();
                        buckets[root] = bucket;
                    }

                    bucket.Add(i);
                }

                List<List<int>> groups = buckets.Values.ToList();
                groups.Sort((left, right) => left[0].CompareTo(right[0]));
                return groups;
            }
        }

        private readonly PsdImage _psd;
        private readonly PsdUiToolkitLayerConfigMap _configMap;
        private readonly string _imageFolderAssetPath;
        private readonly bool _autoImageNaming;
        private readonly Dictionary<string, int> _fileNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly PsdUiToolkitRasterExportResult _result = new PsdUiToolkitRasterExportResult();
        private readonly Dictionary<Layer, Texture2D> _rasterPending = new Dictionary<Layer, Texture2D>();
        private readonly List<Layer> _rasterPendingOrder = new List<Layer>();
        private readonly PsdUiToolkitNineSliceParams _defaultNineSliceParams;
        private readonly float _dedupMaeThreshold;
        private readonly int _dedupFingerprintSize;
        private List<CommonDirImageEntry> _commonDirImageCache;

        public PsdUiToolkitRasterExporter(PsdImage psd, PsdUiToolkitLayerConfigMap configMap, string imageFolderAssetPath, bool autoImageNaming)
        {
            _psd = psd;
            _configMap = configMap;
            _imageFolderAssetPath = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(imageFolderAssetPath);
            _autoImageNaming = autoImageNaming;

            PsdUiToolkitNineSliceConfigData nineSliceConfig = PsdUiToolkitImageExportConfig.LoadNineSlice(true);
            _defaultNineSliceParams = new PsdUiToolkitNineSliceParams
            {
                borderInset = Mathf.Max(0, nineSliceConfig.borderInset),
                pixelThreshold = Mathf.Clamp(nineSliceConfig.pixelThreshold, 0, 255),
                minCenterCols = Mathf.Max(1, nineSliceConfig.minCenterCols),
                minCenterRows = Mathf.Max(1, nineSliceConfig.minCenterRows),
                minSameZone = Mathf.Max(1, nineSliceConfig.minSameZone),
            };

            PsdUiToolkitDedupConfigData dedupConfig = PsdUiToolkitImageExportConfig.LoadDedup(true);
            _dedupMaeThreshold = Mathf.Clamp(
                dedupConfig.maeThreshold,
                PsdUiToolkitDedupConfigData.MinMaeThreshold,
                PsdUiToolkitDedupConfigData.MaxMaeThreshold);
            _dedupFingerprintSize = Mathf.Clamp(
                dedupConfig.fingerprintSize,
                PsdUiToolkitDedupConfigData.MinFingerprintSize,
                PsdUiToolkitDedupConfigData.MaxFingerprintSize);
        }

        public PsdUiToolkitRasterExportResult ExportAll()
        {
            BuildCommonDirImageCache();

            foreach (Layer child in _psd.Children)
                ExportLayerTree(child);

            ProcessPendingRasterExports();

            AssetDatabase.Refresh();
            SetupImportedSprites();
            AssetDatabase.Refresh();
            ResolveAssetReferences();
            return _result;
        }

        public static Texture2D CreatePreviewTexture(PsdImage psd, Layer layer)
        {
            if (psd == null || layer == null)
                return null;

            if (layer.IsGroup)
                return psd.CompositeGroupWithClipping((Group)layer);
            if (layer.Kind == LayerKind.Type)
                return null;
            if (!layer.HasPixels() && !LayerEffectsHelper.HasExtractableColor(layer))
                return null;
            return LayerEffectsHelper.CreateLayerTextureWithEffects(layer, true);
        }

        private void ExportLayerTree(Layer layer)
        {
            if (layer == null || !_configMap.IsExported(layer))
                return;

            if (layer.IsGroup)
            {
                Group group = (Group)layer;
                if (_configMap.IsMergeExport(layer))
                {
                    ExportGroupComposite(group, true);
                    return;
                }

                ExportGroupChildren(group);
                return;
            }

            if (layer.Kind == LayerKind.Type)
                return;

            ExportSingleLayer(layer);
        }

        private void ExportGroupChildren(Group group)
        {
            Layer currentBase = null;
            List<Layer> pendingClipped = new List<Layer>();

            for (int index = 0; index < group.Children.Count; index++)
            {
                Layer child = group.Children[index];
                if (!_configMap.IsExported(child))
                {
                    if (!child.IsClipped)
                    {
                        FinalizeCurrentBase(currentBase, pendingClipped);
                        pendingClipped.Clear();
                        currentBase = null;
                    }

                    continue;
                }

                if (child.IsClipped)
                {
                    pendingClipped.Add(child);
                }
                else
                {
                    FinalizeCurrentBase(currentBase, pendingClipped);
                    pendingClipped.Clear();
                    currentBase = child;
                }
            }

            FinalizeCurrentBase(currentBase, pendingClipped);
        }

        private void FinalizeCurrentBase(Layer baseLayer, List<Layer> clippedLayers)
        {
            if (baseLayer == null)
            {
                foreach (Layer orphan in clippedLayers)
                    ExportLayerTree(orphan);
                return;
            }

            if (baseLayer.IsGroup)
            {
                Group group = (Group)baseLayer;
                if (_configMap.IsMergeExport(baseLayer) || clippedLayers.Count > 0)
                {
                    ExportGroupComposite(group, true);
                    foreach (Layer clipped in clippedLayers)
                        SuppressLayer(clipped);
                    return;
                }

                ExportGroupChildren(group);
                return;
            }

            if (baseLayer.Kind == LayerKind.Type)
            {
                foreach (Layer clipped in clippedLayers)
                    SuppressLayer(clipped);
                return;
            }

            if (clippedLayers.Count > 0)
            {
                Texture2D merged = BuildMergedClippingTexture(baseLayer, clippedLayers);
                if (merged != null)
                {
                    QueueRasterForExport(baseLayer, merged);
                    foreach (Layer clipped in clippedLayers)
                        SuppressLayer(clipped);
                }

                return;
            }

            ExportSingleLayer(baseLayer);
        }

        private void ExportGroupComposite(Group group, bool markAsLeaf)
        {
            if (group?.LayerId == null)
                return;

            Texture2D texture = _psd.CompositeGroupWithClipping(group);
            if (texture == null)
                return;

            QueueRasterForExport(group, texture);
            if (markAsLeaf)
                _result.CompositeLeafLayerIds.Add(group.LayerId.Value);
        }

        private void ExportSingleLayer(Layer layer)
        {
            if (layer?.LayerId == null)
                return;
            if (!layer.HasPixels() && !LayerEffectsHelper.HasExtractableColor(layer))
                return;

            Texture2D texture = LayerEffectsHelper.CreateLayerTextureWithEffects(layer, true);
            if (texture == null)
                return;

            QueueRasterForExport(layer, texture);
        }

        private void QueueRasterForExport(Layer layer, Texture2D texture)
        {
            if (layer?.LayerId == null || texture == null)
            {
                if (texture != null)
                    Object.DestroyImmediate(texture);
                return;
            }

            if (_rasterPending.ContainsKey(layer))
            {
                Object.DestroyImmediate(texture);
                return;
            }

            _rasterPending[layer] = texture;
            _rasterPendingOrder.Add(layer);
        }

        private void ProcessPendingRasterExports()
        {
            if (_rasterPending.Count == 0)
                return;

            try
            {
                List<Layer> allLayers = new List<Layer>(_rasterPendingOrder);
                List<Layer> participators = new List<Layer>();
                List<Layer> nonParticipators = new List<Layer>();
                foreach (Layer layer in allLayers)
                {
                    if (_configMap.ParticipateLocalDedup(layer))
                        participators.Add(layer);
                    else
                        nonParticipators.Add(layer);
                }

                List<List<Layer>> clusters = BuildLocalDedupClusters(participators);
                List<(List<Layer> cluster, Layer imageSource, LocalDedupExportSpec exportSpec)> exportJobs = new List<(List<Layer> cluster, Layer imageSource, LocalDedupExportSpec exportSpec)>();

                foreach (List<Layer> cluster in clusters)
                {
                    if (!TryResolveVirtualLocalDedupGroup(cluster, out Layer imageSourceLayer, out LocalDedupExportSpec exportSpec, out string errorMessage))
                        throw new InvalidOperationException(errorMessage);

                    exportJobs.Add((cluster, imageSourceLayer, exportSpec));
                }

                foreach (Layer layer in nonParticipators)
                {
                    if (!TryResolveVirtualLocalDedupGroup(new List<Layer> { layer }, out Layer imageSourceLayer, out LocalDedupExportSpec exportSpec, out string errorMessage))
                        throw new InvalidOperationException(errorMessage);

                    exportJobs.Add((new List<Layer> { layer }, imageSourceLayer, exportSpec));
                }

                foreach ((List<Layer> cluster, Layer imageSource, LocalDedupExportSpec exportSpec) job in exportJobs)
                    SaveLayerTextureGrouped(_rasterPending[job.imageSource], job.imageSource, job.cluster, job.exportSpec);
            }
            finally
            {
                DisposeAllRasterPending();
            }
        }

        private List<List<Layer>> BuildLocalDedupClusters(List<Layer> participators)
        {
            List<List<Layer>> clusters = new List<List<Layer>>();
            int count = participators.Count;
            if (count == 0)
                return clusters;

            float[][] fingerprintCache = new float[count][];
            for (int i = 0; i < count; i++)
                fingerprintCache[i] = ComputeFingerprint(_rasterPending[participators[i]]);

            LocalDedupUnionFind unionFind = new LocalDedupUnionFind(count);
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (FingerprintsMatchLocal(fingerprintCache[i], fingerprintCache[j]))
                        unionFind.Union(i, j);
                }
            }

            foreach (List<int> indexGroup in unionFind.BuildGroups(count))
            {
                List<Layer> cluster = new List<Layer>(indexGroup.Count);
                foreach (int index in indexGroup)
                    cluster.Add(participators[index]);
                clusters.Add(cluster);
            }

            return clusters;
        }

        private bool TryResolveVirtualLocalDedupGroup(List<Layer> cluster, out Layer imageSourceLayer, out LocalDedupExportSpec exportSpec, out string errorMessage)
        {
            errorMessage = null;
            imageSourceLayer = null;
            exportSpec = default;

            if (cluster == null || cluster.Count == 0)
            {
                errorMessage = "Local dedup group is empty.";
                return false;
            }

            if (cluster.Count == 1)
            {
                imageSourceLayer = cluster[0];
                exportSpec = BuildLocalDedupExportSpec(cluster[0]);
                return true;
            }

            imageSourceLayer = PickLargestRasterSourceLayer(cluster);
            return TryBuildSyntheticLocalDedupExportSpec(cluster, out exportSpec, out errorMessage);
        }

        private bool TryBuildSyntheticLocalDedupExportSpec(List<Layer> cluster, out LocalDedupExportSpec exportSpec, out string errorMessage)
        {
            errorMessage = null;
            exportSpec = default;

            List<LocalDedupExportSpec> specs = new List<LocalDedupExportSpec>(cluster.Count);
            foreach (Layer layer in cluster)
                specs.Add(BuildLocalDedupExportSpec(layer));

            bool participateCommonDedup = specs.TrueForAll(spec => spec.ParticipateCommonDedup);
            bool anyNoSlice = specs.Exists(spec => !spec.SliceEnabled);
            if (anyNoSlice)
            {
                exportSpec = new LocalDedupExportSpec(false, participateCommonDedup, 0, 0, 0, 0, 0);
            }
            else
            {
                LocalDedupExportSpec first = specs[0];
                bool allSameNineSliceParams = specs.TrueForAll(spec =>
                    spec.BorderInset == first.BorderInset &&
                    spec.PixelThreshold == first.PixelThreshold &&
                    spec.MinCenterCols == first.MinCenterCols &&
                    spec.MinCenterRows == first.MinCenterRows &&
                    spec.MinSameZone == first.MinSameZone);
                if (!allSameNineSliceParams)
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("Local dedup detected identical raster content with conflicting nine-slice parameters:");
                    foreach (Layer layer in cluster)
                    {
                        LocalDedupExportSpec spec = BuildLocalDedupExportSpec(layer);
                        builder.Append("- ")
                            .Append(DescribeLayer(layer))
                            .Append(" -> ")
                            .AppendLine(FormatLocalDedupExportSpec(spec));
                    }

                    errorMessage = builder.ToString().TrimEnd();
                    return false;
                }

                exportSpec = new LocalDedupExportSpec(true, participateCommonDedup, first.BorderInset, first.PixelThreshold, first.MinCenterCols, first.MinCenterRows, first.MinSameZone);
            }

            foreach (LocalDedupExportSpec spec in specs)
            {
                if (!LocalDedupExportSpecCovers(exportSpec, spec))
                {
                    errorMessage = "Local dedup could not synthesize one export spec that covers every identical raster node.";
                    return false;
                }
            }

            return true;
        }

        private LocalDedupExportSpec BuildLocalDedupExportSpec(Layer layer)
        {
            bool sliceEnabled = _configMap.GetSliceImage(layer);
            bool participateCommonDedup = _configMap.ParticipateCommonDedup(layer);
            if (!sliceEnabled)
                return new LocalDedupExportSpec(false, participateCommonDedup, 0, 0, 0, 0, 0);

            PsdUiToolkitNineSliceParams parameters = _configMap.GetResolvedNineSliceParams(layer, _defaultNineSliceParams);
            return new LocalDedupExportSpec(
                true,
                participateCommonDedup,
                Mathf.Max(0, parameters.borderInset),
                Mathf.Clamp(parameters.pixelThreshold, 0, 255),
                Mathf.Max(1, parameters.minCenterCols),
                Mathf.Max(1, parameters.minCenterRows),
                Mathf.Max(1, parameters.minSameZone));
        }

        private static bool LocalDedupExportSpecCovers(LocalDedupExportSpec broader, LocalDedupExportSpec narrower)
        {
            if (broader.SliceEnabled && !narrower.SliceEnabled)
                return false;

            if (broader.SliceEnabled && narrower.SliceEnabled)
            {
                if (broader.BorderInset != narrower.BorderInset ||
                    broader.PixelThreshold != narrower.PixelThreshold ||
                    broader.MinCenterCols != narrower.MinCenterCols ||
                    broader.MinCenterRows != narrower.MinCenterRows ||
                    broader.MinSameZone != narrower.MinSameZone)
                    return false;
            }

            if (broader.ParticipateCommonDedup && !narrower.ParticipateCommonDedup)
                return false;

            return true;
        }

        private string FormatLocalDedupExportSpec(LocalDedupExportSpec spec)
        {
            if (!spec.SliceEnabled)
                return spec.ParticipateCommonDedup ? "no slice, common dedup on" : "no slice, common dedup off";

            return string.Format(
                CultureInfo.InvariantCulture,
                "slice bi={0} pt={1} mcc={2} mcr={3} msz={4}, common dedup {5}",
                spec.BorderInset,
                spec.PixelThreshold,
                spec.MinCenterCols,
                spec.MinCenterRows,
                spec.MinSameZone,
                spec.ParticipateCommonDedup ? "on" : "off");
        }

        private string DescribeLayer(Layer layer)
        {
            if (layer == null)
                return "<null>";

            string layerName = string.IsNullOrEmpty(layer.Name) ? "Unnamed" : layer.Name;
            return layer.LayerId.HasValue
                ? $"{layerName} (id {layer.LayerId.Value})"
                : layerName;
        }

        private Layer PickLargestRasterSourceLayer(List<Layer> cluster)
        {
            Layer best = cluster[0];
            int bestPixels = _rasterPending[best].width * _rasterPending[best].height;
            foreach (Layer layer in cluster)
            {
                Texture2D texture = _rasterPending[layer];
                int pixels = texture.width * texture.height;
                if (pixels > bestPixels)
                {
                    best = layer;
                    bestPixels = pixels;
                    continue;
                }

                if (pixels != bestPixels)
                    continue;

                int layerId = layer.LayerId ?? int.MaxValue;
                int bestId = best.LayerId ?? int.MaxValue;
                if (layerId < bestId || (layerId == bestId && string.Compare(layer.Name, best.Name, StringComparison.Ordinal) < 0))
                    best = layer;
            }

            return best;
        }

        private void SaveLayerTextureGrouped(Texture2D texture, Layer namingLayer, IReadOnlyList<Layer> groupMembers, LocalDedupExportSpec exportSpec)
        {
            if (texture == null || namingLayer?.LayerId == null || groupMembers == null || groupMembers.Count == 0)
                return;

            string fileName = BuildImageFileName(namingLayer);
            string assetPath = PsdUiToolkitAssetPathUtility.CombineAssetsPath(_imageFolderAssetPath, fileName);
            Vector4? sliceBorder = null;
            Texture2D slicedTexture = null;
            Texture2D imageToSave = texture;

            try
            {
                if (exportSpec.SliceEnabled)
                {
                    sliceBorder = DetectNineSlice(
                        texture,
                        exportSpec.BorderInset,
                        exportSpec.PixelThreshold,
                        exportSpec.MinSameZone,
                        exportSpec.MinCenterCols,
                        exportSpec.MinCenterRows);
                    if (sliceBorder.HasValue)
                    {
                        Vector4 border = sliceBorder.Value;
                        slicedTexture = BuildNineSliceImage(
                            texture,
                            Mathf.RoundToInt(border.x),
                            Mathf.RoundToInt(border.y),
                            Mathf.RoundToInt(border.z),
                            Mathf.RoundToInt(border.w),
                            exportSpec.MinCenterCols,
                            exportSpec.MinCenterRows);
                        imageToSave = slicedTexture;
                    }
                }

                float[] fingerprint = ComputeFingerprint(imageToSave);
                string resolvedAssetPath = assetPath;
                int commonDirIndex = exportSpec.ParticipateCommonDedup ? FindDuplicateInCommonDirs(fingerprint) : -1;
                if (commonDirIndex >= 0)
                {
                    resolvedAssetPath = _commonDirImageCache[commonDirIndex].AssetPath;
                }
                else
                {
                    PsdUiToolkitAssetPathUtility.EnsureParentDirectoryForFile(assetPath);
                    File.WriteAllBytes(PsdUiToolkitAssetPathUtility.GetDiskPath(assetPath), imageToSave.EncodeToPNG());
                }

                foreach (Layer layer in groupMembers)
                    RegisterRasterAssetInfo(layer, resolvedAssetPath, sliceBorder);
            }
            finally
            {
                if (slicedTexture != null)
                    Object.DestroyImmediate(slicedTexture);
            }
        }

        private void RegisterRasterAssetInfo(Layer layer, string assetPath, Vector4? sliceBorder)
        {
            if (layer?.LayerId == null || string.IsNullOrEmpty(assetPath))
                return;

            PsdUiToolkitLayerBounds bounds = GetLayerBounds(layer);
            _result.AssetsByLayerId[layer.LayerId.Value] = new PsdUiToolkitRasterAssetInfo
            {
                LayerId = layer.LayerId.Value,
                AssetPath = assetPath,
                Width = bounds.Width,
                Height = bounds.Height,
                SliceBorder = sliceBorder,
            };
        }

        private string BuildImageFileName(Layer layer)
        {
            string stem = PsdUiToolkitAssetPathUtility.SanitizeFileName(layer?.Name ?? "layer");
            if (_autoImageNaming && layer?.LayerId != null)
                stem = $"{stem}_{layer.LayerId.Value}";

            if (_fileNameCounts.TryGetValue(stem, out int count))
            {
                count += 1;
                _fileNameCounts[stem] = count;
                stem = $"{stem}_{count}";
            }
            else
            {
                _fileNameCounts[stem] = 0;
            }

            return stem + ".png";
        }

        private void BuildCommonDirImageCache()
        {
            _commonDirImageCache = new List<CommonDirImageEntry>();
            PsdUiToolkitCommonDirectoriesData config = PsdUiToolkitImageExportConfig.LoadCommonDirectories(true);
            if (config?.paths == null || config.paths.Length == 0)
                return;

            foreach (string configuredPath in config.paths)
            {
                string fullDirectoryPath = ResolveCommonDirFullPath(configuredPath);
                if (string.IsNullOrEmpty(fullDirectoryPath) || !Directory.Exists(fullDirectoryPath))
                    continue;

                string[] pngFiles = Directory.GetFiles(fullDirectoryPath, "*.png", SearchOption.AllDirectories);
                foreach (string pngFile in pngFiles)
                {
                    if (!PsdUiToolkitAssetPathUtility.TryConvertDiskPathToAssetPath(pngFile, out string assetPath))
                        continue;

                    Texture2D texture = null;
                    try
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(File.ReadAllBytes(pngFile)))
                            continue;

                        _commonDirImageCache.Add(new CommonDirImageEntry(
                            assetPath,
                            ComputeFingerprint(texture),
                            texture.width,
                            texture.height));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PsdUiToolkitRasterExporter] Failed to fingerprint {pngFile}: {ex.Message}");
                    }
                    finally
                    {
                        if (texture != null)
                            Object.DestroyImmediate(texture);
                    }
                }
            }
        }

        private string ResolveCommonDirFullPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return null;

            string trimmed = configuredPath.Trim();
            if (trimmed.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(PsdUiToolkitAssetPathUtility.GetDiskPath(trimmed));

            if (Path.IsPathRooted(trimmed))
                return Path.GetFullPath(trimmed);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, trimmed));
        }

        private int FindDuplicateInCommonDirs(float[] fingerprint)
        {
            if (_commonDirImageCache == null || _commonDirImageCache.Count == 0)
                return -1;

            for (int i = 0; i < _commonDirImageCache.Count; i++)
            {
                float[] candidate = _commonDirImageCache[i].Fingerprint;
                if (candidate == null || candidate.Length != fingerprint.Length)
                    continue;

                float sumAbsDiff = 0f;
                for (int j = 0; j < fingerprint.Length; j++)
                    sumAbsDiff += Mathf.Abs(fingerprint[j] - candidate[j]);
                if (sumAbsDiff / fingerprint.Length <= _dedupMaeThreshold)
                    return i;
            }

            return -1;
        }

        private float[] ComputeFingerprint(Texture2D texture)
        {
            return ComputeFingerprint(texture, _dedupFingerprintSize);
        }

        internal static float[] ComputeFingerprint(Texture2D texture, int fingerprintSize)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            Color32[] sourcePixels = texture.GetPixels32();
            (Color32[] pixels, int width, int height) = TrimTransparentBorders(sourcePixels, texture.width, texture.height);

            int size = Mathf.Clamp(
                fingerprintSize,
                PsdUiToolkitDedupConfigData.MinFingerprintSize,
                PsdUiToolkitDedupConfigData.MaxFingerprintSize);
            float[] fingerprint = new float[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sampleX = (x + 0.5f) * width / size - 0.5f;
                    float sampleY = (y + 0.5f) * height / size - 0.5f;
                    Color color = BilinearSamplePremultiplied(pixels, width, height, sampleX, sampleY);

                    int index = (y * size + x) * 4;
                    fingerprint[index + 0] = color.r * color.a;
                    fingerprint[index + 1] = color.g * color.a;
                    fingerprint[index + 2] = color.b * color.a;
                    fingerprint[index + 3] = color.a;
                }
            }

            return fingerprint;
        }

        private bool FingerprintsMatchLocal(float[] left, float[] right)
        {
            return CalculateFingerprintMae(left, right) <= _dedupMaeThreshold;
        }

        internal static float CalculateFingerprintMae(float[] left, float[] right)
        {
            if (left == null)
                throw new ArgumentNullException(nameof(left));
            if (right == null)
                throw new ArgumentNullException(nameof(right));
            if (left.Length == 0 || left.Length != right.Length)
                throw new ArgumentException("Fingerprint arrays must be non-empty and have the same length.");

            float sumAbsDiff = 0f;
            for (int i = 0; i < left.Length; i++)
                sumAbsDiff += Mathf.Abs(left[i] - right[i]);
            return sumAbsDiff / left.Length;
        }

        private static Color BilinearSamplePremultiplied(Color32[] pixels, int width, int height, float x, float y)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);

            float fx = Mathf.Max(0f, x - x0);
            float fy = Mathf.Max(0f, y - y0);
            float ifx = 1f - fx;
            float ify = 1f - fy;

            Color32 c00 = pixels[y0 * width + x0];
            Color32 c10 = pixels[y0 * width + x1];
            Color32 c01 = pixels[y1 * width + x0];
            Color32 c11 = pixels[y1 * width + x1];

            float a00 = c00.a / 255f;
            float a10 = c10.a / 255f;
            float a01 = c01.a / 255f;
            float a11 = c11.a / 255f;

            float premultipliedRed = (c00.r / 255f * a00 * ifx + c10.r / 255f * a10 * fx) * ify +
                                     (c01.r / 255f * a01 * ifx + c11.r / 255f * a11 * fx) * fy;
            float premultipliedGreen = (c00.g / 255f * a00 * ifx + c10.g / 255f * a10 * fx) * ify +
                                       (c01.g / 255f * a01 * ifx + c11.g / 255f * a11 * fx) * fy;
            float premultipliedBlue = (c00.b / 255f * a00 * ifx + c10.b / 255f * a10 * fx) * ify +
                                      (c01.b / 255f * a01 * ifx + c11.b / 255f * a11 * fx) * fy;
            float alpha = (a00 * ifx + a10 * fx) * ify + (a01 * ifx + a11 * fx) * fy;

            if (alpha > 0.001f)
            {
                return new Color(
                    Mathf.Clamp01(premultipliedRed / alpha),
                    Mathf.Clamp01(premultipliedGreen / alpha),
                    Mathf.Clamp01(premultipliedBlue / alpha),
                    alpha);
            }

            return new Color(0f, 0f, 0f, 0f);
        }

        private static (Color32[] pixels, int width, int height) TrimTransparentBorders(Color32[] sourcePixels, int sourceWidth, int sourceHeight)
        {
            int minX = sourceWidth;
            int maxX = -1;
            int minY = sourceHeight;
            int maxY = -1;
            for (int y = 0; y < sourceHeight; y++)
            {
                for (int x = 0; x < sourceWidth; x++)
                {
                    if (sourcePixels[y * sourceWidth + x].a <= 0)
                        continue;

                    if (x < minX)
                        minX = x;
                    if (x > maxX)
                        maxX = x;
                    if (y < minY)
                        minY = y;
                    if (y > maxY)
                        maxY = y;
                }
            }

            if (maxX < 0)
                return (sourcePixels, sourceWidth, sourceHeight);

            int trimmedWidth = maxX - minX + 1;
            int trimmedHeight = maxY - minY + 1;
            if (trimmedWidth == sourceWidth && trimmedHeight == sourceHeight)
                return (sourcePixels, sourceWidth, sourceHeight);

            Color32[] trimmed = new Color32[trimmedWidth * trimmedHeight];
            for (int y = 0; y < trimmedHeight; y++)
            {
                for (int x = 0; x < trimmedWidth; x++)
                    trimmed[y * trimmedWidth + x] = sourcePixels[(minY + y) * sourceWidth + (minX + x)];
            }

            return (trimmed, trimmedWidth, trimmedHeight);
        }

        private static bool ColsAreSame(Color32[] pixels, int width, int height, int x, int threshold)
        {
            if (x + 1 >= width)
                return false;

            for (int y = 0; y < height; y++)
            {
                Color32 current = pixels[y * width + x];
                Color32 next = pixels[y * width + x + 1];
                if (Mathf.Abs(current.r - next.r) > threshold ||
                    Mathf.Abs(current.g - next.g) > threshold ||
                    Mathf.Abs(current.b - next.b) > threshold ||
                    Mathf.Abs(current.a - next.a) > threshold)
                    return false;
            }

            return true;
        }

        private static bool RowsAreSame(Color32[] pixels, int width, int height, int y, int threshold)
        {
            if (y + 1 >= height)
                return false;

            for (int x = 0; x < width; x++)
            {
                Color32 current = pixels[y * width + x];
                Color32 next = pixels[(y + 1) * width + x];
                if (Mathf.Abs(current.r - next.r) > threshold ||
                    Mathf.Abs(current.g - next.g) > threshold ||
                    Mathf.Abs(current.b - next.b) > threshold ||
                    Mathf.Abs(current.a - next.a) > threshold)
                    return false;
            }

            return true;
        }

        private static (int start, int length) FindLongestSameZone(bool[] sameFlags, int minLength)
        {
            int bestStart = -1;
            int bestLength = 0;
            int currentStart = 0;
            int currentLength = 0;
            for (int i = 0; i < sameFlags.Length; i++)
            {
                if (sameFlags[i])
                {
                    if (currentLength == 0)
                        currentStart = i;
                    currentLength += 1;
                    if (currentLength > bestLength)
                    {
                        bestStart = currentStart;
                        bestLength = currentLength;
                    }
                }
                else
                {
                    currentLength = 0;
                }
            }

            return bestLength >= minLength ? (bestStart, bestLength) : (-1, 0);
        }

        private static void ComputeNineSliceCenterCrop(int sourceWidth, int sourceHeight, int left, int right, int bottom, int top, int maxCenterCols, int maxCenterRows, out int centerCols, out int centerRows)
        {
            bool horizontalSlice = left > 0 || right > 0;
            bool verticalSlice = bottom > 0 || top > 0;
            maxCenterCols = Mathf.Max(1, maxCenterCols);
            maxCenterRows = Mathf.Max(1, maxCenterRows);

            if (horizontalSlice)
            {
                int croppableWidth = Mathf.Max(1, sourceWidth - left - right);
                centerCols = Mathf.Min(maxCenterCols, croppableWidth);
            }
            else
            {
                centerCols = sourceWidth;
            }

            if (verticalSlice)
            {
                int croppableHeight = Mathf.Max(1, sourceHeight - bottom - top);
                centerRows = Mathf.Min(maxCenterRows, croppableHeight);
            }
            else
            {
                centerRows = sourceHeight;
            }
        }

        private static Vector4? DetectNineSlice(Texture2D texture, int borderInset, int pixelThreshold, int minSameZone, int minCenterCols, int minCenterRows)
        {
            int width = texture.width;
            int height = texture.height;
            if (width < 4 || height < 4)
                return null;

            Color32[] pixels = texture.GetPixels32();
            borderInset = Mathf.Max(0, borderInset);
            pixelThreshold = Mathf.Clamp(pixelThreshold, 0, 255);
            minSameZone = Mathf.Max(1, minSameZone);
            minCenterCols = Mathf.Max(1, minCenterCols);
            minCenterRows = Mathf.Max(1, minCenterRows);

            bool[] columnFlags = new bool[width - 1];
            for (int x = 0; x < width - 1; x++)
                columnFlags[x] = ColsAreSame(pixels, width, height, x, pixelThreshold);
            (int horizontalZoneStart, int horizontalZoneLength) = FindLongestSameZone(columnFlags, 1);

            bool[] rowFlags = new bool[height - 1];
            for (int y = 0; y < height - 1; y++)
                rowFlags[y] = RowsAreSame(pixels, width, height, y, pixelThreshold);
            (int verticalZoneStart, int verticalZoneLength) = FindLongestSameZone(rowFlags, 1);

            bool horizontalStrong = horizontalZoneLength >= minSameZone;
            bool verticalStrong = verticalZoneLength >= minSameZone;
            if (!horizontalStrong && !verticalStrong)
                return null;

            int left;
            int right;
            if (horizontalStrong || (horizontalZoneStart >= 0 && horizontalZoneLength > borderInset))
            {
                int inset = Mathf.Min(borderInset, horizontalZoneLength / 2);
                left = horizontalZoneStart + inset;
                right = Mathf.Max(0, width - 1 - (horizontalZoneStart + horizontalZoneLength) + inset);
            }
            else
            {
                left = 0;
                right = 0;
            }

            int bottom;
            int top;
            if (verticalStrong || (verticalZoneStart >= 0 && verticalZoneLength > borderInset))
            {
                int inset = Mathf.Min(borderInset, verticalZoneLength / 2);
                bottom = verticalZoneStart + inset;
                top = Mathf.Max(0, height - 1 - (verticalZoneStart + verticalZoneLength) + inset);
            }
            else
            {
                bottom = 0;
                top = 0;
            }

            ComputeNineSliceCenterCrop(width, height, left, right, bottom, top, minCenterCols, minCenterRows, out int effectiveCenterCols, out int effectiveCenterRows);
            int outputWidth = left + effectiveCenterCols + right;
            int outputHeight = bottom + effectiveCenterRows + top;
            if (outputWidth >= width && outputHeight >= height)
                return null;

            return new Vector4(left, bottom, right, top);
        }

        private static void CopyRegion(Color32[] source, int sourceWidth, int sourceX, int sourceY, int regionWidth, int regionHeight, Color32[] destination, int destinationWidth, int destinationX, int destinationY)
        {
            for (int y = 0; y < regionHeight; y++)
            {
                for (int x = 0; x < regionWidth; x++)
                {
                    destination[(destinationY + y) * destinationWidth + destinationX + x] =
                        source[(sourceY + y) * sourceWidth + sourceX + x];
                }
            }
        }

        private static Texture2D BuildNineSliceImage(Texture2D source, int left, int bottom, int right, int top, int maxCenterCols, int maxCenterRows)
        {
            int width = source.width;
            int height = source.height;
            ComputeNineSliceCenterCrop(width, height, left, right, bottom, top, maxCenterCols, maxCenterRows, out int centerCols, out int centerRows);
            int outputWidth = left + centerCols + right;
            int outputHeight = bottom + centerRows + top;
            Color32[] sourcePixels = source.GetPixels32();
            Color32[] outputPixels = new Color32[outputWidth * outputHeight];

            int sourceCenterWidth = width - left - right;
            int sourceCenterHeight = height - bottom - top;
            int sampleX = left + Mathf.Max(0, (sourceCenterWidth - centerCols) / 2);
            int sampleY = bottom + Mathf.Max(0, (sourceCenterHeight - centerRows) / 2);

            if (left > 0 && bottom > 0)
                CopyRegion(sourcePixels, width, 0, 0, left, bottom, outputPixels, outputWidth, 0, 0);
            if (centerCols > 0 && bottom > 0)
                CopyRegion(sourcePixels, width, sampleX, 0, centerCols, bottom, outputPixels, outputWidth, left, 0);
            if (right > 0 && bottom > 0)
                CopyRegion(sourcePixels, width, width - right, 0, right, bottom, outputPixels, outputWidth, left + centerCols, 0);
            if (left > 0 && centerRows > 0)
                CopyRegion(sourcePixels, width, 0, sampleY, left, centerRows, outputPixels, outputWidth, 0, bottom);
            if (centerCols > 0 && centerRows > 0)
                CopyRegion(sourcePixels, width, sampleX, sampleY, centerCols, centerRows, outputPixels, outputWidth, left, bottom);
            if (right > 0 && centerRows > 0)
                CopyRegion(sourcePixels, width, width - right, sampleY, right, centerRows, outputPixels, outputWidth, left + centerCols, bottom);
            if (left > 0 && top > 0)
                CopyRegion(sourcePixels, width, 0, height - top, left, top, outputPixels, outputWidth, 0, bottom + centerRows);
            if (centerCols > 0 && top > 0)
                CopyRegion(sourcePixels, width, sampleX, height - top, centerCols, top, outputPixels, outputWidth, left, bottom + centerRows);
            if (right > 0 && top > 0)
                CopyRegion(sourcePixels, width, width - right, height - top, right, top, outputPixels, outputWidth, left + centerCols, bottom + centerRows);

            Texture2D output = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
            output.SetPixels32(outputPixels);
            output.Apply();
            return output;
        }

        private void DisposeAllRasterPending()
        {
            foreach (Texture2D texture in _rasterPending.Values)
            {
                if (texture != null)
                    Object.DestroyImmediate(texture);
            }

            _rasterPending.Clear();
            _rasterPendingOrder.Clear();
        }

        private void SetupImportedSprites()
        {
            Dictionary<string, Vector4?> spriteBordersByPath = new Dictionary<string, Vector4?>(StringComparer.OrdinalIgnoreCase);
            foreach (PsdUiToolkitRasterAssetInfo info in _result.AssetsByLayerId.Values)
            {
                if (string.IsNullOrEmpty(info.AssetPath))
                    continue;

                if (!spriteBordersByPath.TryGetValue(info.AssetPath, out Vector4? existingBorder) || (!existingBorder.HasValue && info.SliceBorder.HasValue))
                    spriteBordersByPath[info.AssetPath] = info.SliceBorder;
            }

            foreach (KeyValuePair<string, Vector4?> entry in spriteBordersByPath)
            {
                AssetImporter importer = AssetImporter.GetAtPath(entry.Key);
                if (!(importer is TextureImporter textureImporter))
                    continue;

                textureImporter.textureType = TextureImporterType.Sprite;
                textureImporter.spriteImportMode = SpriteImportMode.Single;
                textureImporter.mipmapEnabled = false;
                textureImporter.alphaIsTransparency = true;
                textureImporter.spriteBorder = entry.Value ?? Vector4.zero;
                textureImporter.SaveAndReimport();
            }
        }

        private void ResolveAssetReferences()
        {
            foreach (PsdUiToolkitRasterAssetInfo info in _result.AssetsByLayerId.Values)
            {
                if (string.IsNullOrEmpty(info.AssetPath))
                    continue;

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(info.AssetPath);
                if (sprite != null)
                {
                    info.StyleImageUri = PsdUiToolkitAssetPathUtility.BuildProjectDatabaseUri(sprite);
                    if (sprite.border.sqrMagnitude > 0f)
                        info.SliceBorder = new Vector4(sprite.border.x, sprite.border.y, sprite.border.z, sprite.border.w);
                    continue;
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(info.AssetPath);
                if (texture != null)
                    info.StyleImageUri = PsdUiToolkitAssetPathUtility.BuildProjectDatabaseUri(texture);
            }
        }

        private Texture2D BuildMergedClippingTexture(Layer baseLayer, List<Layer> clippedLayers)
        {
            if (baseLayer == null)
                return null;

            Texture2D baseTexture = CreateTextureForLayer(baseLayer);
            if (baseTexture == null)
                return null;

            PsdUiToolkitLayerBounds baseBounds = GetLayerBounds(baseLayer);
            Color32[] basePixels = baseTexture.GetPixels32();
            byte[] originalBaseAlpha = new byte[basePixels.Length];
            for (int i = 0; i < basePixels.Length; i++)
                originalBaseAlpha[i] = basePixels[i].a;

            foreach (Layer clippedLayer in clippedLayers)
            {
                Texture2D clipTexture = CreateTextureForLayer(clippedLayer);
                if (clipTexture == null)
                    continue;

                try
                {
                    CompositeClippedOntoBase(baseTexture, baseBounds, clipTexture, GetLayerBounds(clippedLayer), originalBaseAlpha);
                }
                finally
                {
                    Object.DestroyImmediate(clipTexture);
                }
            }

            return baseTexture;
        }

        private Texture2D CreateTextureForLayer(Layer layer)
        {
            if (layer == null)
                return null;

            if (layer.IsGroup)
                return _psd.CompositeGroupWithClipping((Group)layer);
            if (layer.Kind == LayerKind.Type)
                return null;
            if (!layer.HasPixels() && !LayerEffectsHelper.HasExtractableColor(layer))
                return null;
            return LayerEffectsHelper.CreateLayerTextureWithEffects(layer, true);
        }

        private static void CompositeClippedOntoBase(Texture2D baseTexture, PsdUiToolkitLayerBounds baseBounds, Texture2D clipTexture, PsdUiToolkitLayerBounds clipBounds, byte[] originalBaseAlpha)
        {
            int baseWidth = baseTexture.width;
            int baseHeight = baseTexture.height;
            int clipWidth = clipTexture.width;
            int clipHeight = clipTexture.height;
            Color32[] basePixels = baseTexture.GetPixels32();
            Color32[] clipPixels = clipTexture.GetPixels32();

            for (int psdY = 0; psdY < clipHeight; psdY++)
            {
                for (int psdX = 0; psdX < clipWidth; psdX++)
                {
                    int worldX = clipBounds.Left + psdX;
                    int worldY = clipBounds.Top + psdY;
                    int baseX = worldX - baseBounds.Left;
                    int baseY = worldY - baseBounds.Top;
                    if (baseX < 0 || baseX >= baseWidth || baseY < 0 || baseY >= baseHeight)
                        continue;

                    int baseIndex = (baseHeight - 1 - baseY) * baseWidth + baseX;
                    int clipIndex = (clipHeight - 1 - psdY) * clipWidth + psdX;
                    if (baseIndex < 0 || baseIndex >= basePixels.Length || clipIndex < 0 || clipIndex >= clipPixels.Length)
                        continue;

                    byte maskAlpha = originalBaseAlpha[baseIndex];
                    if (maskAlpha == 0)
                        continue;

                    Color32 source = clipPixels[clipIndex];
                    float sourceAlpha = source.a / 255f * (maskAlpha / 255f);
                    if (sourceAlpha <= 0f)
                        continue;

                    Color32 destination = basePixels[baseIndex];
                    float destinationAlpha = destination.a / 255f;
                    float outputAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
                    if (outputAlpha <= 0f)
                        continue;

                    basePixels[baseIndex] = new Color32(
                        (byte)((source.r * sourceAlpha + destination.r * destinationAlpha * (1f - sourceAlpha)) / outputAlpha),
                        (byte)((source.g * sourceAlpha + destination.g * destinationAlpha * (1f - sourceAlpha)) / outputAlpha),
                        (byte)((source.b * sourceAlpha + destination.b * destinationAlpha * (1f - sourceAlpha)) / outputAlpha),
                        (byte)(outputAlpha * 255f));
                }
            }

            baseTexture.SetPixels32(basePixels);
            baseTexture.Apply();
        }

        private void SuppressLayer(Layer layer)
        {
            if (layer?.LayerId == null)
                return;

            _result.SuppressedLayerIds.Add(layer.LayerId.Value);
        }

        internal static PsdUiToolkitLayerBounds GetLayerBounds(Layer layer)
        {
            if (layer == null)
                return new PsdUiToolkitLayerBounds(0, 0, 0, 0);

            if (layer.IsGroup)
            {
                (int Left, int Top, int Right, int Bottom) bbox = ((Group)layer).BBox;
                return new PsdUiToolkitLayerBounds(bbox.Left, bbox.Top, bbox.Right - bbox.Left, bbox.Bottom - bbox.Top);
            }

            return new PsdUiToolkitLayerBounds(layer.Left, layer.Top, layer.Width, layer.Height);
        }
    }

    public static class PsdUiToolkitExporter
    {
        private static void DeleteStaleGeneratedImages(string imageFolderAssetPath, PsdUiToolkitRasterExportResult rasterResult)
        {
            HashSet<string> currentAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PsdUiToolkitRasterAssetInfo assetInfo in rasterResult.AssetsByLayerId.Values)
            {
                if (assetInfo != null && !string.IsNullOrEmpty(assetInfo.AssetPath))
                    currentAssetPaths.Add(PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(assetInfo.AssetPath));
            }

            string diskFolder = PsdUiToolkitAssetPathUtility.GetDiskPath(imageFolderAssetPath);
            if (!Directory.Exists(diskFolder))
                return;

            List<string> staleAssetPaths = new List<string>();
            foreach (string diskPath in Directory.EnumerateFiles(diskFolder, "*.png", SearchOption.TopDirectoryOnly))
            {
                if (PsdUiToolkitAssetPathUtility.TryConvertDiskPathToAssetPath(diskPath, out string assetPath)
                    && !currentAssetPaths.Contains(PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(assetPath)))
                {
                    staleAssetPaths.Add(assetPath);
                }
            }

            if (staleAssetPaths.Count == 0)
                return;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string staleAssetPath in staleAssetPaths)
                    AssetDatabase.DeleteAsset(staleAssetPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        public static PsdUiToolkitExportArtifacts Export(string psdPath, string imageExportRoot, string uxmlExportRoot, bool autoImageNaming = true)
        {
            if (string.IsNullOrEmpty(psdPath))
                throw new ArgumentException("PSD path is required.", nameof(psdPath));

            string psdName = Path.GetFileNameWithoutExtension(psdPath);
            string imageRoot = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(imageExportRoot);
            string uxmlRoot = PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(uxmlExportRoot);
            string imageFolderAssetPath = PsdUiToolkitAssetPathUtility.CombineAssetsPath(imageRoot, psdName);
            string uxmlAssetPath = PsdUiToolkitAssetPathUtility.CombineAssetsPath(uxmlRoot, psdName + ".uxml");

            PsdUiToolkitAssetPathUtility.EnsureAssetDirectoryExists(imageRoot);
            PsdUiToolkitAssetPathUtility.EnsureAssetDirectoryExists(imageFolderAssetPath);
            PsdUiToolkitAssetPathUtility.EnsureParentDirectoryForFile(uxmlAssetPath);

            PsdImage psd = null;
            try
            {
                psd = PsdImage.Open(psdPath);
                PsdUiToolkitExportConfigData config = PsdUiToolkitConfigStore.LoadAndSync(psdPath, psd);
                PsdUiToolkitConfigStore.ApplyToPsd(psd, config);

                PsdUiToolkitLayerConfigMap configMap = new PsdUiToolkitLayerConfigMap(config);
                PsdUiToolkitFontMappingLookup fontMapping = PsdUiToolkitFontMappingConfig.PrepareForExport(psd);
                PsdUiToolkitRasterExporter rasterExporter = new PsdUiToolkitRasterExporter(psd, configMap, imageFolderAssetPath, autoImageNaming);
                PsdUiToolkitRasterExportResult rasterResult = rasterExporter.ExportAll();
                PsdUiToolkitLayoutTree layoutTree = configMap.GetAutoLayoutConfig().rebuildLayoutTree
                    ? PsdUiToolkitLayoutTreeRebuilder.Build(psd, configMap, rasterResult, psdName)
                    : PsdUiToolkitAutoLayoutAnalyzer.Analyze(psd, configMap, rasterResult, psdName);
                PsdUiToolkitUxmlWriter.Write(layoutTree, configMap, rasterResult, fontMapping, uxmlAssetPath);
                DeleteStaleGeneratedImages(imageFolderAssetPath, rasterResult);
                AssetDatabase.Refresh();

                return new PsdUiToolkitExportArtifacts
                {
                    ImageFolderAssetPath = imageFolderAssetPath,
                    UxmlAssetPath = uxmlAssetPath,
                };
            }
            finally
            {
                psd?.ReleaseAllData();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal sealed class PsdUiToolkitLayoutEditHistory
    {
        private const int MaxUndoSteps = 50;

        [Serializable]
        private sealed class Snapshot
        {
            public LayerIntent[] layers = Array.Empty<LayerIntent>();
            public PsdUiToolkitVirtualGroupConfig[] virtualGroups =
                Array.Empty<PsdUiToolkitVirtualGroupConfig>();
            public PsdUiToolkitButtonSemanticConfig[] buttons =
                Array.Empty<PsdUiToolkitButtonSemanticConfig>();
        }

        [Serializable]
        private struct LayerIntent
        {
            public int id;
            public PsdUiToolkitContainerLayout childrenLayout;
            public PsdUiToolkitItemRole itemRole;
            public PsdUiToolkitMainAxisDistribution mainAxisDistribution;
            public PsdUiToolkitCrossAxisAlignment crossAxisAlignment;
            public PsdUiToolkitWrapMode wrapMode;
            public PsdUiToolkitMultiLineDistribution multiLineDistribution;
        }

        private readonly List<string> _states = new List<string>();
        private int _index = -1;

        public bool CanUndo => _index > 0;
        public bool CanRedo => _index >= 0 && _index < _states.Count - 1;

        public void Reset(PsdUiToolkitExportConfigData data)
        {
            _states.Clear();
            _states.Add(Capture(data));
            _index = 0;
        }

        public void Clear()
        {
            _states.Clear();
            _index = -1;
        }

        public void Record(PsdUiToolkitExportConfigData data)
        {
            string state = Capture(data);
            if (_index >= 0 && string.Equals(_states[_index], state, StringComparison.Ordinal))
                return;

            if (_index < _states.Count - 1)
                _states.RemoveRange(_index + 1, _states.Count - _index - 1);

            _states.Add(state);
            _index = _states.Count - 1;
            while (_states.Count > MaxUndoSteps + 1)
            {
                _states.RemoveAt(0);
                _index--;
            }
        }

        public bool Undo(PsdUiToolkitExportConfigData data)
        {
            if (!CanUndo)
                return false;

            _index--;
            Apply(data, _states[_index]);
            return true;
        }

        public bool Redo(PsdUiToolkitExportConfigData data)
        {
            if (!CanRedo)
                return false;

            _index++;
            Apply(data, _states[_index]);
            return true;
        }

        internal static string Capture(PsdUiToolkitExportConfigData data)
        {
            PsdUiToolkitExportConfigData source =
                PsdUiToolkitConfigStore.MigrateToCurrentVersion(data ?? new PsdUiToolkitExportConfigData());
            LayerIntent[] layerIntents = new LayerIntent[source.layers.Length];
            for (int i = 0; i < source.layers.Length; i++)
            {
                PsdUiToolkitLayerConfig layer = source.layers[i];
                if (layer == null)
                    continue;

                layerIntents[i] = new LayerIntent
                {
                    id = layer.id,
                    childrenLayout = layer.childrenLayout,
                    itemRole = layer.itemRole,
                    mainAxisDistribution = layer.mainAxisDistribution,
                    crossAxisAlignment = layer.crossAxisAlignment,
                    wrapMode = layer.wrapMode,
                    multiLineDistribution = layer.multiLineDistribution,
                };
            }

            Snapshot snapshot = new Snapshot
            {
                layers = layerIntents,
                virtualGroups = source.virtualGroups,
                buttons = source.buttons,
            };
            return JsonUtility.ToJson(snapshot);
        }

        internal static void Apply(PsdUiToolkitExportConfigData data, string json)
        {
            if (data == null || string.IsNullOrEmpty(json))
                return;

            Snapshot snapshot = JsonUtility.FromJson<Snapshot>(json) ?? new Snapshot();
            Dictionary<int, LayerIntent> intents = new Dictionary<int, LayerIntent>();
            LayerIntent[] snapshotLayers = snapshot.layers ?? Array.Empty<LayerIntent>();
            for (int i = 0; i < snapshotLayers.Length; i++)
                intents[snapshotLayers[i].id] = snapshotLayers[i];

            PsdUiToolkitLayerConfig[] layers = data.layers ?? Array.Empty<PsdUiToolkitLayerConfig>();
            for (int i = 0; i < layers.Length; i++)
            {
                PsdUiToolkitLayerConfig layer = layers[i];
                if (layer == null || !intents.TryGetValue(layer.id, out LayerIntent intent))
                    continue;

                layer.childrenLayout = intent.childrenLayout;
                layer.itemRole = intent.itemRole;
                layer.mainAxisDistribution = intent.mainAxisDistribution;
                layer.crossAxisAlignment = intent.crossAxisAlignment;
                layer.wrapMode = intent.wrapMode;
                layer.multiLineDistribution = intent.multiLineDistribution;
                layer.Sanitize();
            }

            data.virtualGroups =
                snapshot.virtualGroups ?? Array.Empty<PsdUiToolkitVirtualGroupConfig>();
            data.buttons = snapshot.buttons ?? Array.Empty<PsdUiToolkitButtonSemanticConfig>();
            PsdUiToolkitConfigStore.MigrateToCurrentVersion(data);
        }
    }
}

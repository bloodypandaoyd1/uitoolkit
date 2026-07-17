using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using PsdTools.Layers;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitUxmlWriter
    {
        private sealed class SemanticWriteContext
        {
            public readonly PsdUiToolkitLayoutTree LayoutTree;
            public readonly Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitLayoutNode> Nodes;
            public readonly Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitButtonSemanticConfig> Buttons;
            public readonly Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitComponentDefinitionConfig> DefinitionsByRoot;
            public readonly Dictionary<string, PsdUiToolkitComponentDefinitionConfig> DefinitionsById;
            public readonly Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitComponentInstanceConfig> Instances;
            public readonly Dictionary<string, PsdUiToolkitComponentExportArtifact> Artifacts;
            public readonly HashSet<PsdUiToolkitNodeReference> InvalidButtonOwners =
                new HashSet<PsdUiToolkitNodeReference>();
            public readonly HashSet<string> WrittenButtonRules =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly StringBuilder Uss = new StringBuilder(2048);
            public string ActiveDefinitionId;

            public SemanticWriteContext(
                PsdUiToolkitLayoutTree layoutTree,
                PsdUiToolkitLayerConfigMap configMap,
                PsdUiToolkitComponentExportArtifact[] artifacts)
            {
                LayoutTree = layoutTree;
                Nodes = new Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitLayoutNode>();
                Buttons = new Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitButtonSemanticConfig>();
                DefinitionsByRoot = new Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitComponentDefinitionConfig>();
                DefinitionsById = new Dictionary<string, PsdUiToolkitComponentDefinitionConfig>(StringComparer.Ordinal);
                Instances = new Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitComponentInstanceConfig>();
                Artifacts = new Dictionary<string, PsdUiToolkitComponentExportArtifact>(StringComparer.Ordinal);
                CollectNodes(layoutTree.Children, Nodes);

                PsdUiToolkitButtonSemanticConfig[] buttons = configMap.GetButtons();
                for (int i = 0; i < buttons.Length; i++)
                {
                    PsdUiToolkitButtonSemanticConfig button = buttons[i];
                    if (button != null && button.owner.IsValid && !Buttons.ContainsKey(button.owner))
                        Buttons.Add(button.owner, button);
                }

                PsdUiToolkitComponentDefinitionConfig[] definitions =
                    configMap.GetComponentDefinitions();
                for (int i = 0; i < definitions.Length; i++)
                {
                    PsdUiToolkitComponentDefinitionConfig definition = definitions[i];
                    if (definition == null
                        || string.IsNullOrEmpty(definition.id)
                        || !definition.root.IsValid)
                    {
                        continue;
                    }
                    if (DefinitionsById.ContainsKey(definition.id))
                        continue;
                    DefinitionsById.Add(definition.id, definition);
                    if (!DefinitionsByRoot.ContainsKey(definition.root))
                        DefinitionsByRoot.Add(definition.root, definition);
                }

                PsdUiToolkitComponentInstanceConfig[] instances =
                    configMap.GetComponentInstances();
                for (int i = 0; i < instances.Length; i++)
                {
                    PsdUiToolkitComponentInstanceConfig instance = instances[i];
                    if (instance != null
                        && instance.owner.IsValid
                        && !Instances.ContainsKey(instance.owner))
                    {
                        Instances.Add(instance.owner, instance);
                    }
                }

                artifacts ??= Array.Empty<PsdUiToolkitComponentExportArtifact>();
                for (int i = 0; i < artifacts.Length; i++)
                {
                    if (artifacts[i] != null
                        && !string.IsNullOrEmpty(artifacts[i].ComponentId))
                    {
                        Artifacts[artifacts[i].ComponentId] = artifacts[i];
                    }
                }
            }

            private SemanticWriteContext(
                SemanticWriteContext source,
                string activeDefinitionId)
            {
                LayoutTree = source.LayoutTree;
                Nodes = source.Nodes;
                Buttons = source.Buttons;
                DefinitionsByRoot = source.DefinitionsByRoot;
                DefinitionsById = source.DefinitionsById;
                Instances = source.Instances;
                Artifacts = source.Artifacts;
                ActiveDefinitionId = activeDefinitionId ?? string.Empty;
            }

            public SemanticWriteContext CreateDocumentContext(string activeDefinitionId)
            {
                return new SemanticWriteContext(this, activeDefinitionId);
            }
        }

        public static void Write(
            PsdUiToolkitLayoutTree layoutTree,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping,
            string outputAssetPath,
            string outputUssAssetPath,
            PsdUiToolkitComponentExportArtifact[] componentArtifacts)
        {
            if (layoutTree == null)
                throw new ArgumentNullException(nameof(layoutTree));
            if (configMap == null)
                throw new ArgumentNullException(nameof(configMap));
            if (rasterResult == null)
                throw new ArgumentNullException(nameof(rasterResult));

            SemanticWriteContext sharedContext = new SemanticWriteContext(
                layoutTree,
                configMap,
                componentArtifacts);
            SemanticWriteContext pageContext = sharedContext.CreateDocumentContext(string.Empty);
            StringBuilder pageBody = new StringBuilder(8192);
            pageBody.Append("  <ui:VisualElement");
            pageBody.Append($" name=\"{EscapeAttribute(layoutTree.RootName)}\"");
            pageBody.Append($" style=\"position: relative; width: {layoutTree.Width}px; height: {layoutTree.Height}px; overflow: hidden;\"");
            pageBody.AppendLine(" >");

            foreach (PsdUiToolkitLayoutNode child in layoutTree.Children)
            {
                AppendLayoutNode(pageBody, child, 2, 0, 0, configMap, rasterResult, fontMapping, PsdUiToolkitFlowChildPlacement.Absolute, pageContext);
            }

            pageBody.AppendLine("  </ui:VisualElement>");
            StringBuilder builder = new StringBuilder(pageBody.Length + 1024);
            AppendDocumentHeader(
                builder,
                pageContext,
                outputUssAssetPath,
                string.Empty,
                pageContext.Uss.Length > 0);
            builder.Append(pageBody);
            builder.AppendLine("</ui:UXML>");

            string diskPath = PsdUiToolkitAssetPathUtility.GetDiskPath(outputAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath) ?? string.Empty);
            File.WriteAllText(diskPath, builder.ToString(), new UTF8Encoding(false));
            WriteUss(outputUssAssetPath, pageContext.Uss);

            PsdUiToolkitComponentExportArtifact[] artifacts =
                componentArtifacts ?? Array.Empty<PsdUiToolkitComponentExportArtifact>();
            for (int i = 0; i < artifacts.Length; i++)
            {
                PsdUiToolkitComponentExportArtifact artifact = artifacts[i];
                PsdUiToolkitComponentDefinitionConfig definition = null;
                PsdUiToolkitLayoutNode rootNode = null;
                if (artifact == null
                    || !sharedContext.DefinitionsById.TryGetValue(
                        artifact.ComponentId,
                        out definition)
                    || !sharedContext.Nodes.TryGetValue(
                        definition.root,
                        out rootNode))
                {
                    AddWarning(
                        layoutTree,
                        $"Component '{artifact?.Name ?? artifact?.ComponentId}' has a missing root and was not generated.",
                        definition?.root ?? default,
                        "MissingComponentRoot");
                    continue;
                }

                SemanticWriteContext componentContext =
                    sharedContext.CreateDocumentContext(definition.id);
                StringBuilder componentBody = new StringBuilder(4096);
                AppendLayoutNode(
                    componentBody,
                    rootNode,
                    1,
                    rootNode.Bounds.Left,
                    rootNode.Bounds.Top,
                    configMap,
                    rasterResult,
                    fontMapping,
                    new PsdUiToolkitFlowChildPlacement(true, 0, 0),
                    componentContext);
                StringBuilder componentBuilder =
                    new StringBuilder(componentBody.Length + 1024);
                AppendDocumentHeader(
                    componentBuilder,
                    componentContext,
                    artifact.GeneratedUssAssetPath,
                    definition.id,
                    componentContext.Uss.Length > 0);
                componentBuilder.Append(componentBody);
                componentBuilder.AppendLine("</ui:UXML>");
                WriteTextAsset(artifact.GeneratedUxmlAssetPath, componentBuilder.ToString());
                WriteUss(artifact.GeneratedUssAssetPath, componentContext.Uss);
            }
        }

        private static void CollectNodes(
            IEnumerable<PsdUiToolkitLayoutNode> nodes,
            Dictionary<PsdUiToolkitNodeReference, PsdUiToolkitLayoutNode> lookup)
        {
            if (nodes == null)
                return;
            foreach (PsdUiToolkitLayoutNode node in nodes)
            {
                if (node == null)
                    continue;
                PsdUiToolkitNodeReference reference = node.Reference;
                if (reference.IsValid && !lookup.ContainsKey(reference))
                    lookup.Add(reference, node);
                CollectNodes(node.Children, lookup);
            }
        }

        private static void AppendDocumentHeader(
            StringBuilder builder,
            SemanticWriteContext context,
            string ussAssetPath,
            string activeDefinitionId,
            bool includeStyle)
        {
            builder.AppendLine("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">");
            if (includeStyle)
            {
                builder.AppendLine(
                    $"  <ui:Style src=\"{EscapeAttribute(ToProjectUri(ussAssetPath))}\" />");
            }
            List<string> componentIds =
                new List<string>(context.Artifacts.Keys);
            componentIds.Sort(StringComparer.Ordinal);
            for (int componentIndex = 0;
                componentIndex < componentIds.Count;
                componentIndex++)
            {
                string componentId = componentIds[componentIndex];
                if (string.Equals(
                    componentId,
                    activeDefinitionId,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                PsdUiToolkitComponentExportArtifact artifact =
                    context.Artifacts[componentId];
                builder.AppendLine(
                    $"  <ui:Template name=\"{GetComponentAlias(componentId)}\" src=\"{EscapeAttribute(ToProjectUri(artifact.GeneratedUxmlAssetPath))}\" />");
            }

            HashSet<string> externalGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PsdUiToolkitComponentInstanceConfig instance
                in context.Instances.Values)
            {
                if (instance == null
                    || string.IsNullOrEmpty(instance.externalTemplateAssetGuid)
                    || !externalGuids.Add(instance.externalTemplateAssetGuid))
                {
                    continue;
                }
            }
            List<string> orderedExternalGuids =
                new List<string>(externalGuids);
            orderedExternalGuids.Sort(StringComparer.Ordinal);
            for (int guidIndex = 0;
                guidIndex < orderedExternalGuids.Count;
                guidIndex++)
            {
                string guid = orderedExternalGuids[guidIndex];
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(
                    guid);
                if (!string.IsNullOrEmpty(path))
                {
                    builder.AppendLine(
                        $"  <ui:Template name=\"{GetExternalAlias(guid)}\" src=\"{EscapeAttribute(ToProjectUri(path))}\" />");
                }
            }
        }

        private static bool TryAppendComponentInstance(
            StringBuilder builder,
            PsdUiToolkitLayoutNode node,
            string elementName,
            int indentLevel,
            int left,
            int top,
            PsdUiToolkitFlowChildPlacement placement,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping,
            SemanticWriteContext context)
        {
            PsdUiToolkitNodeReference reference = node.Reference;
            context.Instances.TryGetValue(
                reference,
                out PsdUiToolkitComponentInstanceConfig explicitInstance);
            context.DefinitionsByRoot.TryGetValue(
                reference,
                out PsdUiToolkitComponentDefinitionConfig definitionAtRoot);

            string componentId = explicitInstance?.componentId;
            string externalGuid = explicitInstance?.externalTemplateAssetGuid;
            if (explicitInstance == null
                && definitionAtRoot != null
                && !string.Equals(
                    definitionAtRoot.id,
                    context.ActiveDefinitionId,
                    StringComparison.Ordinal))
            {
                componentId = definitionAtRoot.id;
            }

            string alias = string.Empty;
            if (!string.IsNullOrEmpty(componentId)
                && context.Artifacts.ContainsKey(componentId))
            {
                if (WouldCreateComponentCycle(
                    context,
                    context.ActiveDefinitionId,
                    componentId,
                    new HashSet<string>(StringComparer.Ordinal)))
                {
                    AddWarning(
                        context.LayoutTree,
                        $"Component instance '{elementName}' would create a template cycle and was expanded as its original PSD subtree.",
                        node.Reference,
                        "ComponentDependencyCycle");
                    return false;
                }
                alias = GetComponentAlias(componentId);
            }
            else if (!string.IsNullOrEmpty(externalGuid)
                && !string.IsNullOrEmpty(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(externalGuid)))
            {
                alias = GetExternalAlias(externalGuid);
            }

            if (string.IsNullOrEmpty(alias))
            {
                if (explicitInstance != null
                    && (!string.IsNullOrEmpty(componentId)
                        || !string.IsNullOrEmpty(externalGuid)))
                {
                    AddWarning(
                        context.LayoutTree,
                        $"Component instance '{elementName}' has a missing template and was expanded as its original PSD subtree.",
                        node.Reference,
                        "MissingComponentTemplate");
                }
                return false;
            }

            string indent = new string(' ', indentLevel * 2);
            string style = BuildInstanceStyle(
                node,
                left,
                top,
                placement,
                configMap);
            bool hasOverrides = explicitInstance?.overrides != null
                && explicitInstance.overrides.Length > 0;
            bool hasContent = explicitInstance?.contentMembers != null
                && explicitInstance.contentMembers.Length > 0;
            builder.Append(indent);
            builder.Append(
                $"<ui:Instance template=\"{EscapeAttribute(alias)}\" name=\"{EscapeAttribute(elementName)}\" style=\"{EscapeAttribute(style)}\"");
            if (!hasOverrides && !hasContent)
            {
                builder.AppendLine(" />");
                return true;
            }

            builder.AppendLine(" >");
            if (hasOverrides)
            {
                for (int i = 0; i < explicitInstance.overrides.Length; i++)
                {
                    PsdUiToolkitComponentAttributeOverrideConfig item =
                        explicitInstance.overrides[i];
                    if (item == null
                        || string.IsNullOrEmpty(item.elementName)
                        || !context.Nodes.TryGetValue(
                            item.source,
                            out PsdUiToolkitLayoutNode sourceNode)
                        || !IsDescendant(node, item.source))
                    {
                        continue;
                    }

                    string value = ResolveComponentOverrideValue(
                        item.kind,
                        sourceNode,
                        rasterResult);
                    if (string.IsNullOrEmpty(value))
                        continue;
                    string attribute = item.kind
                        == PsdUiToolkitComponentAttributeKind.Image
                            ? "image"
                            : "text";
                    builder.AppendLine(
                        $"{indent}  <ui:AttributeOverrides element-name=\"{EscapeAttribute(item.elementName)}\" {attribute}=\"{EscapeAttribute(value)}\" />");
                }
            }

            if (hasContent)
            {
                for (int i = 0; i < explicitInstance.contentMembers.Length; i++)
                {
                    if (!context.Nodes.TryGetValue(
                        explicitInstance.contentMembers[i],
                        out PsdUiToolkitLayoutNode contentNode)
                        || !IsDescendant(
                            node,
                            explicitInstance.contentMembers[i]))
                    {
                        continue;
                    }
                    AppendLayoutNode(
                        builder,
                        contentNode,
                        indentLevel + 1,
                        node.Bounds.Left,
                        node.Bounds.Top,
                        configMap,
                        rasterResult,
                        fontMapping,
                        PsdUiToolkitFlowChildPlacement.Absolute,
                        context);
                }
            }
            builder.AppendLine($"{indent}</ui:Instance>");
            return true;
        }

        private static string ResolveComponentOverrideValue(
            PsdUiToolkitComponentAttributeKind kind,
            PsdUiToolkitLayoutNode sourceNode,
            PsdUiToolkitRasterExportResult rasterResult)
        {
            if (kind == PsdUiToolkitComponentAttributeKind.Text
                && sourceNode?.SourceLayer is TypeLayer text)
            {
                return NormalizeExplicitLineBreaks(
                    text.Text,
                    out _);
            }
            if (kind == PsdUiToolkitComponentAttributeKind.Image
                && sourceNode?.SourceLayer?.LayerId != null
                && rasterResult.AssetsByLayerId.TryGetValue(
                    sourceNode.SourceLayer.LayerId.Value,
                    out PsdUiToolkitRasterAssetInfo raster))
            {
                return raster.StyleImageUri;
            }
            return string.Empty;
        }

        private static string BuildInstanceStyle(
            PsdUiToolkitLayoutNode node,
            int left,
            int top,
            PsdUiToolkitFlowChildPlacement placement,
            PsdUiToolkitLayerConfigMap configMap)
        {
            StringBuilder style = new StringBuilder(128);
            if (placement.UseFlow)
            {
                style.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "position: relative; margin: 0; width: {0}px; height: {1}px; flex-shrink: 0;",
                    node.Bounds.Width,
                    node.Bounds.Height);
                if (placement.MarginLeft > 0)
                    style.AppendFormat(CultureInfo.InvariantCulture, " margin-left: {0}px;", placement.MarginLeft);
                if (placement.MarginTop > 0)
                    style.AppendFormat(CultureInfo.InvariantCulture, " margin-top: {0}px;", placement.MarginTop);
            }
            else
            {
                style.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "position: absolute; left: {0}px; top: {1}px; width: {2}px; height: {3}px;",
                    left,
                    top,
                    node.Bounds.Width,
                    node.Bounds.Height);
            }
            if (node.SourceLayer != null)
            {
                style.AppendFormat(
                    CultureInfo.InvariantCulture,
                    " opacity: {0:0.###};",
                    node.SourceLayer.OpacityFloat);
                style.Append(configMap.IsVisible(node.SourceLayer)
                    ? " display: flex;"
                    : " display: none;");
            }
            return style.ToString().Trim();
        }

        private static bool WouldCreateComponentCycle(
            SemanticWriteContext context,
            string activeComponentId,
            string targetComponentId,
            HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(activeComponentId))
                return false;
            if (string.Equals(activeComponentId, targetComponentId, StringComparison.Ordinal))
                return true;
            if (!visited.Add(targetComponentId)
                || !context.DefinitionsById.TryGetValue(
                    targetComponentId,
                    out PsdUiToolkitComponentDefinitionConfig target)
                || !context.Nodes.TryGetValue(
                    target.root,
                    out PsdUiToolkitLayoutNode targetRoot))
            {
                return false;
            }

            HashSet<PsdUiToolkitNodeReference> descendants =
                new HashSet<PsdUiToolkitNodeReference>();
            CollectDescendantReferences(targetRoot, descendants, true);
            foreach (PsdUiToolkitComponentInstanceConfig instance
                in context.Instances.Values)
            {
                if (instance != null
                    && descendants.Contains(instance.owner)
                    && !string.IsNullOrEmpty(instance.componentId)
                    && WouldCreateComponentCycle(
                        context,
                        activeComponentId,
                        instance.componentId,
                        visited))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryPrepareButton(
            SemanticWriteContext context,
            PsdUiToolkitLayoutNode node,
            out string buttonClass)
        {
            buttonClass = string.Empty;
            if (!context.Buttons.TryGetValue(
                node.Reference,
                out PsdUiToolkitButtonSemanticConfig button))
            {
                return false;
            }
            if (node.Children.Count == 0
                || !button.TryGetState(
                    PsdUiToolkitButtonVisualState.Normal,
                    out PsdUiToolkitNodeReference normal)
                || !IsDescendant(node, normal))
            {
                if (context.InvalidButtonOwners.Add(node.Reference))
                {
                    AddWarning(
                        context.LayoutTree,
                        $"Button '{node.DisplayName}' has no valid Normal state and was exported as a regular container.",
                        node.Reference,
                        "InvalidButtonNormalState");
                }
                return false;
            }

            string id = GetReferenceToken(node.Reference);
            buttonClass = $"psd-button psd-button-{id}";
            AppendButtonRules(context, button, id, node);
            return true;
        }

        private static string GetButtonStateClasses(
            SemanticWriteContext context,
            PsdUiToolkitNodeReference reference)
        {
            List<string> classes = new List<string>();
            foreach (KeyValuePair<PsdUiToolkitNodeReference, PsdUiToolkitButtonSemanticConfig> pair
                in context.Buttons)
            {
                PsdUiToolkitButtonSemanticConfig button = pair.Value;
                if (button == null)
                    continue;
                string id = GetReferenceToken(pair.Key);
                for (int i = 0; i < button.states.Length; i++)
                {
                    PsdUiToolkitButtonStateBinding binding = button.states[i];
                    if (binding != null && binding.source.Equals(reference))
                    {
                        classes.Add($"psd-state-{id}");
                        classes.Add(
                            $"psd-state-{id}-{binding.state.ToString().ToLowerInvariant()}");
                    }
                }
            }
            return string.Join(" ", classes);
        }

        private static void AppendButtonRules(
            SemanticWriteContext context,
            PsdUiToolkitButtonSemanticConfig button,
            string id,
            PsdUiToolkitLayoutNode owner)
        {
            if (!context.WrittenButtonRules.Add(id))
                return;
            string root = $".psd-button-{id}";
            string state = $".psd-state-{id}";
            context.Uss.AppendLine(
                $"{root} {{ padding: 0; border-left-width: 0; border-top-width: 0; border-right-width: 0; border-bottom-width: 0; background-color: rgba(0, 0, 0, 0); }}");
            context.Uss.AppendLine($"{root} {state} {{ display: none; }}");
            AppendButtonStateRule(
                context.Uss,
                root,
                state,
                id,
                string.Empty,
                ResolveButtonState(
                    button,
                    PsdUiToolkitButtonVisualState.Normal,
                    owner));
            AppendButtonStateRule(
                context.Uss,
                root,
                state,
                id,
                ":focus",
                ResolveButtonState(
                    button,
                    PsdUiToolkitButtonVisualState.Focused,
                    owner));
            AppendButtonStateRule(
                context.Uss,
                root,
                state,
                id,
                ":hover",
                ResolveButtonState(
                    button,
                    PsdUiToolkitButtonVisualState.Hover,
                    owner));
            AppendButtonStateRule(
                context.Uss,
                root,
                state,
                id,
                ":active",
                ResolveButtonState(
                    button,
                    PsdUiToolkitButtonVisualState.Pressed,
                    owner));
            AppendButtonStateRule(
                context.Uss,
                root,
                state,
                id,
                ":disabled",
                ResolveButtonState(
                    button,
                    PsdUiToolkitButtonVisualState.Disabled,
                    owner));
        }

        private static PsdUiToolkitButtonVisualState ResolveButtonState(
            PsdUiToolkitButtonSemanticConfig button,
            PsdUiToolkitButtonVisualState requested,
            PsdUiToolkitLayoutNode owner)
        {
            return button.TryGetState(
                    requested,
                    out PsdUiToolkitNodeReference source)
                && IsDescendant(owner, source)
                ? requested
                : PsdUiToolkitButtonVisualState.Normal;
        }

        private static void AppendButtonStateRule(
            StringBuilder uss,
            string root,
            string stateClass,
            string id,
            string pseudo,
            PsdUiToolkitButtonVisualState visibleState)
        {
            if (!string.IsNullOrEmpty(pseudo))
                uss.AppendLine($"{root}{pseudo} {stateClass} {{ display: none; }}");
            uss.AppendLine(
                $"{root}{pseudo} .psd-state-{id}-{visibleState.ToString().ToLowerInvariant()} {{ display: flex; }}");
        }

        private static bool IsActiveContentContainer(
            SemanticWriteContext context,
            PsdUiToolkitNodeReference reference)
        {
            return !string.IsNullOrEmpty(context.ActiveDefinitionId)
                && context.DefinitionsById.TryGetValue(
                    context.ActiveDefinitionId,
                    out PsdUiToolkitComponentDefinitionConfig definition)
                && definition.hasContentContainer
                && definition.contentContainer.Equals(reference);
        }

        private static bool IsActiveExposedImage(
            SemanticWriteContext context,
            PsdUiToolkitNodeReference reference)
        {
            if (string.IsNullOrEmpty(context.ActiveDefinitionId)
                || !context.DefinitionsById.TryGetValue(
                    context.ActiveDefinitionId,
                    out PsdUiToolkitComponentDefinitionConfig definition)
                || !context.Nodes.TryGetValue(reference, out PsdUiToolkitLayoutNode node))
            {
                return false;
            }
            string name = node.DisplayName;
            for (int i = 0; i < definition.exposedElements.Length; i++)
            {
                PsdUiToolkitComponentExposedElementConfig exposed =
                    definition.exposedElements[i];
                if (exposed != null
                    && exposed.kind == PsdUiToolkitComponentAttributeKind.Image
                    && string.Equals(exposed.elementName, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsDescendant(
            PsdUiToolkitLayoutNode root,
            PsdUiToolkitNodeReference sought)
        {
            for (int i = 0; i < root.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = root.Children[i];
                if (child.Reference.Equals(sought) || IsDescendant(child, sought))
                    return true;
            }
            return false;
        }

        private static void CollectDescendantReferences(
            PsdUiToolkitLayoutNode root,
            HashSet<PsdUiToolkitNodeReference> references,
            bool includeRoot)
        {
            if (root == null)
                return;
            if (includeRoot && root.Reference.IsValid)
                references.Add(root.Reference);
            for (int i = 0; i < root.Children.Count; i++)
            {
                CollectDescendantReferences(root.Children[i], references, true);
            }
        }

        private static string JoinClasses(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
                return second ?? string.Empty;
            if (string.IsNullOrEmpty(second))
                return first;
            return first + " " + second;
        }

        private static string GetReferenceToken(PsdUiToolkitNodeReference reference)
        {
            if (reference.kind == PsdUiToolkitNodeReferenceKind.Layer)
                return $"l{Math.Max(0, reference.layerId)}";
            string id = reference.virtualGroupId ?? string.Empty;
            return "g" + id.Substring(0, Math.Min(10, id.Length));
        }

        private static string GetComponentAlias(string componentId)
        {
            string id = componentId ?? string.Empty;
            return "PsdComponent_" + id.Substring(0, Math.Min(12, id.Length));
        }

        private static string GetExternalAlias(string guid)
        {
            string value = guid ?? string.Empty;
            return "External_" + value.Substring(0, Math.Min(12, value.Length));
        }

        private static string ToProjectUri(string assetPath)
        {
            string normalized =
                PsdUiToolkitAssetPathUtility.NormalizeAssetsPath(assetPath);
            return "project:/" + normalized;
        }

        private static void WriteTextAsset(string assetPath, string text)
        {
            string diskPath = PsdUiToolkitAssetPathUtility.GetDiskPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath) ?? string.Empty);
            File.WriteAllText(diskPath, text ?? string.Empty, new UTF8Encoding(false));
        }

        private static void WriteUss(string assetPath, StringBuilder uss)
        {
            WriteTextAsset(
                assetPath,
                uss == null || uss.Length == 0
                    ? "/* Generated by PSDToUIToolKit. */\n"
                    : "/* Generated by PSDToUIToolKit. */\n" + uss);
        }

        private static void AddWarning(
            PsdUiToolkitLayoutTree tree,
            string message,
            PsdUiToolkitNodeReference reference = default,
            string code = "SemanticWarning")
        {
            if (tree == null
                || string.IsNullOrEmpty(message)
                || tree.Warnings.Contains(message))
            {
                return;
            }
            tree.Warnings.Add(message);
            if (!reference.IsValid)
                return;
            tree.Diagnostics.Add(new PsdUiToolkitLayoutDiagnostic(
                code,
                message,
                reference.kind == PsdUiToolkitNodeReferenceKind.Layer
                    ? reference.layerId
                    : -1,
                reference.kind == PsdUiToolkitNodeReferenceKind.VirtualGroup
                    ? reference.virtualGroupId
                    : string.Empty));
        }

        private static void AppendLayoutNode(
            StringBuilder builder,
            PsdUiToolkitLayoutNode node,
            int indentLevel,
            int parentLeft,
            int parentTop,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterExportResult rasterResult,
            PsdUiToolkitFontMappingLookup fontMapping,
            PsdUiToolkitFlowChildPlacement placement,
            SemanticWriteContext semanticContext)
        {
            if (node == null)
                return;
            Layer layer = node.SourceLayer;
            bool isSynthetic = node.IsSynthetic || layer == null || layer.LayerId == null;
            if (!isSynthetic && !configMap.IsExported(layer))
                return;
            if (!isSynthetic && rasterResult.SuppressedLayerIds.Contains(layer.LayerId.Value))
                return;

            PsdUiToolkitLayerBounds bounds = node.Bounds;
            int left = bounds.Left - parentLeft;
            int top = bounds.Top - parentTop;

            PsdUiToolkitRasterAssetInfo rasterInfo = null;
            bool hasRaster = !isSynthetic && rasterResult.AssetsByLayerId.TryGetValue(layer.LayerId.Value, out rasterInfo);
            bool hasChildren = node.Children.Count > 0;
            bool renderAsLeaf = !hasChildren && (isSynthetic || node.RenderAsLeaf || !layer.IsGroup || hasRaster || rasterResult.CompositeLeafLayerIds.Contains(layer.LayerId.Value));

            string indent = new string(' ', indentLevel * 2);
            string elementName = isSynthetic
                ? (string.IsNullOrEmpty(node.DisplayName) ? $"Layout_{node.OriginalIndex}" : node.DisplayName)
                : (string.IsNullOrEmpty(layer.Name) ? $"Layer_{layer.LayerId.Value}" : layer.Name);
            PsdUiToolkitFlowContainerPlan flowPlan = PsdUiToolkitFlowLayoutResolver.Resolve(node, configMap);
            if (TryAppendComponentInstance(
                builder,
                node,
                elementName,
                indentLevel,
                left,
                top,
                placement,
                configMap,
                rasterResult,
                fontMapping,
                semanticContext))
            {
                return;
            }

            string stateClasses = GetButtonStateClasses(semanticContext, node.Reference);
            bool omitInlineDisplay = !string.IsNullOrEmpty(stateClasses);
            bool isButton = TryPrepareButton(
                semanticContext,
                node,
                out string buttonClass);
            string classes = JoinClasses(buttonClass, stateClasses);
            string classAttribute = string.IsNullOrEmpty(classes)
                ? string.Empty
                : $" class=\"{EscapeAttribute(classes)}\"";
            string contentContainerAttribute =
                IsActiveContentContainer(semanticContext, node.Reference)
                    ? " content-container=\"content\""
                    : string.Empty;
            bool exposedImage = IsActiveExposedImage(
                semanticContext,
                node.Reference);
            string style = BuildStyle(
                node,
                layer,
                bounds,
                left,
                top,
                configMap,
                exposedImage ? null : rasterInfo,
                fontMapping,
                placement,
                flowPlan,
                omitInlineDisplay);

            if (!isSynthetic && layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                string rawText = NormalizeExplicitLineBreaks(typeLayer.Text, out bool hasExplicitLineBreak);

                if (PsdUiToolkitTextEffectsHelper.TryGetTextGradientCornersFromLayer(layer, out Color32 cTL, out Color32 cTR, out Color32 cBL, out Color32 cBR))
                {
                    string folder = EnsureAndGetGradientFolder();
                    string layerNameSanitized = PsdUiToolkitAssetPathUtility.SanitizeFileName(layer.Name);
                    string assetName = $"Gradient_{layer.LayerId}_{layerNameSanitized}";
                    string assetPath = $"{folder}/{assetName}.asset";
                    
                    UnityEngine.TextCore.Text.TextColorGradient gradientAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.TextColorGradient>(assetPath);
                    if (gradientAsset == null)
                    {
                        gradientAsset = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.TextCore.Text.TextColorGradient>();
                        gradientAsset.colorMode = UnityEngine.TextCore.Text.ColorGradientMode.FourCornersGradient;
                        gradientAsset.topLeft = cTL;
                        gradientAsset.topRight = cTR;
                        gradientAsset.bottomLeft = cBL;
                        gradientAsset.bottomRight = cBR;
                        UnityEditor.AssetDatabase.CreateAsset(gradientAsset, assetPath);
                    }
                    else
                    {
                        gradientAsset.colorMode = UnityEngine.TextCore.Text.ColorGradientMode.FourCornersGradient;
                        gradientAsset.topLeft = cTL;
                        gradientAsset.topRight = cTR;
                        gradientAsset.bottomLeft = cBL;
                        gradientAsset.bottomRight = cBR;
                        UnityEditor.EditorUtility.SetDirty(gradientAsset);
                    }
                    UnityEditor.AssetDatabase.SaveAssets();

                    // UI Toolkit drops rich-text gradients when an outline is set unless the vertex color is reset inline (UUM-86168).
                    rawText = $"<color=white><gradient=\"{assetName}\">{rawText}</gradient></color>";
                }

                string richTextAttr = hasExplicitLineBreak || rawText.Contains("<gradient=")
                    ? " enable-rich-text=\"true\""
                    : "";

                builder.Append(indent);
                builder.Append($"<ui:Label name=\"{EscapeAttribute(elementName)}\" text=\"{EscapeAttribute(rawText)}\"{richTextAttr}{classAttribute}{contentContainerAttribute} style=\"{EscapeAttribute(style)}\" />");
                builder.AppendLine();
                return;
            }

            if (!hasChildren)
            {
                builder.Append(indent);
                if (exposedImage && rasterInfo != null)
                {
                    builder.Append($"<ui:Image name=\"{EscapeAttribute(elementName)}\" image=\"{EscapeAttribute(rasterInfo.StyleImageUri)}\"{classAttribute}{contentContainerAttribute} style=\"{EscapeAttribute(style)}\" />");
                }
                else
                {
                    builder.Append($"<ui:VisualElement name=\"{EscapeAttribute(elementName)}\"{classAttribute}{contentContainerAttribute} style=\"{EscapeAttribute(style)}\" />");
                }
                builder.AppendLine();
                return;
            }

            builder.Append(indent);
            string containerTag = isButton ? "Button" : "VisualElement";
            string buttonTextAttribute = isButton ? " text=\"\" focusable=\"true\"" : string.Empty;
            builder.Append($"<ui:{containerTag} name=\"{EscapeAttribute(elementName)}\"{buttonTextAttribute}{classAttribute}{contentContainerAttribute} style=\"{EscapeAttribute(style)}\"");
            builder.AppendLine(" >");

            List<PsdUiToolkitLayoutNode> outputChildren =
                GetOutputChildren(node, isButton, semanticContext);
            if (flowPlan.UseFlow)
            {
                for (int i = 0; i < outputChildren.Count; i++)
                {
                    PsdUiToolkitLayoutNode child = outputChildren[i];
                    PsdUiToolkitFlowChildPlacement childPlacement = flowPlan.Placements.TryGetValue(child, out PsdUiToolkitFlowChildPlacement resolvedPlacement)
                        ? resolvedPlacement
                        : PsdUiToolkitFlowChildPlacement.Absolute;
                    AppendLayoutNode(builder, child, indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping, childPlacement, semanticContext);
                }
            }
            else
            {
                for (int i = 0; i < outputChildren.Count; i++)
                    AppendLayoutNode(builder, outputChildren[i], indentLevel + 1, bounds.Left, bounds.Top, configMap, rasterResult, fontMapping, PsdUiToolkitFlowChildPlacement.Absolute, semanticContext);
            }

            builder.Append(indent);
            builder.AppendLine($"</ui:{containerTag}>");
        }

        private static List<PsdUiToolkitLayoutNode> GetOutputChildren(
            PsdUiToolkitLayoutNode node,
            bool isButton,
            SemanticWriteContext context)
        {
            if (!isButton
                || !context.Buttons.TryGetValue(
                    node.Reference,
                    out PsdUiToolkitButtonSemanticConfig button))
            {
                return node.Children;
            }

            List<PsdUiToolkitLayoutNode> stateBranches =
                new List<PsdUiToolkitLayoutNode>();
            List<PsdUiToolkitLayoutNode> commonBranches =
                new List<PsdUiToolkitLayoutNode>();
            for (int i = 0; i < node.Children.Count; i++)
            {
                PsdUiToolkitLayoutNode child = node.Children[i];
                bool containsState = false;
                for (int stateIndex = 0;
                    stateIndex < button.states.Length;
                    stateIndex++)
                {
                    PsdUiToolkitButtonStateBinding binding =
                        button.states[stateIndex];
                    if (binding != null
                        && (child.Reference.Equals(binding.source)
                            || IsDescendant(child, binding.source)))
                    {
                        containsState = true;
                        break;
                    }
                }
                (containsState ? stateBranches : commonBranches).Add(child);
            }
            stateBranches.AddRange(commonBranches);
            return stateBranches;
        }

        private static string BuildStyle(
            PsdUiToolkitLayoutNode node,
            Layer layer,
            PsdUiToolkitLayerBounds bounds,
            int left,
            int top,
            PsdUiToolkitLayerConfigMap configMap,
            PsdUiToolkitRasterAssetInfo rasterInfo,
            PsdUiToolkitFontMappingLookup fontMapping,
            PsdUiToolkitFlowChildPlacement placement,
            PsdUiToolkitFlowContainerPlan flowPlan,
            bool omitDisplay)
        {
            StringBuilder style = new StringBuilder(256);
            if (placement.UseFlow)
            {
                style.Append("position: relative; margin: 0;");
                style.AppendFormat(CultureInfo.InvariantCulture, " width: {0}px; height: {1}px;", bounds.Width, bounds.Height);
                if (placement.MarginLeft > 0)
                    style.AppendFormat(CultureInfo.InvariantCulture, " margin-left: {0}px;", placement.MarginLeft);
                if (placement.MarginTop > 0)
                    style.AppendFormat(CultureInfo.InvariantCulture, " margin-top: {0}px;", placement.MarginTop);
                style.Append(" flex-shrink: 0;");
            }
            else
            {
                style.Append("position: absolute;");
                style.AppendFormat(CultureInfo.InvariantCulture, " left: {0}px; top: {1}px; width: {2}px; height: {3}px;", left, top, bounds.Width, bounds.Height);
            }

            if (layer != null)
            {
                style.AppendFormat(CultureInfo.InvariantCulture, " opacity: {0:0.###};", layer.OpacityFloat);
                if (!omitDisplay)
                    style.Append(configMap.IsVisible(layer) ? " display: flex;" : " display: none;");
            }
            else
            {
                style.Append(" opacity: 1;");
                if (!omitDisplay)
                    style.Append(" display: flex;");
            }

            AppendFlowContainerStyle(style, flowPlan);

            if (layer == null)
                return style.ToString().Trim();

            if (layer.Kind == LayerKind.Type)
            {
                TypeLayer typeLayer = (TypeLayer)layer;
                style.Append(placement.UseFlow ? " padding: 0;" : " margin: 0; padding: 0;");
                style.AppendFormat(CultureInfo.InvariantCulture, " font-size: {0:0.##}px;", typeLayer.EffectiveFontSize);
                style.Append(" white-space: nowrap;");
                style.Append(" -unity-text-align: middle-center;");
                string fontUri = fontMapping?.ResolveStyleUri(typeLayer.PsdFontName);
                if (!string.IsNullOrEmpty(fontUri))
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-font-definition: url('{0}');", fontUri);
                if (typeLayer.FillColor != null && typeLayer.FillColor.Length >= 4)
                {
                    int red = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[1] * 255f), 0, 255);
                    int green = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[2] * 255f), 0, 255);
                    int blue = Mathf.Clamp(Mathf.RoundToInt(typeLayer.FillColor[3] * 255f), 0, 255);
                    float alpha = Mathf.Clamp01(typeLayer.FillColor[0]);
                    style.AppendFormat(CultureInfo.InvariantCulture, " color: rgba({0}, {1}, {2}, {3:0.###});", red, green, blue, alpha);
                }

                if (PsdUiToolkitTextEffectsHelper.TryGetStrokeEffect(layer, out Color strokeColor, out float strokeSize))
                {
                    int sr = Mathf.Clamp(Mathf.RoundToInt(strokeColor.r * 255f), 0, 255);
                    int sg = Mathf.Clamp(Mathf.RoundToInt(strokeColor.g * 255f), 0, 255);
                    int sb = Mathf.Clamp(Mathf.RoundToInt(strokeColor.b * 255f), 0, 255);
                    float outlineWidth = Mathf.Clamp01(strokeSize / Mathf.Max(1f, typeLayer.EffectiveFontSize) * 2f);
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-text-outline-width: {0:0.###}px;", outlineWidth);
                    style.AppendFormat(CultureInfo.InvariantCulture, " -unity-text-outline-color: rgba({0}, {1}, {2}, {3:0.###});", sr, sg, sb, strokeColor.a);
                }

                if (PsdUiToolkitTextEffectsHelper.TryGetDropShadowEffect(layer, out Color shadowColor, out Vector2 shadowOffset, out float blurRadius))
                {
                    int sr = Mathf.Clamp(Mathf.RoundToInt(shadowColor.r * 255f), 0, 255);
                    int sg = Mathf.Clamp(Mathf.RoundToInt(shadowColor.g * 255f), 0, 255);
                    int sb = Mathf.Clamp(Mathf.RoundToInt(shadowColor.b * 255f), 0, 255);
                    style.AppendFormat(CultureInfo.InvariantCulture,
                        " text-shadow: {0:0.###}px {1:0.###}px {2:0.###}px rgba({3}, {4}, {5}, {6:0.###});",
                        shadowOffset.x, shadowOffset.y, blurRadius, sr, sg, sb, shadowColor.a);
                }

                return style.ToString().Trim();
            }

            if (rasterInfo != null && !string.IsNullOrEmpty(rasterInfo.StyleImageUri))
            {
                style.AppendFormat(CultureInfo.InvariantCulture, " background-image: url('{0}');", rasterInfo.StyleImageUri);
                style.Append(" background-repeat: no-repeat; -unity-background-scale-mode: stretch-to-fill;");
                if (rasterInfo.SliceBorder.HasValue)
                {
                    Vector4 border = rasterInfo.SliceBorder.Value;
                    style.AppendFormat(CultureInfo.InvariantCulture,
                        " -unity-slice-left: {0:0.###}; -unity-slice-bottom: {1:0.###}; -unity-slice-right: {2:0.###}; -unity-slice-top: {3:0.###};",
                        border.x, border.y, border.z, border.w);
                }
            }

            return style.ToString().Trim();
        }

        private static string NormalizeExplicitLineBreaks(string text, out bool hasExplicitLineBreak)
        {
            string value = text ?? string.Empty;
            hasExplicitLineBreak = value.IndexOf('\r') >= 0
                || value.IndexOf('\n') >= 0
                || value.IndexOf('\u2028') >= 0
                || value.IndexOf('\u2029') >= 0;
            if (!hasExplicitLineBreak)
                return value;

            return value.Replace("\r\n", "<br>")
                .Replace("\r", "<br>")
                .Replace("\n", "<br>")
                .Replace("\u2028", "<br>")
                .Replace("\u2029", "<br>");
        }

        private static void AppendFlowContainerStyle(
            StringBuilder style,
            PsdUiToolkitFlowContainerPlan flowPlan)
        {
            if (style == null || flowPlan == null || !flowPlan.UseFlow)
                return;

            switch (flowPlan.LayoutType)
            {
                case PsdUiToolkitLayoutType.Row:
                    style.Append(" flex-direction: row;");
                    AppendMainAxisDistribution(style, flowPlan.MainAxisDistribution);
                    AppendCrossAxisAlignment(style, flowPlan.CrossAxisAlignment);
                    break;
                case PsdUiToolkitLayoutType.Column:
                    style.Append(" flex-direction: column;");
                    AppendMainAxisDistribution(style, flowPlan.MainAxisDistribution);
                    AppendCrossAxisAlignment(style, flowPlan.CrossAxisAlignment);
                    break;
            }

            if (flowPlan.WrapMode == PsdUiToolkitWrapMode.Wrap)
            {
                style.Append(" flex-wrap: wrap;");
                AppendMultiLineDistribution(style, flowPlan.MultiLineDistribution);
            }

            style.AppendFormat(CultureInfo.InvariantCulture,
                " padding-left: {0}px; padding-top: {1}px; padding-right: {2}px; padding-bottom: {3}px;",
                flowPlan.PaddingLeft,
                flowPlan.PaddingTop,
                flowPlan.PaddingRight,
                flowPlan.PaddingBottom);
        }

        private static void AppendMainAxisDistribution(
            StringBuilder style,
            PsdUiToolkitMainAxisDistribution distribution)
        {
            switch (distribution)
            {
                case PsdUiToolkitMainAxisDistribution.Center:
                    style.Append(" justify-content: center;");
                    break;
                case PsdUiToolkitMainAxisDistribution.End:
                    style.Append(" justify-content: flex-end;");
                    break;
                case PsdUiToolkitMainAxisDistribution.SpaceBetween:
                    style.Append(" justify-content: space-between;");
                    break;
                case PsdUiToolkitMainAxisDistribution.SpaceAround:
                    style.Append(" justify-content: space-around;");
                    break;
                case PsdUiToolkitMainAxisDistribution.Start:
                    style.Append(" justify-content: flex-start;");
                    break;
            }
        }

        private static void AppendCrossAxisAlignment(
            StringBuilder style,
            PsdUiToolkitCrossAxisAlignment alignment)
        {
            switch (alignment)
            {
                case PsdUiToolkitCrossAxisAlignment.Center:
                    style.Append(" align-items: center;");
                    break;
                case PsdUiToolkitCrossAxisAlignment.End:
                    style.Append(" align-items: flex-end;");
                    break;
                default:
                    style.Append(" align-items: flex-start;");
                    break;
            }
        }

        private static void AppendMultiLineDistribution(
            StringBuilder style,
            PsdUiToolkitMultiLineDistribution distribution)
        {
            switch (distribution)
            {
                case PsdUiToolkitMultiLineDistribution.Center:
                    style.Append(" align-content: center;");
                    break;
                case PsdUiToolkitMultiLineDistribution.End:
                    style.Append(" align-content: flex-end;");
                    break;
                default:
                    style.Append(" align-content: flex-start;");
                    break;
            }
        }

        private static string EscapeAttribute(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }


        private static string EnsureAndGetGradientFolder()
        {
            string presetPath = "Assets/Resources/Text Color Gradients";
            var folderList = presetPath.Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);

            string currentPath = folderList[0];
            for (int i = 1; i < folderList.Length; i++)
            {
                string nextPath = currentPath + "/" + folderList[i];
                if (!UnityEditor.AssetDatabase.IsValidFolder(nextPath))
                {
                    UnityEditor.AssetDatabase.CreateFolder(currentPath, folderList[i]);
                }
                currentPath = nextPath;
            }
            return currentPath;
        }
    }
}

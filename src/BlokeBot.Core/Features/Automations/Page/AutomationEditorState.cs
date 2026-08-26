using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed class AutomationEditorState
{
    private AutomationEditorState(
        AutomationFlowId? id,
        string name,
        bool isEnabled,
        AutomationFlowCanvasSettings canvas,
        IEnumerable<AutomationEditorNode> nodes,
        IEnumerable<AutomationFlowDraftEdge> edges
    )
    {
        Id = id;
        Name = name;
        IsEnabled = isEnabled;
        Canvas = canvas;
        Nodes.AddRange(nodes);
        Edges.AddRange(edges);
    }

    internal AutomationFlowId? Id { get; set; }

    internal string Name { get; set; }

    internal bool IsEnabled { get; set; }

    internal AutomationFlowCanvasSettings Canvas { get; set; }

    internal List<AutomationEditorNode> Nodes { get; } = [];

    internal List<AutomationFlowDraftEdge> Edges { get; } = [];

    internal static AutomationEditorState Create(string name) =>
        new(null, name, false, default, [], []);

    internal static AutomationEditorState Restore(
        AutomationFlowSnapshot snapshot,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions
    ) => Restore(snapshot.Draft, definitions);

    internal static AutomationEditorState Restore(
        AutomationFlowDraft draft,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions
    ) =>
        new(
            draft.Id,
            draft.Name,
            draft.IsEnabled,
            draft.Canvas,
            draft.Nodes.Select(node => AutomationEditorNode.Restore(node, definitions[node.Id])),
            draft.Edges
        );

    internal AutomationFlowDraft Draft(AutomationHostId hostId) =>
        new(
            Id,
            hostId,
            Name,
            AutomationFlowSchema.CurrentVersion,
            IsEnabled,
            Nodes.Select(static node => node.Draft()).ToImmutableArray(),
            Edges.ToImmutableArray(),
            Canvas
        );

    internal AutomationEditorNode AddNode(AutomationDefinitionDescriptor definition)
    {
        var position = NextFreePosition();
        var node = AutomationEditorNode.Create(definition, position);
        Nodes.Add(node);
        return node;
    }

    private AutomationCanvasPosition NextFreePosition()
    {
        for (var index = 0; ; index++)
        {
            var x = 48 + (index % 3 * 240);
            var y = 72 + (index / 3 * 168);
            var candidate = new AutomationCanvasPosition(new(x), new(y));
            if (Nodes.All(node => node.Position != candidate))
            {
                return candidate;
            }
        }
    }

    internal void RemoveNode(AutomationNodeId nodeId)
    {
        _ = Nodes.RemoveAll(node => node.Id == nodeId);
        _ = Edges.RemoveAll(edge => edge.SourceNodeId == nodeId || edge.TargetNodeId == nodeId);
    }
}

public sealed partial class AutomationEditorNode
{
    private readonly Dictionary<AutomationConfigurationFieldId, string> _values;
    private readonly Dictionary<AutomationConfigurationFieldId, AutomationInputBinding> _bindings;
    private AutomationCelTransformConfiguration? _transform;

    private AutomationEditorNode(
        AutomationNodeId id,
        AutomationDefinitionDescriptor definition,
        AutomationCanvasPosition position,
        AutomationNodeFailurePolicy failurePolicy,
        string? displayAlias,
        Dictionary<AutomationConfigurationFieldId, string> values,
        Dictionary<AutomationConfigurationFieldId, AutomationInputBinding> bindings,
        AutomationCelTransformConfiguration? transform
    )
    {
        Id = id;
        Definition = definition;
        Position = position;
        FailurePolicy = failurePolicy;
        DisplayAlias = displayAlias;
        _values = values;
        _bindings = bindings;
        _transform = transform;
    }

    internal AutomationNodeId Id { get; }

    internal AutomationDefinitionDescriptor Definition { get; private set; }

    internal bool RefreshDefinition(AutomationDefinitionDescriptor current)
    {
        if (Definition.Id != current.Id)
        {
            return false;
        }
        if (
            Definition.PluginProvenance is { } previous
            && current.PluginProvenance is { } replacement
            && previous.SameCode(replacement)
        )
        {
            Definition = current;
            return true;
        }
        return Definition.PluginProvenance is null && current.PluginProvenance is null;
    }

    internal AutomationCanvasPosition Position { get; set; }

    internal AutomationNodeFailurePolicy FailurePolicy { get; set; }

    internal string? DisplayAlias { get; set; }

    internal string EffectiveName =>
        string.IsNullOrWhiteSpace(DisplayAlias) ? Definition.Display.Name : DisplayAlias;

    internal bool IsCelTransform => _transform is not null;

    internal IReadOnlyList<AutomationCelTransformInput> TransformInputs => _transform?.Inputs ?? [];

    internal IReadOnlyList<AutomationCelTransformOutput> TransformOutputs =>
        _transform?.Outputs ?? [];

    internal static AutomationEditorNode Create(
        AutomationDefinitionDescriptor definition,
        AutomationCanvasPosition position
    )
    {
        var transform =
            definition.Id == AutomationDefinitionIds.CelTransform ? DefaultTransform() : null;
        var effective = transform is null
            ? definition
            : EffectiveTransformDefinition(definition, transform);
        return new(
            new(Guid.NewGuid()),
            effective,
            position,
            AutomationNodeFailurePolicy.Stop,
            null,
            effective.Configuration.ToDictionary(
                static field => field.Id,
                field =>
                    transform is null
                        ? DefaultValue(field)
                        : DisplayFixedValue(
                            transform
                                .Inputs.Single(input => input.BindingFieldId == field.Id)
                                .FixedValue
                        )
            ),
            effective.Configuration.ToDictionary(
                static field => field.Id,
                static _ => new AutomationInputBinding(AutomationInputBindingMode.Fixed, null)
            ),
            transform
        );
    }

    internal static AutomationEditorNode Restore(
        AutomationFlowDraftNode node,
        AutomationDefinitionDescriptor definition
    )
    {
        var transform = ParseTransform(node, definition);
        return new(
            node.Id,
            definition,
            node.Position,
            node.FailurePolicy,
            node.DisplayAlias,
            definition.Configuration.ToDictionary(
                static field => field.Id,
                field =>
                    transform is null
                        ? ReadValue(node.Definition.Configuration, field.Id)
                        : DisplayFixedValue(
                            transform
                                .Inputs.Single(input => input.BindingFieldId == field.Id)
                                .FixedValue
                        )
            ),
            definition.Configuration.ToDictionary(
                static field => field.Id,
                field =>
                    node.InputBindings.GetValueOrDefault(field.Id)
                    ?? new(AutomationInputBindingMode.Fixed, null)
            ),
            transform
        );
    }

    internal string Value(AutomationConfigurationFieldId fieldId) => _values[fieldId];

    internal void SetValue(AutomationConfigurationFieldId fieldId, string value) =>
        _values[fieldId] = value;

    internal bool SetComplexFixedValue(AutomationPortId portId, string source)
    {
        if (_transform is null)
        {
            return false;
        }

        var index = IndexOf(_transform.Inputs, input => input.PortId == portId);
        if (index < 0)
        {
            return false;
        }

        var input = _transform.Inputs[index];
        if (!IsComplexFixedValue(input.ValueType))
        {
            return false;
        }

        if (
            !TryParseComplexFixedValue(
                source,
                input.ValueType,
                input.Nullability,
                out var fixedValue
            )
        )
        {
            return false;
        }

        _transform = _transform with
        {
            Inputs = _transform.Inputs.SetItem(index, input with { FixedValue = fixedValue }),
        };
        _values[input.BindingFieldId] = DisplayFixedValue(fixedValue);
        RefreshTransformDefinition();
        return true;
    }

    internal AutomationInputBinding Binding(AutomationConfigurationFieldId fieldId) =>
        _bindings[fieldId];

    internal void SetBindingMode(
        AutomationConfigurationFieldId fieldId,
        AutomationInputBindingMode mode
    ) => _bindings[fieldId] = _bindings[fieldId] with { Mode = mode };

    internal void SetExpression(
        AutomationConfigurationFieldId fieldId,
        AutomationExpressionSource expression
    ) => _bindings[fieldId] = _bindings[fieldId] with { Expression = expression };

    internal void SetDisplayAlias(string? value) => DisplayAlias = value;
}

internal enum AutomationTransformInputRenameOutcome
{
    Succeeded,
    InvalidIdentifier,
    InvalidOutput,
}

public sealed record AutomationReferenceChoice(string Value, string Label);

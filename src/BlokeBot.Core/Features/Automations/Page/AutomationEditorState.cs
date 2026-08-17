using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

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

public sealed class AutomationEditorNode
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

    internal void AddTransformInput()
    {
        if (_transform is null)
        {
            return;
        }

        var sequence = NextTransformSequence(
            _transform.Inputs.Select(static input => input.Identifier.Value),
            "input"
        );
        var input = new AutomationCelTransformInput(
            new($"input-{Guid.NewGuid():N}"),
            new(sequence),
            $"Input {_transform.Inputs.Length + 1}",
            new($"binding-{Guid.NewGuid():N}"),
            AutomationPortValueType.Text,
            AutomationPortNullability.NonNullable,
            new AutomationValue.Text(string.Empty)
        );
        _transform = _transform with { Inputs = _transform.Inputs.Add(input) };
        _values[input.BindingFieldId] = string.Empty;
        _bindings[input.BindingFieldId] = new(AutomationInputBindingMode.Fixed, null);
        RefreshTransformDefinition();
    }

    internal void AddTransformOutput()
    {
        if (_transform is null)
        {
            return;
        }

        var inputIdentifier = _transform.Inputs.FirstOrDefault()?.Identifier.Value;
        var output = new AutomationCelTransformOutput(
            new($"output-{Guid.NewGuid():N}"),
            $"Output {_transform.Outputs.Length + 1}",
            AutomationPortValueType.Text,
            AutomationPortNullability.NonNullable,
            inputIdentifier ?? "\"\""
        );
        _transform = _transform with { Outputs = _transform.Outputs.Add(output) };
        RefreshTransformDefinition();
    }

    internal void RemoveTransformInput(AutomationPortId portId)
    {
        if (
            _transform is null
            || _transform.Inputs.FirstOrDefault(input => input.PortId == portId) is not { } input
        )
        {
            return;
        }

        _transform = _transform with { Inputs = _transform.Inputs.Remove(input) };
        _ = _values.Remove(input.BindingFieldId);
        _ = _bindings.Remove(input.BindingFieldId);
        RefreshTransformDefinition();
    }

    internal void RemoveTransformOutput(AutomationPortId portId)
    {
        if (
            _transform is null
            || _transform.Outputs.FirstOrDefault(output => output.PortId == portId)
                is not { } output
        )
        {
            return;
        }

        _transform = _transform with { Outputs = _transform.Outputs.Remove(output) };
        RefreshTransformDefinition();
    }

    internal void UpdateTransformInput(
        AutomationPortId portId,
        string displayName,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    )
    {
        if (_transform is null)
        {
            return;
        }

        var index = IndexOf(_transform.Inputs, input => input.PortId == portId);
        if (index < 0)
        {
            return;
        }

        var current = _transform.Inputs[index];
        var fixedValue = ParseFixedValue(_values[current.BindingFieldId], valueType, nullability);
        _transform = _transform with
        {
            Inputs = _transform.Inputs.SetItem(
                index,
                current with
                {
                    DisplayName = displayName,
                    ValueType = valueType,
                    Nullability = nullability,
                    FixedValue = fixedValue,
                }
            ),
        };
        _values[current.BindingFieldId] = DisplayFixedValue(fixedValue);
        RefreshTransformDefinition();
    }

    internal void UpdateTransformOutput(
        AutomationPortId portId,
        string displayName,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability,
        string source
    )
    {
        if (_transform is null)
        {
            return;
        }

        var index = IndexOf(_transform.Outputs, output => output.PortId == portId);
        if (index < 0)
        {
            return;
        }

        _transform = _transform with
        {
            Outputs = _transform.Outputs.SetItem(
                index,
                _transform.Outputs[index] with
                {
                    DisplayName = displayName,
                    ValueType = valueType,
                    Nullability = nullability,
                    Source = source,
                }
            ),
        };
        RefreshTransformDefinition();
    }

    internal AutomationFlowDraftNode Draft() =>
        new(
            Id,
            new(Definition.Id.Value, Definition.Schema.Current.Value, ConfigurationJson()),
            AutomationExpressionLanguage.CurrentVersion,
            FailurePolicy,
            _bindings.ToImmutableDictionary(),
            Position,
            string.IsNullOrWhiteSpace(DisplayAlias) ? null : DisplayAlias
        );

    private JsonElement ConfigurationJson()
    {
        if (_transform is not null)
        {
            return TransformConfigurationJson();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var field in Definition.Configuration)
            {
                WriteField(writer, field, _values[field.Id]);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteField(
        Utf8JsonWriter writer,
        AutomationConfigurationFieldMetadata field,
        string value
    )
    {
        if (!field.Required && string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        switch (field.FieldType)
        {
            case AutomationConfigurationFieldType.Number:
            case AutomationConfigurationFieldType.Duration:
                if (long.TryParse(value, out var number))
                {
                    writer.WriteNumber(field.Id.Value, number);
                }
                else
                {
                    writer.WriteNull(field.Id.Value);
                }

                break;
            case AutomationConfigurationFieldType.Data data:
                WriteDataValue(
                    writer,
                    field.Id.Value,
                    data.ValueType,
                    field.Required
                        ? AutomationPortNullability.NonNullable
                        : AutomationPortNullability.Nullable,
                    value
                );
                break;
            case AutomationConfigurationFieldType.Reference
            {
                ReferenceKind: AutomationReferenceKind.CustomCommand,
            }:
                if (int.TryParse(value, out var commandId))
                {
                    writer.WriteNumber(field.Id.Value, commandId);
                }
                else
                {
                    writer.WriteNull(field.Id.Value);
                }

                break;
            default:
                writer.WriteString(field.Id.Value, value);
                break;
        }
    }

    private JsonElement TransformConfigurationJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("inputs");
            writer.WriteStartArray();
            foreach (var input in _transform!.Inputs)
            {
                writer.WriteStartObject();
                writer.WriteString("port-id", input.PortId.Value);
                writer.WriteString("cel-identifier", input.Identifier.Value);
                writer.WriteString("display-name", input.DisplayName);
                writer.WriteString("binding-field-id", input.BindingFieldId.Value);
                writer.WriteString("type", input.ValueType.ToString());
                writer.WriteString("nullability", input.Nullability.ToString());
                writer.WritePropertyName("fixed");
                WriteAutomationValue(
                    writer,
                    ParseFixedValue(
                        _values[input.BindingFieldId],
                        input.ValueType,
                        input.Nullability
                    )
                );
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("outputs");
            writer.WriteStartArray();
            foreach (var output in _transform.Outputs)
            {
                writer.WriteStartObject();
                writer.WriteString("port-id", output.PortId.Value);
                writer.WriteString("display-name", output.DisplayName);
                writer.WriteString("type", output.ValueType.ToString());
                writer.WriteString("nullability", output.Nullability.ToString());
                writer.WriteString("cel", output.Source);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private void RefreshTransformDefinition()
    {
        if (_transform is not null)
        {
            Definition = EffectiveTransformDefinition(Definition, _transform);
        }
    }

    private static AutomationCelTransformConfiguration? ParseTransform(
        AutomationFlowDraftNode node,
        AutomationDefinitionDescriptor definition
    )
    {
        if (definition.Id != AutomationDefinitionIds.CelTransform)
        {
            return null;
        }

        var parser = AutomationCelTransform.Definition(definition.Id, definition.Display);
        return
            parser.Parse(node.Definition.Configuration)
                is AutomationConfigurationParseResult.Parsed
                {
                    Configuration: AutomationCelTransformConfiguration configuration,
                }
            ? configuration
            : null;
    }

    private static AutomationDefinitionDescriptor EffectiveTransformDefinition(
        AutomationDefinitionDescriptor registered,
        AutomationCelTransformConfiguration configuration
    )
    {
        var definition = AutomationCelTransform.Definition(registered.Id, registered.Display);
        return ((IAutomationEffectiveDefinition)definition).EffectiveDescriptor(configuration);
    }

    private static AutomationCelTransformConfiguration DefaultTransform() =>
        new(
            [
                new(
                    new("input-value"),
                    new("value"),
                    "Value",
                    new("binding-value"),
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Text(string.Empty)
                ),
            ],
            [
                new(
                    new("output-result"),
                    "Result",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "value"
                ),
            ]
        );

    private static string NextTransformSequence(IEnumerable<string> values, string prefix)
    {
        var existing = values.ToHashSet(StringComparer.Ordinal);
        for (var sequence = 1; ; sequence++)
        {
            var candidate = $"{prefix}_{sequence}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static void WriteDataValue(
        Utf8JsonWriter writer,
        string name,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability,
        string value
    )
    {
        writer.WritePropertyName(name);
        WriteAutomationValue(writer, ParseFixedValue(value, valueType, nullability));
    }

    private static AutomationValue ParseFixedValue(
        string value,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    ) =>
        nullability == AutomationPortNullability.Nullable && string.IsNullOrWhiteSpace(value)
            ? new AutomationValue.Null(valueType)
            : valueType switch
            {
                AutomationPortValueType.Text => new AutomationValue.Text(value),
                AutomationPortValueType.Number
                    when decimal.TryParse(
                        value,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var number
                    ) => new AutomationValue.Number(number),
                AutomationPortValueType.Boolean when bool.TryParse(value, out var boolean) =>
                    new AutomationValue.Boolean(boolean),
                AutomationPortValueType.Timestamp
                    when DateTimeOffset.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var timestamp
                    ) => new AutomationValue.Timestamp(timestamp),
                AutomationPortValueType.Arguments => new AutomationValue.Arguments([]),
                AutomationPortValueType.Actor => new AutomationValue.Actor(
                    new(string.Empty, string.Empty)
                ),
                AutomationPortValueType.Channel => new AutomationValue.Channel(
                    new(string.Empty, string.Empty)
                ),
                AutomationPortValueType.Stream => new AutomationValue.Stream(new(null, null, null)),
                _ => new AutomationValue.Null(valueType),
            };

    private static string DisplayFixedValue(AutomationValue value) =>
        value switch
        {
            AutomationValue.Text text => text.Value,
            AutomationValue.Number number => number.Value.ToString(CultureInfo.InvariantCulture),
            AutomationValue.Boolean boolean => boolean.Value.ToString(),
            AutomationValue.Timestamp timestamp => timestamp.Value.ToString(
                "O",
                CultureInfo.InvariantCulture
            ),
            AutomationValue.Arguments => "[]",
            AutomationValue.Actor actor => $"{actor.Value.Login} · {actor.Value.DisplayName}",
            AutomationValue.Channel channel =>
                $"{channel.Value.Login} · {channel.Value.DisplayName}",
            AutomationValue.Stream => string.Empty,
            AutomationValue.Null => string.Empty,
            _ => string.Empty,
        };

    private static void WriteAutomationValue(Utf8JsonWriter writer, AutomationValue value)
    {
        switch (value)
        {
            case AutomationValue.Text text:
                writer.WriteStringValue(text.Value);
                break;
            case AutomationValue.Number number:
                writer.WriteNumberValue(number.Value);
                break;
            case AutomationValue.Boolean boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case AutomationValue.Timestamp timestamp:
                writer.WriteStringValue(timestamp.Value);
                break;
            case AutomationValue.Actor actor:
                writer.WriteStartObject();
                writer.WriteString("login", actor.Value.Login);
                writer.WriteString("display-name", actor.Value.DisplayName);
                writer.WriteEndObject();
                break;
            case AutomationValue.Channel channel:
                writer.WriteStartObject();
                writer.WriteString("login", channel.Value.Login);
                writer.WriteString("display-name", channel.Value.DisplayName);
                writer.WriteEndObject();
                break;
            case AutomationValue.Stream stream:
                writer.WriteStartObject();
                writer.WriteString("title", stream.Value.Title);
                writer.WriteString("game-name", stream.Value.GameName);
                if (stream.Value.StartedAtUtc is { } startedAt)
                {
                    writer.WriteString(
                        "started-at",
                        startedAt.ToString("O", CultureInfo.InvariantCulture)
                    );
                }
                else
                {
                    writer.WriteNull("started-at");
                }
                writer.WriteEndObject();
                break;
            case AutomationValue.Arguments arguments:
                writer.WriteStartArray();
                foreach (var argument in arguments.Values)
                {
                    writer.WriteStringValue(argument.Value);
                }
                writer.WriteEndArray();
                break;
            case AutomationValue.Null:
                writer.WriteNullValue();
                break;
        }
    }

    private static int IndexOf<T>(ImmutableArray<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string DefaultValue(AutomationConfigurationFieldMetadata field) =>
        field.FieldType switch
        {
            AutomationConfigurationFieldType.Number number => number.Minimum.ToString(
                CultureInfo.InvariantCulture
            ),
            AutomationConfigurationFieldType.Duration duration => field.Id.Value.EndsWith(
                "milliseconds",
                StringComparison.Ordinal
            )
                ? ((long)duration.Minimum.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
                : ((long)duration.Minimum.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            AutomationConfigurationFieldType.Choice choice => choice.Values[0],
            AutomationConfigurationFieldType.Data { ValueType: AutomationPortValueType.Number } =>
                "0",
            AutomationConfigurationFieldType.Data { ValueType: AutomationPortValueType.Boolean } =>
                bool.FalseString,
            AutomationConfigurationFieldType.Data
            {
                ValueType: AutomationPortValueType.Timestamp,
            } => DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

    private static string ReadValue(JsonElement configuration, AutomationConfigurationFieldId id) =>
        configuration.TryGetProperty(id.Value, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => string.Empty,
            }
            : string.Empty;
}

public sealed record AutomationReferenceChoice(string Value, string Label);

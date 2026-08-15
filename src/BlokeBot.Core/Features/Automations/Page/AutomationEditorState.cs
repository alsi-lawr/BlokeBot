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
        IEnumerable<AutomationEditorNode> nodes,
        IEnumerable<AutomationFlowDraftEdge> edges
    )
    {
        Id = id;
        Name = name;
        IsEnabled = isEnabled;
        Nodes.AddRange(nodes);
        Edges.AddRange(edges);
    }

    internal AutomationFlowId? Id { get; set; }

    internal string Name { get; set; }

    internal bool IsEnabled { get; set; }

    internal List<AutomationEditorNode> Nodes { get; } = [];

    internal List<AutomationFlowDraftEdge> Edges { get; } = [];

    internal static AutomationEditorState Create(string name) => new(null, name, false, [], []);

    internal static AutomationEditorState Restore(
        AutomationFlowSnapshot snapshot,
        IReadOnlyDictionary<AutomationDefinitionId, AutomationDefinitionDescriptor> definitions
    ) =>
        new(
            snapshot.Draft.Id,
            snapshot.Draft.Name,
            snapshot.Draft.IsEnabled,
            snapshot.Draft.Nodes.Select(node =>
                AutomationEditorNode.Restore(node, definitions[new(node.Definition.TypeId)])
            ),
            snapshot.Draft.Edges
        );

    internal AutomationFlowDraft Draft(AutomationHostId hostId) =>
        new(
            Id,
            hostId,
            Name,
            AutomationFlowSchema.CurrentVersion,
            IsEnabled,
            Nodes.Select(static node => node.Draft()).ToImmutableArray(),
            Edges.ToImmutableArray()
        );

    internal AutomationEditorNode AddNode(AutomationDefinitionDescriptor definition)
    {
        var column = Nodes.Count % 3;
        var row = Nodes.Count / 3;
        var node = AutomationEditorNode.Create(
            definition,
            new(new(48 + (column * 240)), new(72 + (row * 168)))
        );
        Nodes.Add(node);
        return node;
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

    private AutomationEditorNode(
        AutomationNodeId id,
        AutomationDefinitionDescriptor definition,
        AutomationCanvasPosition position,
        AutomationNodeFailurePolicy failurePolicy,
        Dictionary<AutomationConfigurationFieldId, string> values
    )
    {
        Id = id;
        Definition = definition;
        Position = position;
        FailurePolicy = failurePolicy;
        _values = values;
    }

    internal AutomationNodeId Id { get; }

    internal AutomationDefinitionDescriptor Definition { get; }

    internal AutomationCanvasPosition Position { get; set; }

    internal AutomationNodeFailurePolicy FailurePolicy { get; set; }

    internal static AutomationEditorNode Create(
        AutomationDefinitionDescriptor definition,
        AutomationCanvasPosition position
    ) =>
        new(
            new(Guid.NewGuid()),
            definition,
            position,
            AutomationNodeFailurePolicy.Stop,
            definition.Configuration.ToDictionary(
                static field => field.Id,
                static field => DefaultValue(field)
            )
        );

    internal static AutomationEditorNode Restore(
        AutomationFlowDraftNode node,
        AutomationDefinitionDescriptor definition
    ) =>
        new(
            node.Id,
            definition,
            node.Position,
            node.FailurePolicy,
            definition.Configuration.ToDictionary(
                static field => field.Id,
                field => ReadValue(node.Definition.Configuration, field.Id)
            )
        );

    internal string Value(AutomationConfigurationFieldId fieldId) => _values[fieldId];

    internal void SetValue(AutomationConfigurationFieldId fieldId, string value) =>
        _values[fieldId] = value;

    internal AutomationFlowDraftNode Draft() =>
        new(
            Id,
            new(Definition.Id.Value, Definition.Schema.Current.Value, ConfigurationJson()),
            AutomationExpressionLanguage.CurrentVersion,
            FailurePolicy,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource>.Empty,
            Position
        );

    private JsonElement ConfigurationJson()
    {
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

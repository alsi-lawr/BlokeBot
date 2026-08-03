using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationContextRestoreOutcome
{
    private AutomationContextRestoreOutcome() { }

    internal sealed record Available(AutomationContext Context) : AutomationContextRestoreOutcome;

    internal sealed record Unsupported : AutomationContextRestoreOutcome;
}

internal static class AutomationRuntimeSerialization
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    internal static string SerializeExpressions(
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> expressions
    ) =>
        JsonSerializer.Serialize(
            expressions.ToDictionary(
                static pair => pair.Key.Value,
                static pair => new PersistedExpression(
                    pair.Value.LanguageVersion.Value,
                    pair.Value.Source
                ),
                StringComparer.Ordinal
            ),
            _options
        );

    internal static ImmutableDictionary<
        AutomationConfigurationFieldId,
        AutomationExpressionSource
    > DeserializeExpressions(string json) =>
        JsonSerializer
            .Deserialize<Dictionary<string, PersistedExpression>>(json, _options)!
            .ToImmutableDictionary(
                static pair => new AutomationConfigurationFieldId(pair.Key),
                static pair => new AutomationExpressionSource(
                    new(pair.Value.LanguageVersion),
                    pair.Value.Source
                )
            );

    internal static string SerializeContext(AutomationContext context) =>
        JsonSerializer.Serialize(
            new PersistedContext(
                context.Event.OccurrenceId,
                context.Event.SourceDefinitionId.Value,
                context.Actor,
                context.Channel,
                context.Stream,
                context.Timestamps,
                context.Arguments,
                context
                    .Variables.ForExecution()
                    .Select(static pair => Persist(pair.Key, pair.Value))
                    .ToImmutableArray()
            ),
            _options
        );

    internal static AutomationContextRestoreOutcome RestoreContext(int schemaVersion, string json)
    {
        if (schemaVersion != AutomationContextSchema.CurrentVersion)
        {
            return new AutomationContextRestoreOutcome.Unsupported();
        }

        var context = JsonSerializer.Deserialize<PersistedContext>(json, _options)!;
        return new AutomationContextRestoreOutcome.Available(
            new(
                new(context.OccurrenceId, new(context.SourceDefinitionId)),
                context.Actor,
                context.Channel,
                context.Stream,
                context.Timestamps,
                context.Arguments,
                new(
                    context.Variables.Select(static variable => new KeyValuePair<
                        AutomationVariableName,
                        AutomationVariable
                    >(new(variable.Name), Restore(variable)))
                )
            )
        );
    }

    internal static string SerializeDefinition(AutomationFlow flow) =>
        JsonSerializer.Serialize(
            new PersistedFlow(
                flow.Id,
                flow.HostId,
                flow.SchemaVersion,
                flow.Nodes.Select(static node => new PersistedNode(
                        node.Id,
                        node.DefinitionId,
                        node.DefinitionSchemaVersion,
                        node.ConfigurationJson,
                        node.FieldExpressionsJson,
                        node.ExpressionLanguageVersion,
                        node.ContinueOnFailure
                    ))
                    .ToImmutableArray(),
                flow.Edges.Select(static edge => new PersistedEdge(
                        edge.Id,
                        edge.SourceNodeId,
                        edge.SourcePortId,
                        edge.TargetNodeId,
                        edge.TargetPortId
                    ))
                    .ToImmutableArray()
            ),
            _options
        );

    internal static PersistedFlow DeserializeDefinition(string json) =>
        JsonSerializer.Deserialize<PersistedFlow>(json, _options)!;

    internal static PersistedAutomationNodeDefinition Definition(PersistedNode node) =>
        new(
            node.DefinitionId,
            node.DefinitionSchemaVersion,
            JsonDocument.Parse(node.ConfigurationJson).RootElement.Clone()
        );

    private static PersistedVariable Persist(
        AutomationVariableName name,
        AutomationVariable variable
    ) =>
        variable.Value switch
        {
            AutomationValue.Text text => new(
                name.Value,
                "text",
                JsonSerializer.Serialize(text.Value, _options),
                variable.Sensitivity
            ),
            AutomationValue.Number number => new(
                name.Value,
                "number",
                JsonSerializer.Serialize(number.Value, _options),
                variable.Sensitivity
            ),
            AutomationValue.Boolean boolean => new(
                name.Value,
                "boolean",
                JsonSerializer.Serialize(boolean.Value, _options),
                variable.Sensitivity
            ),
            AutomationValue.Timestamp timestamp => new(
                name.Value,
                "timestamp",
                JsonSerializer.Serialize(timestamp.Value, _options),
                variable.Sensitivity
            ),
            AutomationValue.Actor actor => new(
                name.Value,
                "actor",
                JsonSerializer.Serialize(actor.Value, _options),
                variable.Sensitivity
            ),
            AutomationValue.Channel channel => new(
                name.Value,
                "channel",
                JsonSerializer.Serialize(channel.Value, _options),
                variable.Sensitivity
            ),
            AutomationValue.Stream stream => new(
                name.Value,
                "stream",
                JsonSerializer.Serialize(stream.Value, _options),
                variable.Sensitivity
            ),
            _ => throw new InvalidOperationException("Unknown automation variable value."),
        };

    private static AutomationVariable Restore(PersistedVariable variable) =>
        new(
            variable.Kind switch
            {
                "text" => new AutomationValue.Text(
                    JsonSerializer.Deserialize<string>(variable.ValueJson, _options)!
                ),
                "number" => new AutomationValue.Number(
                    JsonSerializer.Deserialize<decimal>(variable.ValueJson, _options)
                ),
                "boolean" => new AutomationValue.Boolean(
                    JsonSerializer.Deserialize<bool>(variable.ValueJson, _options)
                ),
                "timestamp" => new AutomationValue.Timestamp(
                    JsonSerializer.Deserialize<DateTimeOffset>(variable.ValueJson, _options)
                ),
                "actor" => new AutomationValue.Actor(
                    JsonSerializer.Deserialize<AutomationActor>(variable.ValueJson, _options)!
                ),
                "channel" => new AutomationValue.Channel(
                    JsonSerializer.Deserialize<AutomationChannel>(variable.ValueJson, _options)!
                ),
                "stream" => new AutomationValue.Stream(
                    JsonSerializer.Deserialize<AutomationStream>(variable.ValueJson, _options)!
                ),
                _ => throw new InvalidOperationException("Unknown persisted automation value."),
            },
            variable.Sensitivity
        );

    private sealed record PersistedExpression(int LanguageVersion, string Source);

    private sealed record PersistedContext(
        Guid OccurrenceId,
        string SourceDefinitionId,
        AutomationActor? Actor,
        AutomationChannel Channel,
        AutomationStream? Stream,
        AutomationTimestamps Timestamps,
        ImmutableArray<AutomationArgument> Arguments,
        ImmutableArray<PersistedVariable> Variables
    );

    private sealed record PersistedVariable(
        string Name,
        string Kind,
        string ValueJson,
        AutomationDataSensitivity Sensitivity
    );

    internal sealed record PersistedFlow(
        Guid Id,
        int HostId,
        int SchemaVersion,
        ImmutableArray<PersistedNode> Nodes,
        ImmutableArray<PersistedEdge> Edges
    );

    internal sealed record PersistedNode(
        Guid Id,
        string DefinitionId,
        int DefinitionSchemaVersion,
        string ConfigurationJson,
        string FieldExpressionsJson,
        int ExpressionLanguageVersion,
        bool ContinueOnFailure
    );

    internal sealed record PersistedEdge(
        Guid Id,
        Guid SourceNodeId,
        string SourcePortId,
        Guid TargetNodeId,
        string TargetPortId
    );
}

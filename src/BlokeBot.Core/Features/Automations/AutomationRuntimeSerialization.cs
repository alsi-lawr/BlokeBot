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

internal abstract record AutomationInputBindingsRestoreOutcome
{
    private AutomationInputBindingsRestoreOutcome() { }

    internal sealed record Available(
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding> Bindings
    ) : AutomationInputBindingsRestoreOutcome;

    internal sealed record Invalid : AutomationInputBindingsRestoreOutcome;
}

internal abstract record AutomationDefinitionRestoreOutcome
{
    private AutomationDefinitionRestoreOutcome() { }

    internal sealed record Available(AutomationRuntimeSerialization.PersistedFlow Flow)
        : AutomationDefinitionRestoreOutcome;

    internal sealed record Invalid : AutomationDefinitionRestoreOutcome;
}

internal static class AutomationRuntimeSerialization
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    internal static string SerializeInputBindings(
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding> bindings
    ) =>
        JsonSerializer.Serialize(
            bindings.ToDictionary(
                static pair => pair.Key.Value,
                static pair => new PersistedInputBinding(
                    pair.Value.Mode.ToString(),
                    pair.Value.Expression is { } expression
                        ? new PersistedExpression(
                            expression.LanguageVersion.Value,
                            expression.Source
                        )
                        : null
                ),
                StringComparer.Ordinal
            ),
            _options
        );

    internal static AutomationInputBindingsRestoreOutcome RestoreInputBindings(string json)
    {
        Dictionary<string, PersistedInputBinding>? persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<Dictionary<string, PersistedInputBinding>>(
                json,
                _options
            );
        }
        catch (JsonException)
        {
            return new AutomationInputBindingsRestoreOutcome.Invalid();
        }
        catch (NotSupportedException)
        {
            return new AutomationInputBindingsRestoreOutcome.Invalid();
        }

        if (persisted is null)
        {
            return new AutomationInputBindingsRestoreOutcome.Invalid();
        }

        var bindings = ImmutableDictionary.CreateBuilder<
            AutomationConfigurationFieldId,
            AutomationInputBinding
        >();
        foreach (var (fieldId, binding) in persisted)
        {
            if (
                string.IsNullOrWhiteSpace(fieldId)
                || binding is null
                || !Enum.TryParse<AutomationInputBindingMode>(binding.Mode, out var mode)
                || !Enum.IsDefined(mode)
                || binding.Mode != mode.ToString()
                || (mode == AutomationInputBindingMode.Expression && binding.Expression is null)
                || (
                    binding.Expression is { } expression
                    && (
                        expression.LanguageVersion <= 0
                        || string.IsNullOrWhiteSpace(expression.Source)
                    )
                )
            )
            {
                return new AutomationInputBindingsRestoreOutcome.Invalid();
            }

            bindings.Add(
                new(fieldId),
                new(
                    mode,
                    binding.Expression is { } source
                        ? new(new(source.LanguageVersion), source.Source)
                        : null
                )
            );
        }

        return new AutomationInputBindingsRestoreOutcome.Available(bindings.ToImmutable());
    }

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
                        node.InputBindingsJson,
                        node.ExpressionLanguageVersion,
                        node.ContinueOnFailure
                    ))
                    .ToImmutableArray(),
                flow.Edges.Select(static edge => new PersistedEdge(
                        edge.Id,
                        Restore(edge.Kind),
                        edge.SourceNodeId,
                        edge.SourcePortId,
                        edge.TargetNodeId,
                        edge.TargetPortId
                    ))
                    .ToImmutableArray()
            ),
            _options
        );

    internal static AutomationDefinitionRestoreOutcome RestoreDefinition(string json)
    {
        PersistedFlow? flow;
        try
        {
            flow = JsonSerializer.Deserialize<PersistedFlow>(json, _options);
        }
        catch (JsonException)
        {
            return new AutomationDefinitionRestoreOutcome.Invalid();
        }
        catch (NotSupportedException)
        {
            return new AutomationDefinitionRestoreOutcome.Invalid();
        }

        return (
            flow is null
            || flow.SchemaVersion != AutomationFlowSchema.CurrentVersion
            || flow.Nodes.IsDefault
            || flow.Edges.IsDefault
            || flow.Nodes.Any(static node =>
                node.Id == Guid.Empty || !IsValidJson(node.ConfigurationJson)
            )
            || flow.Nodes.Select(static node => node.Id).Distinct().Count() != flow.Nodes.Length
            || flow.Nodes.Any(static node =>
                RestoreInputBindings(node.InputBindingsJson)
                is AutomationInputBindingsRestoreOutcome.Invalid
            )
            || flow.Edges.Any(static edge => edge.Id == Guid.Empty)
            || flow.Edges.Select(static edge => edge.Id).Distinct().Count() != flow.Edges.Length
            || flow.Edges.Any(static edge => !Enum.IsDefined(edge.Kind))
            || flow.Edges.Any(edge =>
                flow.Nodes.All(node => node.Id != edge.SourceNodeId)
                || flow.Nodes.All(node => node.Id != edge.TargetNodeId)
            )
        )
            ? new AutomationDefinitionRestoreOutcome.Invalid()
            : new AutomationDefinitionRestoreOutcome.Available(flow);
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
            AutomationValue.Arguments arguments => new(
                name.Value,
                "arguments",
                JsonSerializer.Serialize(arguments.Values, _options),
                variable.Sensitivity
            ),
            AutomationValue.Null nullValue
                when nullValue.ValueType != AutomationPortValueType.Flow => new(
                name.Value,
                "null",
                JsonSerializer.Serialize(nullValue.ValueType, _options),
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
                    JsonSerializer.Deserialize<AutomationPublicActor>(variable.ValueJson, _options)!
                ),
                "channel" => new AutomationValue.Channel(
                    JsonSerializer.Deserialize<AutomationPublicChannel>(
                        variable.ValueJson,
                        _options
                    )!
                ),
                "stream" => new AutomationValue.Stream(
                    JsonSerializer.Deserialize<AutomationPublicStream>(
                        variable.ValueJson,
                        _options
                    )!
                ),
                "arguments" => new AutomationValue.Arguments(
                    JsonSerializer.Deserialize<ImmutableArray<AutomationValueArgument>>(
                        variable.ValueJson,
                        _options
                    )
                ),
                "null" => RestoreNull(variable.ValueJson),
                _ => throw new InvalidOperationException("Unknown persisted automation value."),
            },
            variable.Sensitivity
        );

    private static AutomationValue RestoreNull(string json)
    {
        var valueType = JsonSerializer.Deserialize<AutomationPortValueType>(json, _options);
        return valueType != AutomationPortValueType.Flow && Enum.IsDefined(valueType)
            ? new AutomationValue.Null(valueType)
            : throw new InvalidOperationException("Unknown persisted automation null type.");
    }

    private sealed record PersistedExpression(int LanguageVersion, string Source);

    private sealed record PersistedInputBinding(string Mode, PersistedExpression? Expression);

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
        string InputBindingsJson,
        int ExpressionLanguageVersion,
        bool ContinueOnFailure
    );

    internal sealed record PersistedEdge(
        Guid Id,
        AutomationEdgeKind Kind,
        Guid SourceNodeId,
        string SourcePortId,
        Guid TargetNodeId,
        string TargetPortId
    );

    private static AutomationEdgeKind Restore(PersistedAutomationEdgeKind kind) =>
        kind switch
        {
            PersistedAutomationEdgeKind.Flow => AutomationEdgeKind.Flow,
            PersistedAutomationEdgeKind.Data => AutomationEdgeKind.Data,
            _ => (AutomationEdgeKind)(-1),
        };
}

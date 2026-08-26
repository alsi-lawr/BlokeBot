using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationInputResolution
{
    private AutomationInputResolution() { }

    internal sealed record Available(
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> PortValues,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> FieldValues
    ) : AutomationInputResolution;

    internal sealed record Failed(string Code) : AutomationInputResolution;
}

internal abstract record AutomationPureCheckpoint
{
    private AutomationPureCheckpoint() { }

    internal sealed record Begin : AutomationPureCheckpoint;

    internal sealed record Available(
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> Outputs
    ) : AutomationPureCheckpoint;

    internal sealed record Failed : AutomationPureCheckpoint;
}

internal interface IAutomationPureCheckpointStore
{
    ValueTask<AutomationPureCheckpoint> ReadOrBeginAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        CancellationToken cancellationToken
    );

    ValueTask<bool> CompleteAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> outputs,
        CancellationToken cancellationToken
    );

    ValueTask FailAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        string code,
        CancellationToken cancellationToken
    );
}

internal sealed class AutomationDataResolver(
    AutomationCatalogService catalog,
    AutomationPureHandlerRegistry handlers,
    AutomationExpressionService expressions,
    IAutomationIntegerEntropy integerEntropy,
    PluginAutomationExecutionService? pluginExecution = null
)
{
    private readonly AutomationSafeTriggerExpressionService _safeExpressions = new();

    internal async ValueTask<AutomationInputResolution> ResolveInputsAsync(
        AutomationHostId hostId,
        AutomationContext context,
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode consumer,
        IAutomationPureCheckpointStore checkpoints,
        CancellationToken cancellationToken
    ) =>
        await ResolveInputsAsync(
            hostId,
            context,
            flow,
            consumer,
            checkpoints,
            [],
            integerEntropy,
            cancellationToken
        );

    internal async ValueTask<AutomationInputResolution> ResolveSampleInputsAsync(
        AutomationHostId hostId,
        AutomationContext context,
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode consumer,
        IAutomationPureCheckpointStore checkpoints,
        IAutomationIntegerEntropy sampleIntegerEntropy,
        CancellationToken cancellationToken
    ) =>
        await ResolveInputsAsync(
            hostId,
            context,
            flow,
            consumer,
            checkpoints,
            [],
            sampleIntegerEntropy,
            cancellationToken
        );

    private async ValueTask<AutomationInputResolution> ResolveInputsAsync(
        AutomationHostId hostId,
        AutomationContext context,
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode consumer,
        IAutomationPureCheckpointStore checkpoints,
        ImmutableHashSet<Guid> resolving,
        IAutomationIntegerEntropy executionIntegerEntropy,
        CancellationToken cancellationToken
    )
    {
        if (
            AutomationRuntimeSerialization.RestoreInputBindings(consumer.InputBindingsJson)
                is not AutomationInputBindingsRestoreOutcome.Available bindings
            || catalog.ValidatePersistedDefinition(
                AutomationRuntimeSerialization.Definition(consumer)
            )
                is not AutomationConfigurationCheck.Valid valid
        )
        {
            return new AutomationInputResolution.Failed("binding-invalid");
        }

        var portValues = ImmutableDictionary.CreateBuilder<
            AutomationPortId,
            AutomationResolvedValue
        >();
        var fieldValues = ImmutableDictionary.CreateBuilder<
            AutomationConfigurationFieldId,
            AutomationResolvedValue
        >();
        foreach (
            var input in valid.Definition.Inputs.Where(static port =>
                port.ValueType != AutomationPortValueType.Flow && port.BindingFieldId is not null
            )
        )
        {
            if (!bindings.Bindings.TryGetValue(input.BindingFieldId!.Value, out var binding))
            {
                return new AutomationInputResolution.Failed("binding-invalid");
            }

            AutomationResolvedValue? resolved;
            if (
                binding.Mode == AutomationInputBindingMode.Fixed
                && valid.Configuration is AutomationCelTransformConfiguration transform
            )
            {
                var declared = transform.Inputs.Single(candidate => candidate.PortId == input.Id);
                resolved = new(declared.FixedValue, [AutomationValueProvenance.Generated]);
            }
            else if (binding.Mode == AutomationInputBindingMode.Fixed)
            {
                resolved = ResolveFixed(valid.Configuration, input);
            }
            else if (
                binding.Mode == AutomationInputBindingMode.Expression
                && valid.Configuration is AutomationCelTransformConfiguration
            )
            {
                resolved = _safeExpressions.Evaluate(binding.Expression!, input, context);
            }
            else if (binding.Mode == AutomationInputBindingMode.Expression)
            {
                resolved = ResolveExpression(binding.Expression!, input, context);
            }
            else
            {
                var edges = flow
                    .Edges.Where(edge =>
                        edge.Kind == AutomationEdgeKind.Data
                        && edge.TargetNodeId == consumer.Id
                        && edge.TargetPortId == input.Id.Value
                    )
                    .ToArray();
                if (edges.Length != 1)
                {
                    return new AutomationInputResolution.Failed("input-resolution-unavailable");
                }

                var edge = edges[0];
                var producer = flow.Nodes.SingleOrDefault(node => node.Id == edge.SourceNodeId);
                if (producer is null)
                {
                    return new AutomationInputResolution.Failed("input-resolution-unavailable");
                }

                resolved = await ResolveOutputAsync(
                    hostId,
                    context,
                    flow,
                    producer,
                    new(edge.SourcePortId),
                    checkpoints,
                    resolving,
                    executionIntegerEntropy,
                    cancellationToken
                );
            }

            if (resolved is null || !Matches(input, resolved.Value))
            {
                return new AutomationInputResolution.Failed("input-resolution-unavailable");
            }

            portValues.Add(input.Id, resolved);
            fieldValues.Add(input.BindingFieldId.Value, resolved);
        }

        return new AutomationInputResolution.Available(
            portValues.ToImmutable(),
            fieldValues.ToImmutable()
        );
    }

    private async ValueTask<AutomationResolvedValue?> ResolveOutputAsync(
        AutomationHostId hostId,
        AutomationContext context,
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode producer,
        AutomationPortId outputPortId,
        IAutomationPureCheckpointStore checkpoints,
        ImmutableHashSet<Guid> resolving,
        IAutomationIntegerEntropy executionIntegerEntropy,
        CancellationToken cancellationToken
    )
    {
        if (
            catalog.ValidatePersistedDefinition(AutomationRuntimeSerialization.Definition(producer))
            is not AutomationConfigurationCheck.Valid valid
        )
        {
            return null;
        }

        var descriptor = valid.Definition;
        if (
            descriptor.Outputs.SingleOrDefault(port => port.Id == outputPortId)
                is not { } outputPort
            || outputPort.ValueType == AutomationPortValueType.Flow
        )
        {
            return null;
        }

        if (descriptor.Kind == AutomationNodeKind.Source)
        {
            return ResolveSource(outputPort, context);
        }

        if (descriptor.Kind is not (AutomationNodeKind.Value or AutomationNodeKind.Transform))
        {
            return null;
        }

        var outputs = await ResolvePureNodeAsync(
            hostId,
            context,
            flow,
            producer,
            descriptor,
            checkpoints,
            resolving,
            executionIntegerEntropy,
            cancellationToken
        );
        return outputs?.GetValueOrDefault(outputPortId);
    }

    private async ValueTask<ImmutableDictionary<
        AutomationPortId,
        AutomationResolvedValue
    >?> ResolvePureNodeAsync(
        AutomationHostId hostId,
        AutomationContext context,
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode producer,
        AutomationDefinitionDescriptor descriptor,
        IAutomationPureCheckpointStore checkpoints,
        ImmutableHashSet<Guid> resolving,
        IAutomationIntegerEntropy executionIntegerEntropy,
        CancellationToken cancellationToken
    )
    {
        if (resolving.Contains(producer.Id))
        {
            return null;
        }

        var checkpoint = await checkpoints.ReadOrBeginAsync(producer, cancellationToken);
        if (checkpoint is AutomationPureCheckpoint.Failed)
        {
            return null;
        }

        var check = await catalog.ValidatePersistedBeforeExecutionAsync(
            hostId,
            context,
            AutomationRuntimeSerialization.Definition(producer),
            cancellationToken
        );
        if (check is not AutomationConfigurationCheck.Valid valid)
        {
            await checkpoints.FailAsync(producer, "handler-unavailable", cancellationToken);
            return null;
        }

        var nextResolving = resolving.Add(producer.Id);
        var inputs = await ResolveInputsAsync(
            hostId,
            context,
            flow,
            producer,
            checkpoints,
            nextResolving,
            executionIntegerEntropy,
            cancellationToken
        );
        if (inputs is not AutomationInputResolution.Available resolvedInputs)
        {
            await checkpoints.FailAsync(producer, "input-resolution-failed", cancellationToken);
            return null;
        }

        if (checkpoint is AutomationPureCheckpoint.Available available)
        {
            if (ValidCheckpoint(descriptor, available.Outputs, resolvedInputs.PortValues))
            {
                return available.Outputs;
            }

            await checkpoints.FailAsync(producer, "output-invalid", cancellationToken);
            return null;
        }

        AutomationPureNodeResult result;
        try
        {
            result =
                valid.Configuration is PluginAutomationConfiguration pluginConfiguration
                && pluginExecution is not null
                    ? await pluginExecution.ExecutePureAsync(
                        hostId,
                        new(producer.DefinitionId),
                        pluginConfiguration,
                        resolvedInputs.PortValues,
                        cancellationToken
                    )
                : handlers.TryResolve(new(producer.DefinitionId), out var handler)
                    ? await handler.ExecuteAsync(
                        new(
                            valid.Configuration,
                            resolvedInputs.PortValues,
                            executionIntegerEntropy
                        ),
                        cancellationToken
                    )
                : new AutomationPureNodeResult.Failed("handler-unavailable");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await checkpoints.FailAsync(producer, "handler-failed", cancellationToken);
            return null;
        }

        if (result is AutomationPureNodeResult.Failed failed)
        {
            await checkpoints.FailAsync(
                producer,
                StableCode(failed.Code) ? failed.Code : "handler-failed",
                cancellationToken
            );
            return null;
        }

        if (
            result is not AutomationPureNodeResult.Succeeded succeeded
            || !AutomationPureHandlerRegistry.TryValidateResult(
                descriptor,
                succeeded,
                resolvedInputs.PortValues,
                out var outputs
            )
        )
        {
            await checkpoints.FailAsync(producer, "output-invalid", cancellationToken);
            return null;
        }

        return await checkpoints.CompleteAsync(producer, outputs, cancellationToken)
            ? outputs
            : null;
    }

    private static AutomationResolvedValue? ResolveFixed(
        AutomationConfiguration configuration,
        AutomationPortMetadata input
    ) =>
        (configuration, input.Id.Value) switch
        {
            (SendChatActionConfiguration sendChat, "message") => new(
                new AutomationValue.Text(sendChat.Message),
                [AutomationValueProvenance.Generated]
            ),
            (ConditionControlConfiguration condition, "predicate") => new(
                new AutomationValue.Boolean(condition.Predicate),
                [AutomationValueProvenance.Generated]
            ),
            (PluginAutomationConfiguration plugin, _)
                when input.BindingFieldId is { } fieldId
                    && plugin.Values.TryGetValue(fieldId, out var value) => new(
                value,
                [AutomationValueProvenance.Generated]
            ),
            _ => null,
        };

    private static AutomationResolvedValue? ResolveSource(
        AutomationPortMetadata port,
        AutomationContext context
    ) =>
        port.Sensitivity != AutomationDataSensitivity.Safe
            ? null
            : port.Id.Value switch
            {
                "actor" when context.Actor is { } actor => new(
                    new AutomationValue.Actor(new(actor.Login, actor.DisplayName)),
                    [
                        AutomationValueProvenance.PublicDisplayName,
                        AutomationValueProvenance.PublicLogin,
                    ]
                ),
                "actor" when port.Nullability == AutomationPortNullability.Nullable => new(
                    new AutomationValue.Null(AutomationPortValueType.Actor),
                    [
                        AutomationValueProvenance.PublicDisplayName,
                        AutomationValueProvenance.PublicLogin,
                    ]
                ),
                "channel" => new(
                    new AutomationValue.Channel(
                        new(context.Channel.Login, context.Channel.DisplayName)
                    ),
                    [
                        AutomationValueProvenance.PublicDisplayName,
                        AutomationValueProvenance.PublicLogin,
                    ]
                ),
                "arguments" => new(
                    new AutomationValue.Arguments([
                        .. context.Arguments.Select(static argument => new AutomationValueArgument(
                            argument.Position,
                            argument.Value,
                            [AutomationValueProvenance.PublicChat]
                        )),
                    ]),
                    [AutomationValueProvenance.PublicChat]
                ),
                _ => ResolvePluginSource(port, context),
            };

    private static AutomationResolvedValue? ResolvePluginSource(
        AutomationPortMetadata port,
        AutomationContext context
    )
    {
        var variable =
            context.Variables.ForExecution().TryGetValue(new(port.Id.Value), out var found)
            && found.Sensitivity == AutomationDataSensitivity.Safe
                ? found
                : null;
        return variable is null
            ? port.Nullability == AutomationPortNullability.Nullable
                ? new(
                    new AutomationValue.Null(port.ValueType),
                    [AutomationValueProvenance.Generated]
                )
                : null
            : AutomationPureHandlerRegistry.ValueType(variable.Value) == port.ValueType
                ? new(variable.Value, [AutomationValueProvenance.Generated])
                : null;
    }

    private AutomationResolvedValue? ResolveExpression(
        AutomationExpressionSource expression,
        AutomationPortMetadata port,
        AutomationContext context
    )
    {
        var evaluation =
            port.ValueType == AutomationPortValueType.Text
            && expression.Source.Contains("${", StringComparison.Ordinal)
                ? expressions.Interpolate(expression.Source, context)
                : expressions.Evaluate(expression, context);
        if (
            port.Sensitivity != AutomationDataSensitivity.Safe
            || evaluation
                is not AutomationExpressionResult.Value { UsesSensitiveValues: false } evaluated
        )
        {
            return null;
        }

        var provenance = ExpressionProvenance(expression.Source);
        if (evaluated.Result is null)
        {
            return port.Nullability == AutomationPortNullability.Nullable
                ? new(new AutomationValue.Null(port.ValueType), provenance)
                : null;
        }

        AutomationValue? value = port.ValueType switch
        {
            AutomationPortValueType.Text when evaluated.Result is string text =>
                new AutomationValue.Text(text),
            AutomationPortValueType.Number => Number(evaluated.Result),
            AutomationPortValueType.Boolean when evaluated.Result is bool boolean =>
                new AutomationValue.Boolean(boolean),
            AutomationPortValueType.Timestamp when evaluated.Result is DateTimeOffset timestamp =>
                new AutomationValue.Timestamp(timestamp),
            AutomationPortValueType.Arguments
                when evaluated.Result is IEnumerable<string> arguments =>
                new AutomationValue.Arguments([
                    .. arguments.Select(
                        (argument, position) =>
                            new AutomationValueArgument(position, argument, provenance)
                    ),
                ]),
            _ => null,
        };
        return value is null ? null : new(value, provenance);
    }

    private static ImmutableArray<AutomationValueProvenance> ExpressionProvenance(string source)
    {
        var provenance = ImmutableHashSet.CreateBuilder<AutomationValueProvenance>();
        if (
            ContainsIdentifier(source, "actor.display_name")
            || ContainsIdentifier(source, "channel.display_name")
        )
        {
            _ = provenance.Add(AutomationValueProvenance.PublicDisplayName);
        }

        if (
            ContainsIdentifier(source, "actor.login") || ContainsIdentifier(source, "channel.login")
        )
        {
            _ = provenance.Add(AutomationValueProvenance.PublicLogin);
        }

        if (ContainsIdentifier(source, "arguments"))
        {
            _ = provenance.Add(AutomationValueProvenance.PublicChat);
        }

        if (provenance.Count == 0)
        {
            _ = provenance.Add(AutomationValueProvenance.Generated);
        }

        return [.. provenance.Order()];
    }

    private static AutomationValue? Number(object value) =>
        value switch
        {
            decimal number => new AutomationValue.Number(number),
            long number => new AutomationValue.Number(number),
            int number => new AutomationValue.Number(number),
            uint number => new AutomationValue.Number(number),
            ulong number => new AutomationValue.Number(number),
            _ => null,
        };

    private static bool Matches(AutomationPortMetadata port, AutomationValue value) =>
        value switch
        {
            AutomationValue.Null nullValue => port.Nullability == AutomationPortNullability.Nullable
                && nullValue.ValueType == port.ValueType,
            _ => AutomationPureHandlerRegistry.ValueType(value) == port.ValueType,
        };

    private static bool ValidCheckpoint(
        AutomationDefinitionDescriptor descriptor,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> outputs,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> inputs
    ) =>
        AutomationPureHandlerRegistry.ValidCheckpointShape(descriptor, outputs)
        && (
            descriptor.Kind != AutomationNodeKind.Transform
            || outputs.Values.All(output =>
                output.Provenance.SequenceEqual(
                    inputs
                        .Values.SelectMany(static input => input.Provenance)
                        .Append(AutomationValueProvenance.Generated)
                        .Distinct()
                        .Order()
                )
                && output.SafeTriggerFields.SequenceEqual(
                    inputs
                        .Values.SelectMany(static input =>
                            input.SafeTriggerFields.IsDefault ? [] : input.SafeTriggerFields
                        )
                        .Distinct()
                        .OrderBy(static field => field.Value, StringComparer.Ordinal)
                )
            )
        );

    private static bool StableCode(string code) =>
        !string.IsNullOrWhiteSpace(code)
        && code.Length <= 64
        && code[0] is >= 'a' and <= 'z'
        && code.All(static character =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'
        );

    private static bool ContainsIdentifier(string source, string identifier)
    {
        var offset = 0;
        while ((offset = source.IndexOf(identifier, offset, StringComparison.Ordinal)) >= 0)
        {
            var before = offset == 0 || !IsIdentifierCharacter(source[offset - 1]);
            var after =
                offset + identifier.Length == source.Length
                || !IsIdentifierCharacter(source[offset + identifier.Length]);
            if (before && after)
            {
                return true;
            }

            offset += identifier.Length;
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value == '_';
}

internal sealed class AutomationSampleCheckpointStore : IAutomationPureCheckpointStore
{
    private readonly Dictionary<
        Guid,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue>
    > _outputs = [];
    private readonly HashSet<Guid> _failed = [];
    private readonly HashSet<Guid> _running = [];

    public ValueTask<AutomationPureCheckpoint> ReadOrBeginAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        CancellationToken cancellationToken
    ) =>
        _outputs.TryGetValue(node.Id, out var outputs)
            ? ValueTask.FromResult<AutomationPureCheckpoint>(
                new AutomationPureCheckpoint.Available(outputs)
            )
        : _failed.Contains(node.Id) || !_running.Add(node.Id)
            ? ValueTask.FromResult<AutomationPureCheckpoint>(new AutomationPureCheckpoint.Failed())
        : ValueTask.FromResult<AutomationPureCheckpoint>(new AutomationPureCheckpoint.Begin());

    public ValueTask<bool> CompleteAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> outputs,
        CancellationToken cancellationToken
    )
    {
        _ = _running.Remove(node.Id);
        _outputs.Add(node.Id, outputs);
        return ValueTask.FromResult(true);
    }

    public ValueTask FailAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        string code,
        CancellationToken cancellationToken
    )
    {
        _ = _running.Remove(node.Id);
        _ = _failed.Add(node.Id);
        return ValueTask.CompletedTask;
    }
}

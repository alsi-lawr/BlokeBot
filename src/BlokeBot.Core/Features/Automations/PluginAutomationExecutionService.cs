using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

internal sealed class PluginAutomationExecutionService(
    AutomationDefinitionCatalog catalog,
    IPluginAutomationInvoker invoker
)
{
    internal async ValueTask<AutomationActionOutcome> ExecuteActionAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        PluginAutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        var result = await InvokeAsync(
            hostId,
            definitionId,
            configuration,
            inputs,
            cancellationToken
        );
        return result is PluginDispatchInvocationOutcome.Returned
            ? new AutomationActionOutcome.Succeeded()
            : new AutomationActionOutcome.Failed(Code(result));
    }

    internal async ValueTask<AutomationPureNodeResult> ExecutePureAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        PluginAutomationConfiguration configuration,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken
    )
    {
        var fieldInputs = inputs.ToImmutableDictionary(
            pair => new AutomationConfigurationFieldId(pair.Key.Value),
            static pair => pair.Value
        );
        var result = await InvokeAsync(
            hostId,
            definitionId,
            configuration,
            fieldInputs,
            cancellationToken
        );
        if (
            result
                is not PluginDispatchInvocationOutcome.Returned { Value: PluginValue.Map returned }
            || !TryResolve(hostId, definitionId, out var definition)
        )
        {
            return new AutomationPureNodeResult.Failed(Code(result));
        }

        var properties = returned.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );
        var dataOutputs = definition
            .Descriptor.Outputs.Where(static output =>
                output.ValueType != AutomationPortValueType.Flow
            )
            .ToArray();
        if (properties.Keys.Any(name => dataOutputs.All(output => output.Id.Value != name)))
        {
            return new AutomationPureNodeResult.Failed("plugin-output-invalid");
        }

        var provenance = inputs
            .Values.SelectMany(static input => input.Provenance)
            .Append(AutomationValueProvenance.Generated)
            .Distinct()
            .Order()
            .ToImmutableArray();
        var outputs = ImmutableDictionary.CreateBuilder<
            AutomationPortId,
            AutomationResolvedValue
        >();
        foreach (var output in dataOutputs)
        {
            if (!properties.TryGetValue(output.Id.Value, out var value))
            {
                if (output.Nullability == AutomationPortNullability.Nullable)
                {
                    outputs.Add(
                        output.Id,
                        new(
                            new AutomationValue.Null(output.ValueType),
                            provenance,
                            ValueFreeDiagnostic: true
                        )
                    );
                    continue;
                }
                return new AutomationPureNodeResult.Failed("plugin-output-invalid");
            }
            if (value is PluginValue.Nil)
            {
                if (output.Nullability == AutomationPortNullability.NonNullable)
                {
                    return new AutomationPureNodeResult.Failed("plugin-output-invalid");
                }
                outputs.Add(
                    output.Id,
                    new(
                        new AutomationValue.Null(output.ValueType),
                        provenance,
                        ValueFreeDiagnostic: true
                    )
                );
                continue;
            }
            var converted = AutomationStructuredValue.TryConvert(value, out var structured)
                ? structured
                : null;
            if (
                converted is null
                || AutomationPureHandlerRegistry.ValueType(converted) != output.ValueType
            )
            {
                return new AutomationPureNodeResult.Failed("plugin-output-invalid");
            }
            outputs.Add(output.Id, new(converted, provenance, ValueFreeDiagnostic: true));
        }
        return new AutomationPureNodeResult.Succeeded(outputs.ToImmutable());
    }

    private async ValueTask<PluginDispatchInvocationOutcome> InvokeAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        PluginAutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken
    )
    {
        if (!TryResolve(hostId, definitionId, out var definition))
        {
            return new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            );
        }
        var endpoint = definition.Endpoint;
        if (!PluginHostId.TryCreate(hostId.Value, out var pluginHostId))
        {
            return new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.InvalidContext
            );
        }
        _ = PluginAutomationInvocationId.TryCreate(Guid.NewGuid(), out var invocationId);
        var context = new PluginInvocationContext.Automation(
            endpoint.Declaration.Installation,
            pluginHostId,
            endpoint.State.Key.FeatureId,
            endpoint.Descriptor.Id,
            invocationId
        );
        var input = new PluginValue.Map([
            new(
                "configuration",
                new PluginValue.Map(
                    configuration
                        .Values.Select(pair => new PluginValueProperty(
                            pair.Key.Value,
                            AutomationStructuredValue.ToPluginValue(pair.Value)
                        ))
                        .ToImmutableArray()
                )
            ),
            new(
                "inputs",
                new PluginValue.Map(
                    inputs
                        .Select(pair => new PluginValueProperty(
                            pair.Key.Value,
                            AutomationStructuredValue.ToPluginValue(pair.Value.Value)
                        ))
                        .ToImmutableArray()
                )
            ),
        ]);
        return await invoker.InvokeAutomationAsync(endpoint, context, input, cancellationToken);
    }

    private static string Code(PluginDispatchInvocationOutcome result) =>
        result switch
        {
            PluginDispatchInvocationOutcome.Rejected => "plugin-feature-unavailable",
            PluginDispatchInvocationOutcome.Stale => "plugin-generation-stale",
            PluginDispatchInvocationOutcome.Cancelled => "plugin-cancelled",
            PluginDispatchInvocationOutcome.Failed => "plugin-execution-failed",
            _ => "plugin-output-invalid",
        };

    private bool TryResolve(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        out IPluginAutomationDefinition definition
    )
    {
        if (
            catalog.TryResolve(hostId, definitionId, out var resolved)
            && resolved is IPluginAutomationDefinition plugin
        )
        {
            definition = plugin;
            return true;
        }
        definition = null!;
        return false;
    }
}

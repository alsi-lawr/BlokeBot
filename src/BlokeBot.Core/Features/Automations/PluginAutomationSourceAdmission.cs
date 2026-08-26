using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

internal sealed class PluginAutomationSourceAdmission(
    AutomationCatalogService catalog,
    AutomationRuntimeService runtime,
    IPluginHostContextResolver hosts,
    TimeProvider clock
) : IPluginAutomationSourceAdmission
{
    public async ValueTask AdmitAsync(
        PluginDispatchEndpoint endpoint,
        PluginInvocationContext.Channel context,
        ImmutableArray<PluginAutomationSourceEmission> emissions,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.FindAsync(endpoint.State.Key.HostId, cancellationToken);
        if (host is null || context.Host != host.Id)
        {
            return;
        }

        foreach (var emission in emissions)
        {
            var definitionId = PluginAutomationCatalogRegistry.DefinitionId(
                endpoint.State.Key.PluginId,
                emission.DefinitionId
            );
            if (
                !catalog.TryResolvePlugin(
                    new(endpoint.State.Key.HostId.Value),
                    definitionId,
                    out var definition
                )
                || definition.Endpoint.State.Key != endpoint.State.Key
                || definition.Endpoint.State.Fence != endpoint.State.Fence
                || definition.Endpoint.State.Generation != endpoint.State.Generation
                || definition.Descriptor.Kind != AutomationNodeKind.Source
                || !TryVariables(
                    definition.Endpoint.Descriptor,
                    emission.Outputs,
                    out var variables
                )
            )
            {
                continue;
            }

            var occurredAt = OccurredAt(context) ?? clock.GetUtcNow();
            var automationContext = new AutomationContext(
                new(OccurrenceId(context, definitionId), definitionId),
                context.Actor is { } actor
                    ? new(actor.TwitchUserId ?? string.Empty, actor.Login, actor.DisplayName)
                    : null,
                new(new(host.Id.Value), host.Login, host.Login, host.Login),
                context.Stream is { StreamId: { } streamId } stream
                    ? new(streamId, null, null, null)
                    : null,
                new(occurredAt, clock.GetUtcNow()),
                context.Command is { } command
                    ?
                    [
                        .. command.Arguments.Select(
                            (argument, position) => new AutomationArgument(position, argument)
                        ),
                    ]
                    : [],
                new AutomationVariableSet(variables)
            );
            _ = await runtime.DispatchAsync(
                new(
                    automationContext,
                    new PluginAutomationConfiguration(
                        ImmutableDictionary<AutomationConfigurationFieldId, AutomationValue>.Empty
                    )
                ),
                cancellationToken
            );
        }
    }

    private static bool TryVariables(
        PluginAutomationDefinitionDescriptor definition,
        PluginValue.Map outputs,
        out ImmutableArray<KeyValuePair<AutomationVariableName, AutomationVariable>> variables
    )
    {
        variables = [];
        if (PluginValueValidator.Validate(outputs) is PluginValueValidationOutcome.Invalid)
        {
            return false;
        }
        var values = outputs.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );
        if (
            values.Keys.Any(name => definition.Outputs.All(output => output.Id.Value != name))
            || definition.Outputs.Any(output =>
                output.Required && !values.ContainsKey(output.Id.Value)
            )
        )
        {
            return false;
        }

        var result = ImmutableArray.CreateBuilder<
            KeyValuePair<AutomationVariableName, AutomationVariable>
        >();
        foreach (var output in definition.Outputs)
        {
            if (!values.TryGetValue(output.Id.Value, out var pluginValue))
            {
                continue;
            }
            if (pluginValue is PluginValue.Nil && !output.Required)
            {
                continue;
            }
            if (
                !AutomationStructuredValue.TryConvert(pluginValue, out var value)
                || AutomationPureHandlerRegistry.ValueType(value)
                    != PluginAutomationDefinition.ValueType(output.ValueKind)
            )
            {
                return false;
            }
            result.Add(new(new(output.Id.Value), new(value, AutomationDataSensitivity.Safe)));
        }
        variables = result.ToImmutable();
        return true;
    }

    private static DateTimeOffset? OccurredAt(PluginInvocationContext.Channel context) =>
        context.Event?.OccurredAtUtc ?? context.Schedule?.DueAtUtc;

    private static Guid OccurrenceId(
        PluginInvocationContext.Channel context,
        AutomationDefinitionId definitionId
    )
    {
        var identity = context switch
        {
            { Event: { } @event } => $"event:{@event.EventId}",
            { Schedule: { } schedule } => $"schedule:{schedule.ScheduleId:D}:{schedule.DueAtUtc:O}",
            _ => $"command:{Guid.NewGuid():D}",
        };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{definitionId.Value}\n{identity}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}

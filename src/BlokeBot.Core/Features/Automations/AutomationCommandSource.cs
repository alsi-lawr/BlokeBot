using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationCommandSource
{
    internal static async Task<ImmutableDictionary<int, CustomCommandArgumentSchema>> LoadAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var commands = await db
            .CustomCommands.AsNoTracking()
            .Where(command =>
                command.HostId == hostId.Value
                && command.ArgumentSchema != null
                && command.Action is AutomationCustomCommandAction
            )
            .Select(command => new { command.Id, command.ArgumentSchema })
            .ToArrayAsync(cancellationToken);
        return commands.ToImmutableDictionary(
            command => command.Id,
            command => command.ArgumentSchema!
        );
    }

    internal static AutomationDefinitionDescriptor Project(
        AutomationDefinitionDescriptor descriptor,
        AutomationConfiguration configuration,
        IReadOnlyDictionary<int, CustomCommandArgumentSchema> schemas
    ) =>
        configuration is CustomCommandSourceConfiguration command
        && schemas.TryGetValue(command.CommandId.Value, out var schema)
            ? Project(descriptor, schema)
            : descriptor;

    internal static AutomationDefinitionDescriptor Project(
        AutomationDefinitionDescriptor descriptor,
        CustomCommandArgumentSchema schema
    ) =>
        descriptor with
        {
            Outputs =
            [
                .. descriptor.Outputs,
                .. schema.Fields.Select(field => new AutomationPortMetadata(
                    new(CustomCommandArguments.PortId(field.Name)),
                    field.Name,
                    "Parsed command argument. Connect this output to an input; it is not an expression variable.",
                    field.Kind == CustomCommandArgumentKind.Number
                        ? AutomationPortValueType.Number
                        : AutomationPortValueType.Text
                )),
            ],
        };

    internal static AutomationDefinitionDescriptor ApplyContract(
        AutomationDefinitionDescriptor descriptor,
        AutomationSubflowNodeContract contract
    ) =>
        descriptor with
        {
            Kind = contract.Kind,
            Display = contract.Display,
            Inputs = contract.Inputs,
            Outputs = contract.Outputs,
            Capabilities = contract.Capabilities,
            RetrySafety = contract.RetrySafety,
        };

    internal static AutomationSubflowNodeContract Contract(
        AutomationNodeId id,
        AutomationDefinitionDescriptor descriptor
    ) =>
        new(
            id,
            descriptor.Kind,
            descriptor.Display,
            descriptor.Inputs,
            descriptor.Outputs,
            descriptor.Capabilities,
            descriptor.RetrySafety
        );

    internal static async Task<bool> PreparedIsCurrentAsync(
        BlokeBotDbContext db,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.CommandArguments is not { } prepared)
        {
            return true;
        }
        var current = await db
            .CustomCommands.AsNoTracking()
            .Where(command =>
                command.HostId == context.HostId.Value
                && command.Id == prepared.CommandId
                && command.Action is AutomationCustomCommandAction
            )
            .Select(command => new { command.ArgumentSchema })
            .SingleOrDefaultAsync(cancellationToken);
        return current is not null
            && (
                current.ArgumentSchema is null
                    ? prepared.Schema is null
                    : prepared.Schema is not null
                        && CustomCommandArgumentSchema.Serialize(current.ArgumentSchema)
                            == CustomCommandArgumentSchema.Serialize(prepared.Schema)
            );
    }
}

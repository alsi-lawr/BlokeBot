using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    private async Task<(
        AutomationScenarioFixture Fixture,
        AutomationGraphError? Error
    )> PrepareCommandArgumentsAsync(
        BlokeBotDbContext db,
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture,
        CancellationToken cancellationToken,
        string? commandRoute = null
    )
    {
        var schemas = await AutomationCommandSource.LoadAsync(db, draft.HostId, cancellationToken);
        var source = draft.Nodes.FirstOrDefault(node => node.Id == fixture.SourceNodeId);
        var command =
            source is not null
            && catalog.ValidatePersistedDefinition(source.Definition)
                is AutomationConfigurationCheck.Valid
                {
                    Configuration: CustomCommandSourceConfiguration configured
                }
                ? configured
                : null;
        var alias = "command";
        if (commandRoute is not null)
        {
            if (command is null)
            {
                return (
                    fixture,
                    new(
                        fixture.SourceNodeId,
                        "scenario-command-invalid",
                        "Enter a full chat command beginning with !."
                    )
                );
            }
            alias = CommandAliasNormalizer.Normalize(commandRoute);
            var matches = await db
                .CustomCommandAliases.AsNoTracking()
                .AnyAsync(
                    candidate =>
                        candidate.HostId == draft.HostId.Value
                        && candidate.CustomCommandId == command.CommandId.Value
                        && candidate.Alias == alias,
                    cancellationToken
                );
            if (!matches)
            {
                return (
                    fixture,
                    new(
                        fixture.SourceNodeId,
                        "scenario-command-mismatch",
                        "This message does not invoke the selected command. Choose its current alias."
                    )
                );
            }
        }
        if (command is null || !schemas.TryGetValue(command.CommandId.Value, out var schema))
        {
            return (
                fixture with
                {
                    Context = fixture.Context with { CommandArguments = null },
                },
                null
            );
        }
        var result = CustomCommandArguments.Parse(
            command.CommandId.Value,
            alias,
            schema,
            fixture.CommandArgumentTail ?? string.Empty
        );
        return result.Match(
            prepared =>
                (
                    fixture with
                    {
                        Context = fixture.Context with
                        {
                            CommandArguments = prepared.Arguments,
                            Arguments = commandRoute is not null
                                ? fixture.Context.Arguments
                                :
                                [
                                    .. (fixture.CommandArgumentTail ?? string.Empty)
                                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(
                                            (text, index) => new AutomationArgument(index, text)
                                        ),
                                ],
                        },
                    },
                    (AutomationGraphError?)null
                ),
            rejected =>
                (
                    fixture,
                    (AutomationGraphError?)
                        new(
                            fixture.SourceNodeId,
                            "scenario-command-arguments-invalid",
                            rejected.Message
                        )
                )
        );
    }

    internal ImmutableArray<AutomationPortMetadata> SourceFields(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        IReadOnlyDictionary<int, CustomCommandArgumentSchema> schemas
    ) =>
        draft.Nodes.FirstOrDefault(node => node.Id == sourceNodeId) is { } source
        && catalog.ValidatePersistedDefinition(source.Definition)
            is AutomationConfigurationCheck.Valid valid
            ?
            [
                .. AutomationCommandSource
                    .Project(valid.Definition, valid.Configuration, schemas)
                    .Outputs.Where(port => port.ValueType != AutomationPortValueType.Flow),
            ]
            : [];
}

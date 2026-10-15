using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

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
        CancellationToken cancellationToken
    )
    {
        var schemas = await AutomationCommandSource.LoadAsync(db, draft.HostId, cancellationToken);
        var source = draft.Nodes.FirstOrDefault(node => node.Id == fixture.SourceNodeId);
        if (
            source is null
            || catalog.ValidatePersistedDefinition(source.Definition)
                is not AutomationConfigurationCheck.Valid
                {
                    Configuration: CustomCommandSourceConfiguration command
                }
            || !schemas.TryGetValue(command.CommandId.Value, out var schema)
        )
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
            "command",
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
                            Arguments =
                            [
                                .. (fixture.CommandArgumentTail ?? string.Empty)
                                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                    .Select((text, index) => new AutomationArgument(index, text)),
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

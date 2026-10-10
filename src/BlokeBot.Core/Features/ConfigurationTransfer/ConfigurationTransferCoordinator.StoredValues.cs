using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationTransferCoordinator
{
    private async Task<
        IReadOnlyList<ConfigurationPostCommitFailure>
    > PublishImportedDefinitionsAsync(int hostId, IReadOnlyList<CustomValueDefinition> definitions)
    {
        try
        {
            await _storedValueChanges.PublishCommittedAsync(
                definitions.Select(definition => new CustomStoredValueChange.Definition(
                    hostId,
                    definition.Id
                ))
            );
            return [];
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Stored-value refresh failed after committed import on host {HostId}.",
                hostId
            );
            return [new(ConfigurationSectionId.CustomCommands, "reconciliation-failed")];
        }
    }
}

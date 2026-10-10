using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class CountdownConfigurationImportObserver(IAutomationUiChangePublisher changes)
    : IConfigurationImportObserver
{
    public ConfigurationSectionId Section => ConfigurationSectionId.ChannelToolEnablement;

    public async ValueTask<ConfigurationImportObservation> ImportedAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await changes.CountdownsChangedAsync(new(hostId));
        return ConfigurationImportObservation.Complete;
    }
}

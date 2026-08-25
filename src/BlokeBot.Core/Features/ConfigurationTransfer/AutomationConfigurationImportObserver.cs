namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class AutomationConfigurationImportObserver(
    IEventSubChannelReconciliationTrigger eventSub
) : IConfigurationImportObserver
{
    public ConfigurationSectionId Section => ConfigurationSectionId.Automations;

    public async ValueTask<ConfigurationImportObservation> ImportedAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        _ = hostId;
        await eventSub.ReconcileAsync(cancellationToken);
        return ConfigurationImportObservation.Complete;
    }
}

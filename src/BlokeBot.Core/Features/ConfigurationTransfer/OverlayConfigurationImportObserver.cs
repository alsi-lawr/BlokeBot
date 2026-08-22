using BlokeBot.Core.Features.Overlays;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class OverlayConfigurationImportObserver(
    EventBus<AppEventKind> events,
    OverlayMediaMaintenanceService mediaMaintenance
) : IConfigurationImportObserver
{
    public ConfigurationSectionId Section => ConfigurationSectionId.Overlays;

    public async ValueTask ImportedAsync(int hostId, CancellationToken cancellationToken)
    {
        _ = hostId;
        mediaMaintenance.Schedule();
        _ = await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
    }
}

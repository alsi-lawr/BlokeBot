using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class OverlayConfigurationImportObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DurableAlertService alerts,
    EventBus<AppEventKind> events,
    OverlayMediaMaintenanceService mediaMaintenance
) : IConfigurationImportObserver
{
    public ConfigurationSectionId Section => ConfigurationSectionId.Overlays;

    public async ValueTask<ConfigurationImportObservation> ImportedAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sourceNames = await db
            .OverlayInstances.AsNoTracking()
            .Where(value => value.HostId == hostId && value.RequiresAccessKeyRegeneration)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .Select(value => value.Name)
            .ToArrayAsync(cancellationToken);
        if (sourceNames.Length == 0)
        {
            _ = await alerts
                .Resolve(
                    hostId,
                    OverlayAccessRegeneration.AlertSource,
                    OverlayAccessRegeneration.AlertSourceKey,
                    "BlokeBot"
                )
                .ExecuteAsync(cancellationToken);
        }
        else
        {
            _ = await alerts
                .Create(
                    hostId,
                    DurableAlertSeverity.Warning,
                    OverlayAccessRegeneration.AlertSource,
                    OverlayAccessRegeneration.AlertSourceKey,
                    OverlayAccessRegeneration.Title,
                    OverlayAccessRegeneration.Message(sourceNames),
                    OverlayAccessRegeneration.LinkPath
                )
                .ExecuteAsync(cancellationToken);
        }

        mediaMaintenance.Schedule();
        _ = await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
        return sourceNames.Length == 0
            ? ConfigurationImportObservation.Complete
            : new([
                new(
                    OverlayAccessRegeneration.FollowUpCode,
                    OverlayAccessRegeneration.Title,
                    OverlayAccessRegeneration.Message(sourceNames),
                    OverlayAccessRegeneration.LinkPath
                ),
            ]);
    }
}

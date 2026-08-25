using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationTransferCoordinator
{
    private async Task<ConfigurationActivation?> StageEnablementAsync(
        BlokeBotDbContext db,
        BotHost host,
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (
            Selected(selection, ConfigurationSectionId.ChannelToolEnablement) is null
            || document.Sections.ChannelToolEnablement is not { } imported
            || selection.EnablementChanges.Count == 0
        )
        {
            return null;
        }
        var importedFlags = ChannelToolEnablementMapper.ToFlags(imported);
        var previous = host.EnabledFeatures;
        var updated = previous;
        foreach (var feature in selection.EnablementChanges)
        {
            updated = importedFlags.Contains(feature) ? updated | feature : updated & ~feature;
        }
        if (updated == previous)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await HostFeatureTransitionStager.StageAsync(db, host, updated, now, cancellationToken);
        var enabled = updated & ~previous;
        var disabled = previous & ~updated;
        var pending = await db.ConfigurationActivations.SingleOrDefaultAsync(
            x => x.HostId == host.Id && (x.Status == ConfigurationActivationStatus.Pending),
            cancellationToken
        );
        if (pending is null)
        {
            pending = new ConfigurationActivation
            {
                Id = Guid.NewGuid(),
                HostId = host.Id,
                Status = ConfigurationActivationStatus.Pending,
                CreatedAtUtc = now,
            };
            _ = db.ConfigurationActivations.Add(pending);
        }
        var queuedEnabled = pending.EnabledChanges;
        var queuedDisabled = pending.DisabledChanges;
        pending.EnabledChanges = (queuedEnabled & ~disabled) | (enabled & ~queuedDisabled);
        pending.DisabledChanges = (queuedDisabled & ~enabled) | (disabled & ~queuedEnabled);
        pending.Status = ConfigurationActivationStatus.Pending;
        pending.Revision++;
        pending.UpdatedAtUtc = now;
        pending.IssuesJson = null;
        pending.CompletedAtUtc = null;
        return pending;
    }
}

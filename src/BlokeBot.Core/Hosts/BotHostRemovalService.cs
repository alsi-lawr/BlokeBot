using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Hosts;

internal sealed class BotHostRemovalService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes,
    IOptions<BlokeBotOptions> options,
    OverlayMediaMaintenanceService mediaMaintenance,
    TimeProvider timeProvider,
    ILogger<BotHostRemovalService> logger
)
{
    public async Task<HostRemovalResult> RemoveAsync(int hostId, CancellationToken ct)
    {
        await mediaMaintenance.Gate.WaitAsync(ct);
        bool wasRemoved;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var documentIds = await db
                .OverlayMediaAssets.Where(reference => reference.HostId == hostId)
                .Select(reference => reference.DocumentId)
                .Distinct()
                .ToArrayAsync(ct);

            var removed = await db.Hosts.Where(host => host.Id == hostId).ExecuteDeleteAsync(ct);
            if (removed > 0 && documentIds.Length > 0)
            {
                var orphans = await db
                    .OverlayMediaDocuments.Where(document =>
                        documentIds.Contains(document.Id)
                        && !db.OverlayMediaAssets.Any(reference =>
                            reference.DocumentId == document.Id
                        )
                    )
                    .ToArrayAsync(ct);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                foreach (var document in orphans)
                {
                    document.State = Persistence.Models.OverlayMediaDocumentState.Orphaned;
                    document.OrphanedAtUtc = now;
                    document.UpdatedAtUtc = now;
                }
                _ = await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            wasRemoved = removed > 0;
        }
        finally
        {
            _ = mediaMaintenance.Gate.Release();
        }

        var media = RemoveMedia(hostId);
        mediaMaintenance.Schedule();
        if (!wasRemoved)
        {
            return new HostRemovalResult(Removed: false, media);
        }
        _ = await changes.NotifyChangedAsync(ct);
        return new HostRemovalResult(Removed: true, media);
    }

    private HostMediaCleanup RemoveMedia(int hostId)
    {
        var directory = OverlayMediaDirectory.HostDirectory(options.Value.DatabasePath, hostId);
        if (!Directory.Exists(directory))
        {
            return new HostMediaCleanup.NotPresent();
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return new HostMediaCleanup.Removed();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Overlay media for removed host {HostId} could not be fully deleted "
                    + "({ErrorType}); remove {Directory} manually",
                hostId,
                exception.GetType().Name,
                directory
            );
            return new HostMediaCleanup.Failed(directory);
        }
    }
}

public sealed record HostRemovalResult(bool Removed, HostMediaCleanup Media);

public abstract record HostMediaCleanup
{
    private HostMediaCleanup() { }

    public sealed record Removed : HostMediaCleanup;

    public sealed record NotPresent : HostMediaCleanup;

    public sealed record Failed(string Directory) : HostMediaCleanup;
}

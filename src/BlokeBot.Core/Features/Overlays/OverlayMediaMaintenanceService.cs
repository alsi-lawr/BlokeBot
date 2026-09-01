using System.Threading.Channels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class OverlayMediaMaintenanceService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IOptions<BlokeBotOptions> options,
    IOverlayMediaFileDeletion fileDeletion,
    TimeProvider timeProvider,
    ILogger<OverlayMediaMaintenanceService> logger
) : BackgroundService
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        }
    );

    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal void Schedule() => _ = _wake.Writer.TryWrite(true);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RecoverAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var _ in _wake.Reader.ReadAllAsync(stoppingToken))
        {
            await RecoverAsync(stoppingToken);
        }
    }

    internal async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var root = DocumentDirectory();
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var publishing = await db
                .OverlayMediaDocuments.Where(document =>
                    document.State == OverlayMediaDocumentState.Publishing
                )
                .ToArrayAsync(cancellationToken);
            foreach (var document in publishing)
            {
                RecoverPublication(document, root);
            }

            var referencedIds = await db
                .OverlayMediaAssets.Select(reference => reference.DocumentId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var referenced = referencedIds.ToHashSet();
            var newlyOrphaned = await db
                .OverlayMediaDocuments.Where(document =>
                    !referenced.Contains(document.Id)
                    && document.State != OverlayMediaDocumentState.Orphaned
                )
                .ToArrayAsync(cancellationToken);
            var now = Now();
            foreach (var document in newlyOrphaned)
            {
                document.State = OverlayMediaDocumentState.Orphaned;
                document.OrphanedAtUtc = now;
                document.UpdatedAtUtc = now;
            }
            _ = await db.SaveChangesAsync(cancellationToken);

            var orphans = await db
                .OverlayMediaDocuments.Where(document =>
                    document.State == OverlayMediaDocumentState.Orphaned
                    && !db.OverlayMediaAssets.Any(reference => reference.DocumentId == document.Id)
                )
                .ToArrayAsync(cancellationToken);
            foreach (var document in orphans)
            {
                var path = Path.Combine(root, document.StorageKey);
                if (fileDeletion.Delete(path) is OverlayMediaFileDeletionOutcome.Unavailable)
                {
                    continue;
                }
                _ = db.OverlayMediaDocuments.Remove(document);
            }
            _ = await db.SaveChangesAsync(cancellationToken);

            var knownStorageKeys = await db
                .OverlayMediaDocuments.Select(document => document.StorageKey)
                .ToHashSetAsync(cancellationToken);
            foreach (
                var path in Directory
                    .EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                    .Where(path =>
                        !Path.GetFileName(path).StartsWith(".upload-", StringComparison.Ordinal)
                        && !knownStorageKeys.Contains(Path.GetFileName(path))
                    )
            )
            {
                _ = fileDeletion.Delete(path);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogWarning(
                exception,
                "Overlay media publication and orphan recovery could not complete."
            );
        }
        finally
        {
            _ = Gate.Release();
        }
    }

    private void RecoverPublication(OverlayMediaDocument document, string root)
    {
        var destination = Path.Combine(root, document.StorageKey);
        if (
            !File.Exists(destination)
            && document.LegacyHostId is { } hostId
            && document.LegacyStorageKey is { } legacyStorageKey
        )
        {
            var source = Path.Combine(
                OverlayMediaDirectory.HostDirectory(
                    BlokeBotLocalState.Directory(options.Value),
                    hostId
                ),
                legacyStorageKey
            );
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        document.State =
            File.Exists(destination) && new FileInfo(destination).Length == document.ByteLength
                ? OverlayMediaDocumentState.Available
                : OverlayMediaDocumentState.Unavailable;
        document.LegacyHostId = null;
        document.LegacyStorageKey = null;
        document.UpdatedAtUtc = Now();
    }

    private string DocumentDirectory()
    {
        var directory = OverlayMediaDirectory.DocumentDirectory(
            BlokeBotLocalState.Directory(options.Value)
        );
        _ = Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
        return directory;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}

using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomAnnouncementScheduler(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ICustomAnnouncementSender sender,
    ICustomAnnouncementTickScheduler scheduler,
    CustomMessageSelector messageSelector,
    CustomCommandTemplateRenderer templates,
    IOptions<BlokeBotOptions> options,
    ILogger<CustomAnnouncementScheduler> log
) : BackgroundService
{
    internal async Task RunTickAsync(CancellationToken cancellationToken)
    {
        var now = scheduler.GetUtcNow();
        await using var candidateDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidateRows = await (
            from announcement in candidateDb.CustomAnnouncements.AsNoTracking()
            join host in candidateDb.Hosts.AsNoTracking() on announcement.HostId equals host.Id
            where
                announcement.Enabled
                && host.BotRuntimeState == BotChannelRuntimeState.Started
                && (host.EnabledFeatures & HostFeatureFlags.CustomCommands)
                    == HostFeatureFlags.CustomCommands
            orderby announcement.Id
            select new
            {
                AnnouncementId = announcement.Id,
                HostId = host.Id,
                HostLogin = host.Login,
                TwitchUserId = host.TwitchUserId ?? string.Empty,
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc,
            }
        ).ToListAsync(cancellationToken);
        var candidates = candidateRows
            .Select(row => new AnnouncementCandidate(
                row.AnnouncementId,
                row.HostId,
                row.HostLogin,
                row.TwitchUserId,
                HostedChannelRuntimeLifecycle
                    .FromPersistence(row.BotRuntimeState, row.BotRuntimeStateChangedAtUtc)
                    .Match(
                        _ => throw new PersistenceDataIntegrityException(typeof(BotHost)),
                        _ => throw new PersistenceDataIntegrityException(typeof(BotHost)),
                        static started => started,
                        _ => throw new PersistenceDataIntegrityException(typeof(BotHost))
                    )
            ))
            .ToArray();

        foreach (var candidate in candidates)
        {
            try
            {
                await ProcessCandidateAsync(candidate, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                log.LogError(
                    "Custom announcement {AnnouncementId} candidate processing failed for host {HostLogin}; FailureType: {FailureType}.",
                    candidate.AnnouncementId,
                    candidate.HostLogin,
                    exception.GetType().Name
                );
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                log.LogError(
                    "Custom announcement scheduler tick failed; FailureType: {FailureType}.",
                    exception.GetType().Name
                );
            }

            try
            {
                await scheduler.DelayAsync(TickInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}

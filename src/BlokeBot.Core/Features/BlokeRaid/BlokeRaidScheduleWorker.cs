using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.BlokeRaid;

internal sealed class BlokeRaidScheduleWorker(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BlokeRaidService raids,
    TimeProvider timeProvider,
    ILogger<BlokeRaidScheduleWorker> log
) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, timeProvider);
        await ProcessAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessAsync(stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var hostIds = await db
                .Hosts.AsNoTracking()
                .Where(host =>
                    (host.EnabledFeatures & HostFeatureFlags.CooperativeGame)
                        == HostFeatureFlags.CooperativeGame
                    && (
                        db.BlokeRaidCampaigns.Any(campaign =>
                            campaign.HostId == host.Id
                            && campaign.Status == BlokeRaidCampaignStatus.Active
                            && campaign.EndsAtUtc <= now
                        )
                        || db.BlokeRaidConfigurations.Any(configuration =>
                            configuration.HostId == host.Id
                            && configuration.ResetPolicy == BlokeRaidResetPolicy.Weekly
                            && configuration.NextWeeklyResetAtUtc != null
                            && configuration.NextWeeklyResetAtUtc <= now
                        )
                    )
                )
                .Select(host => host.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var hostId in hostIds)
            {
                await raids.ProcessDueWorkAsync(hostId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(exception, "BlokeRaid scheduled work failed.");
        }
    }
}

using System.Globalization;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

public sealed class CompetitionReminderWorker(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ICompetitionReminderDelivery delivery,
    TimeProvider timeProvider,
    ILogger<CompetitionReminderWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        do
        {
            try
            {
                _ = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Competition reminder scan failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var due = await db
            .CompetitionMatches.AsSplitQuery()
            .Include(x => x.Competition)
                .ThenInclude(x => x.Entrants)
                    .ThenInclude(x => x.Members)
            .Where(x =>
                x.Competition.Status == CompetitionStatus.Running
                && x.Status == CompetitionMatchStatus.Pending
                && x.ReminderDueAtUtc <= now
                && x.ReminderDeliveredAtUtc == null
                && x.ReminderSuppressedAtUtc == null
                && db.Hosts.Any(host =>
                    host.Id == x.HostId
                    && (host.EnabledFeatures & HostFeatureFlags.Competitions)
                        == HostFeatureFlags.Competitions
                    && (
                        host.CompetitionsAcceptWorkAfterUtc == null
                        || x.ReminderDueAtUtc >= host.CompetitionsAcceptWorkAfterUtc
                    )
                )
            )
            .OrderBy(x => x.ReminderDueAtUtc)
            .Take(50)
            .ToArrayAsync(ct);
        var delivered = 0;
        foreach (var match in due)
        {
            var hostLogin = await db
                .Hosts.Where(x => x.Id == match.HostId)
                .Select(x => x.Login)
                .SingleAsync(ct);
            var entrantIds = new[] { match.EntrantAId, match.EntrantBId }
                .Where(x => x is not null)
                .Select(x => x!.Value)
                .ToHashSet();
            var recipients = match
                .Competition.Entrants.Where(x => entrantIds.Contains(x.Id))
                .SelectMany(x => x.Members)
                .Select(x => new CompetitionReminderRecipient(x.Login, x.TwitchUserId))
                .ToArray();
            var sent = await delivery.DeliverAsync(
                hostLogin,
                RenderMessage(match, hostLogin),
                recipients,
                ct
            );
            if (!sent)
            {
                continue;
            }
            match.ReminderDeliveredAtUtc = now;
            delivered++;
            _ = await db.SaveChangesAsync(ct);
        }
        return delivered;
    }

    private static string RenderMessage(CompetitionMatch match, string hostLogin) =>
        match
            .Competition.ReminderMessage.Replace(
                "{competition}",
                match.Competition.Name,
                StringComparison.Ordinal
            )
            .Replace(
                "{round}",
                match.Round.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            )
            .Replace(
                "{scheduled}",
                match.ScheduledAtUtc?.ToString(
                    "yyyy-MM-dd HH:mm 'UTC'",
                    CultureInfo.InvariantCulture
                ) ?? "to be announced",
                StringComparison.Ordinal
            )
            .Replace("{public_url}", $"/competitions/{hostLogin}", StringComparison.Ordinal);
}

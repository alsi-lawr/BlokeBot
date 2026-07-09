using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayDrawService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointBalanceService balances,
    IPointsRandom random,
    PointsGiveawayMessageFormatter formatter,
    PointsChangeNotifier changes
)
{
    public async Task<PointOperationResult> DrawAsync(int giveawayId, CancellationToken ct)
    {
        var outcome = await DrawOutcomeAsync(giveawayId, ct);
        var delivery = outcome.Settings is { } settings
            ? await LoadReplyDeliveryAsync(settings.HostId, ct)
            : new ReplyDeliveryMap();
        return formatter.Reply(outcome, delivery);
    }

    internal async Task<PointsGiveawayDrawOutcome> DrawOutcomeAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var giveawayHeader = await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.Id == giveawayId)
            .Select(x => new { x.HostId, x.Status })
            .SingleOrDefaultAsync(ct);
        if (giveawayHeader is null)
            return PointsGiveawayDrawOutcome.Missing();

        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, giveawayHeader.HostId, ct);
        if (giveawayHeader.Status != PointsGiveawayStatus.Active)
            return PointsGiveawayDrawOutcome.NotActive(settings);

        var now = DateTime.UtcNow;
        var claimed = await db
            .PointsGiveaways.Where(x =>
                x.Id == giveawayId && x.Status == PointsGiveawayStatus.Active
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(x => x.Status, PointsGiveawayStatus.Completed)
                        .SetProperty(x => x.CompletedAtUtc, now),
                ct
            );
        if (claimed == 0)
            return PointsGiveawayDrawOutcome.NotActive(settings);

        var giveaway = await db
            .PointsGiveaways.Include(x => x.Entrants)
            .Include(x => x.Winners)
            .SingleAsync(x => x.Id == giveawayId, ct);
        var entrants = giveaway
            .Entrants.Select(x => x.Login)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (entrants.Count == 0)
        {
            await tx.CommitAsync(ct);
            await changes.NotifyChangedAsync();
            return PointsGiveawayDrawOutcome.NoEntrants(settings);
        }

        var winnerCount = Math.Min(Math.Max(1, giveaway.WinnerCount), entrants.Count);
        var winners = entrants
            .OrderBy(_ => random.Next(0, int.MaxValue))
            .Take(winnerCount)
            .ToArray();
        var winnerPayouts = new List<PointsGiveawayWinnerPayout>();
        foreach (var winner in winners)
        {
            var payout = RandomPayout(giveaway.MinimumPayout, giveaway.MaximumPayout);
            winnerPayouts.Add(new PointsGiveawayWinnerPayout(winner, payout));
            giveaway.Winners.Add(
                new PointsGiveawayWinner
                {
                    GiveawayId = giveaway.Id,
                    Login = winner,
                    Payout = payout.ToString(),
                }
            );
            await balances.AwardGiveawayAsync(
                db,
                giveaway.HostId,
                giveaway.Id,
                winner,
                payout,
                now,
                ct
            );
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await changes.NotifyChangedAsync();
        return PointsGiveawayDrawOutcome.WithWinners(settings, winnerPayouts);
    }

    private PointAmount RandomPayout(string minimum, string maximum)
    {
        var min = PointAmount.ParseAbsolute(minimum).Value / 10;
        var max = PointAmount.ParseAbsolute(maximum).Value / 10;
        var range = max - min;
        var offset =
            range <= int.MaxValue ? random.Next(0, (int)range + 1) : random.Next(0, int.MaxValue);
        return new PointAmount((min + offset) * 10);
    }

    private async Task<ReplyDeliveryMap> LoadReplyDeliveryAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PointsGiveawayQueries.LoadReplyDeliveryAsync(db, hostId, ct);
    }
}

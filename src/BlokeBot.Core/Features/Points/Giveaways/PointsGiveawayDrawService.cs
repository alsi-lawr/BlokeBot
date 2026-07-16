using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.Points.Giveaways;

public sealed class PointsGiveawayDrawService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointBalanceService balances,
    IPointsRandom random
)
{
    internal async Task<PointsGiveawayDrawOutcome> DrawOutcomeAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        PointsGiveawayDrawOutcome? committedOutcome = null;
        try
        {
            return await DrawAndCommitOutcomeAsync(
                giveawayId,
                outcome => committedOutcome = outcome,
                ct
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PointsGiveawayDrawCommitAmbiguousException)
        {
            throw;
        }
        catch (Exception exception) when (committedOutcome is not null)
        {
            throw new PointsGiveawayDrawPostCommitException(
                giveawayId,
                committedOutcome,
                exception
            );
        }
    }

    private async Task<PointsGiveawayDrawOutcome> DrawAndCommitOutcomeAsync(
        int giveawayId,
        Action<PointsGiveawayDrawOutcome> onCommitted,
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
        {
            return new PointsGiveawayDrawOutcome.Missing();
        }

        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, giveawayHeader.HostId, ct);
        if (giveawayHeader.Status != PointsGiveawayStatus.Active)
        {
            return new PointsGiveawayDrawOutcome.NotActive(settings);
        }

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
        {
            return new PointsGiveawayDrawOutcome.NotActive(settings);
        }

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
            var outcome = new PointsGiveawayDrawOutcome.NoEntrants(settings);
            await CommitAsync(tx, giveawayId, outcome, ct);
            onCommitted(outcome);
            return outcome;
        }

        var winnerCount = Math.Min(Math.Max(1, giveaway.WinnerCount), entrants.Count);
        var winners = entrants
            .OrderBy(_ => random.Next(0, int.MaxValue))
            .Take(winnerCount)
            .ToArray();
        Result<List<PointsGiveawayWinnerPayout>, PointBalanceMutationFailure> payoutAttempt =
            Result<List<PointsGiveawayWinnerPayout>, PointBalanceMutationFailure>.Success([]);
        foreach (var winner in winners)
        {
            payoutAttempt = await payoutAttempt.Match(
                async winnerPayouts =>
                {
                    var payout = RandomPayout(giveaway.MinimumPayout, giveaway.MaximumPayout);
                    var result = await balances
                        .AwardGiveaway(db, giveaway.HostId, giveaway.Id, winner, payout, now)
                        .ExecuteAsync(ct);
                    return result.Match(
                        mutation =>
                        {
                            winnerPayouts.Add(
                                new PointsGiveawayWinnerPayout(winner, mutation.Amount)
                            );
                            giveaway.Winners.Add(
                                new PointsGiveawayWinner
                                {
                                    GiveawayId = giveaway.Id,
                                    Login = winner,
                                    Payout = mutation.Amount.ToString(),
                                }
                            );
                            return Result<
                                List<PointsGiveawayWinnerPayout>,
                                PointBalanceMutationFailure
                            >.Success(winnerPayouts);
                        },
                        Result<List<PointsGiveawayWinnerPayout>, PointBalanceMutationFailure>.Error
                    );
                },
                failure =>
                    Task.FromResult(
                        Result<List<PointsGiveawayWinnerPayout>, PointBalanceMutationFailure>.Error(
                            failure
                        )
                    )
            );
        }

        return await payoutAttempt.Match(CommitWinnersAsync, PayoutFailedAsync);

        async Task<PointsGiveawayDrawOutcome> CommitWinnersAsync(
            List<PointsGiveawayWinnerPayout> winnerPayouts
        )
        {
            await db.SaveChangesAsync(ct);
            var completed = new PointsGiveawayDrawOutcome.Winners(settings, winnerPayouts);
            await CommitAsync(tx, giveawayId, completed, ct);
            onCommitted(completed);
            return completed;
        }

        Task<PointsGiveawayDrawOutcome> PayoutFailedAsync(PointBalanceMutationFailure failure)
        {
            return Task.FromResult<PointsGiveawayDrawOutcome>(
                new PointsGiveawayDrawOutcome.PayoutFailed(settings, failure)
            );
        }
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

    private static async Task CommitAsync(
        IDbContextTransaction transaction,
        int giveawayId,
        PointsGiveawayDrawOutcome intendedOutcome,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            throw new PointsGiveawayDrawCommitAmbiguousException(
                giveawayId,
                intendedOutcome,
                exception
            );
        }
    }
}

internal sealed class PointsGiveawayDrawCommitAmbiguousException(
    int giveawayId,
    PointsGiveawayDrawOutcome intendedOutcome,
    Exception innerException
) : Exception("The points giveaway draw commit outcome is ambiguous.", innerException)
{
    internal int GiveawayId { get; } = giveawayId;

    internal PointsGiveawayDrawOutcome IntendedOutcome { get; } = intendedOutcome;
}

internal sealed class PointsGiveawayDrawPostCommitException(
    int giveawayId,
    PointsGiveawayDrawOutcome committedOutcome,
    Exception innerException
) : Exception("The committed points giveaway draw cleanup failed.", innerException)
{
    internal int GiveawayId { get; } = giveawayId;

    internal PointsGiveawayDrawOutcome CommittedOutcome { get; } = committedOutcome;
}

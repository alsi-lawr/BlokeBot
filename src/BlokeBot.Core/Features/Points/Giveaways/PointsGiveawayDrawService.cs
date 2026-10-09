using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.Points.Giveaways;

public sealed class PointsGiveawayDrawService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointBalanceService balances,
    IPointsRandom random,
    IEnumerable<IOverlayEventPresenter> eventPresenters,
    AutomationFeatureLifecycle? automations = null
)
{
    public PointsGiveawayDrawService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        PointBalanceService balances,
        IPointsRandom random
    )
        : this(dbFactory, balances, random, []) { }

    internal async Task<PointsGiveawayDrawOutcome> DrawOutcomeAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        PointsGiveawayDrawOutcome? committedOutcome = null;
        var preparation = new DrawPreparation();
        try
        {
            DrawWork work;
            for (var attempt = 1; ; attempt++)
            {
                var phase = new DrawAttemptPhase();
                try
                {
                    work = await DrawAttemptAsync(
                        giveawayId,
                        preparation,
                        phase,
                        outcome => committedOutcome = outcome,
                        ct
                    );
                    break;
                }
                catch (Exception exception)
                    when (attempt < 20
                        && committedOutcome is null
                        && !ct.IsCancellationRequested
                        && phase.CanRetry(exception)
                    )
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), ct);
                }
            }
            if (work.HostId is { } hostId)
            {
                var key = giveawayId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (automations is not null)
                {
                    await automations.EmitAsync(
                        hostId,
                        FeatureLifecycleKind.GiveawayClosed,
                        key,
                        key,
                        work.Now,
                        work.Outcome is PointsGiveawayDrawOutcome.NoEntrants
                            ? "no entrants"
                            : "draw complete",
                        ct
                    );
                }
                if (work.Outcome is PointsGiveawayDrawOutcome.Winners winners)
                {
                    if (automations is not null)
                    {
                        await automations.EmitAsync(
                            hostId,
                            FeatureLifecycleKind.GiveawayWinners,
                            key,
                            key,
                            work.Now,
                            string.Join(", ", winners.Payouts.Select(value => value.Login)),
                            ct
                        );
                    }
                    foreach (var presenter in eventPresenters)
                    {
                        await presenter.PresentAsync(
                            new OverlayEventPresentation.GiveawayWinner
                            {
                                HostId = hostId,
                                SourceKey = key,
                                Winners = winners
                                    .Payouts.Select(value => value.Login)
                                    .ToImmutableArray(),
                                Prizes = winners
                                    .Payouts.Select(value =>
                                        $"{value.Payout.ToDisplayString()} {winners.Settings.PointLabel}"
                                    )
                                    .ToImmutableArray(),
                                PointLabel = winners.Settings.PointLabel,
                            },
                            ct
                        );
                    }
                }
            }
            return work.Outcome;
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

    private async Task<DrawWork> DrawAttemptAsync(
        int giveawayId,
        DrawPreparation preparation,
        DrawAttemptPhase phase,
        Action<PointsGiveawayDrawOutcome> onCommitted,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            ct
        );
        phase.Acquired = true;
        try
        {
            var header = await db
                .PointsGiveaways.AsNoTracking()
                .Where(value => value.Id == giveawayId)
                .Select(value => new { value.HostId, value.Status })
                .SingleOrDefaultAsync(ct);
            if (header is null)
            {
                return new(new PointsGiveawayDrawOutcome.Missing(), null, default);
            }
            var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, header.HostId, ct);
            if (header.Status != PointsGiveawayStatus.Active)
            {
                return new(new PointsGiveawayDrawOutcome.NotActive(settings), null, default);
            }
            var now = DateTime.UtcNow;
            var claimed = await db
                .PointsGiveaways.Where(value =>
                    value.Id == giveawayId && value.Status == PointsGiveawayStatus.Active
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(value => value.Status, PointsGiveawayStatus.Completed)
                            .SetProperty(value => value.CompletedAtUtc, now),
                    ct
                );
            if (claimed == 0)
            {
                return new(new PointsGiveawayDrawOutcome.NotActive(settings), null, default);
            }
            var giveaway = await db
                .PointsGiveaways.Include(value => value.Entrants)
                .Include(value => value.Winners)
                .SingleAsync(value => value.Id == giveawayId, ct);
            var entrants = giveaway
                .Entrants.Select(value => value.Login)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var entrant in entrants)
            {
                if (!preparation.Ranks.ContainsKey(entrant))
                {
                    preparation.Ranks.Add(
                        entrant,
                        (random.Next(0, int.MaxValue), preparation.Ranks.Count)
                    );
                }
            }
            PointsGiveawayDrawOutcome outcome;
            if (entrants.Length == 0)
            {
                outcome = new PointsGiveawayDrawOutcome.NoEntrants(settings);
            }
            else
            {
                var winnerCount = Math.Min(Math.Max(1, giveaway.WinnerCount), entrants.Length);
                var winners = entrants
                    .OrderBy(value => preparation.Ranks[value].Rank)
                    .ThenBy(value => preparation.Ranks[value].Order)
                    .Take(winnerCount)
                    .ToArray();
                var payouts = new List<PointsGiveawayWinnerPayout>();
                foreach (var winner in winners)
                {
                    if (!preparation.Payouts.TryGetValue(winner, out var payout))
                    {
                        payout = RandomPayout(giveaway.MinimumPayout, giveaway.MaximumPayout);
                        preparation.Payouts.Add(winner, payout);
                    }
                    var mutation = await balances
                        .AwardGiveaway(db, giveaway.HostId, giveaway.Id, winner, payout, now)
                        .ExecuteAsync(ct);
                    var failure = mutation.Match<PointBalanceMutationFailure?>(
                        _ => null,
                        value => value
                    );
                    if (failure is not null)
                    {
                        return new(
                            new PointsGiveawayDrawOutcome.PayoutFailed(settings, failure),
                            null,
                            default
                        );
                    }
                    payouts.Add(new(winner, payout));
                    giveaway.Winners.Add(
                        new PointsGiveawayWinner
                        {
                            GiveawayId = giveaway.Id,
                            Login = winner,
                            Payout = payout.ToString(),
                        }
                    );
                }
                _ = await db.SaveChangesAsync(ct);
                outcome = new PointsGiveawayDrawOutcome.Winners(settings, payouts);
            }
            await CommitAsync(tx, giveawayId, outcome, ct);
            phase.Committed = true;
            onCommitted(outcome);
            return new(outcome, giveaway.HostId, now);
        }
        catch (Exception exception)
            when (!phase.Committed
                && !ct.IsCancellationRequested
                && MainDatabaseFailureClassifier.IsContention(exception)
            )
        {
            await tx.RollbackAsync(CancellationToken.None);
            phase.RolledBack = true;
            throw;
        }
    }

    private sealed record DrawWork(PointsGiveawayDrawOutcome Outcome, int? HostId, DateTime Now);

    private sealed class DrawPreparation
    {
        public Dictionary<string, (int Rank, int Order)> Ranks { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PointAmount> Payouts { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DrawAttemptPhase
    {
        public bool Acquired { get; set; }
        public bool Committed { get; set; }
        public bool RolledBack { get; set; }

        public bool CanRetry(Exception exception) =>
            !Committed
            && (!Acquired || RolledBack)
            && MainDatabaseFailureClassifier.IsContention(exception);
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
        catch (Exception exception) when (!MainDatabaseFailureClassifier.IsContention(exception))
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

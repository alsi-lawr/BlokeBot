using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using PointMutationIO = BlokeBot.Functional.IO<
    BlokeBot.Core.Features.Points.Balances.PointBalanceMutation,
    BlokeBot.Core.Features.Points.Balances.PointBalanceMutationFailure
>;
using PointMutationResult = BlokeBot.Functional.Result<
    BlokeBot.Core.Features.Points.Balances.PointBalanceMutation,
    BlokeBot.Core.Features.Points.Balances.PointBalanceMutationFailure
>;

namespace BlokeBot.Core.Features.Points.Balances;

public sealed partial class PointBalanceService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IEnumerable<IOverlayEventPresenter> eventPresenters
)
{
    public PointBalanceService(IDbContextFactory<BlokeBotDbContext> dbFactory)
        : this(dbFactory, []) { }

    public async Task<PointBalanceEntry> GetBalanceAsync(
        int hostId,
        string login,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalized = LoginName.Parse(login).Value;
        var row = await db
            .PointBalances.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HostId == hostId && x.Login == normalized, ct);

        return new PointBalanceEntry(
            normalized,
            row is null ? PointAmount.Zero : PointAmount.ParseAbsolute(row.Amount),
            row?.UpdatedAtUtc ?? DateTime.MinValue
        );
    }

    public async Task<IReadOnlyList<PointBalanceEntry>> GetLeaderboardAsync(
        int hostId,
        int count,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db
            .PointBalances.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);

        return rows.Select(x => new PointBalanceEntry(
                x.Login,
                PointAmount.ParseAbsolute(x.Amount),
                x.UpdatedAtUtc
            ))
            .OrderByDescending(x => x.Balance.Value)
            .ThenBy(x => x.Login)
            .Take(count)
            .ToArray();
    }

    public async Task<IReadOnlyList<PointLedgerEntryView>> GetRecentLedgerAsync(
        int hostId,
        int count,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entries = await db
            .PointLedgerEntries.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(count)
            .ToListAsync(ct);
        return entries
            .Select(x => new PointLedgerEntryView(
                x.CreatedAtUtc,
                x.Kind,
                x.Login,
                FormatSignedDelta(x.Delta),
                PointAmount.ParseAbsolute(x.BalanceAfter).ToDisplayString(),
                x.ActorLogin,
                x.CounterpartyLogin,
                x.Note
            ))
            .ToList();
    }

    private static string FormatSignedDelta(string delta)
    {
        var value = BigInteger.Parse(delta, CultureInfo.InvariantCulture);
        var display = new PointAmount(BigInteger.Abs(value)).ToDisplayString();
        return value.Sign switch
        {
            < 0 => $"-{display}",
            > 0 => $"+{display}",
            _ => display,
        };
    }

    public PointMutationIO Add(
        int hostId,
        string targetLogin,
        PointAmount amount,
        string actorLogin,
        string note
    ) => PointMutationIO.Create(ct => AddAsync(hostId, targetLogin, amount, actorLogin, note, ct));

    private async ValueTask<PointMutationResult> AddAsync(
        int hostId,
        string targetLogin,
        PointAmount amount,
        string actorLogin,
        string note,
        CancellationToken ct
    )
    {
        if (amount.IsZero)
        {
            return Failure(new PointBalanceMutationFailure.InvalidAmount());
        }
        var login = LoginName.Parse(targetLogin).Value;
        var result = await RetryCreditAsync(
            async (db, now) =>
            {
                var mutation = await CreditAsync(db, hostId, login, amount, now, ct);
                return mutation.Map(value =>
                {
                    AddLedger(
                        db,
                        hostId,
                        PointLedgerKind.Add,
                        login,
                        amount.Value,
                        value.Balance,
                        actorLogin,
                        null,
                        null,
                        note,
                        now
                    );
                    return value;
                });
            },
            ct
        );
        return await result.Match<ValueTask<PointMutationResult>>(
            async mutation =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var label =
                    await db
                        .PointsSettings.AsNoTracking()
                        .Where(x => x.HostId == hostId)
                        .Select(x => x.PointLabel)
                        .SingleOrDefaultAsync(ct)
                    ?? "points";
                foreach (var presenter in eventPresenters)
                {
                    await presenter.PresentAsync(
                        new OverlayEventPresentation.PointAward
                        {
                            HostId = hostId,
                            SourceKey = mutation.LedgerId.ToString(CultureInfo.InvariantCulture),
                            Recipient = login,
                            Amount = amount.ToDisplayString(),
                            PointLabel = label,
                        },
                        ct
                    );
                }
                return Success(mutation.Mutation.Balance, amount);
            },
            failure => ValueTask.FromResult(Failure(failure))
        );
    }

    public PointMutationIO Remove(
        int hostId,
        string targetLogin,
        PointAmount amount,
        string actorLogin,
        string note
    ) =>
        PointMutationIO.Create(ct =>
            RemoveAsync(hostId, targetLogin, amount, actorLogin, note, ct)
        );

    private async ValueTask<PointMutationResult> RemoveAsync(
        int hostId,
        string targetLogin,
        PointAmount amount,
        string actorLogin,
        string note,
        CancellationToken ct
    )
    {
        if (amount.IsZero)
        {
            return Failure(new PointBalanceMutationFailure.InvalidAmount());
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var target = new PointBalanceTarget(hostId, LoginName.Parse(targetLogin).Value);
        await MainDatabaseStatements.EnsurePointBalanceAsync(db, target, now, ct);
        var outcome = await MainDatabaseStatements.ApplyPointDeltaAsync(
            db,
            target,
            CanonicalPointInteger.From(-amount.Value),
            new(
                CanonicalPointInteger.From(amount.Value),
                CanonicalPointInteger.From(PointAmount.MaximumValue)
            ),
            now,
            ct
        );
        var prepared = outcome.Match(
            applied => Success(new PointAmount(applied.After.ToBigInteger()), amount),
            rejected =>
                Failure(
                    new PointBalanceMutationFailure.InsufficientBalance(
                        CurrentAmount(rejected.Current),
                        amount
                    )
                )
        );
        if (prepared.Match(_ => false, _ => true))
        {
            return prepared;
        }
        var next = prepared.Match(value => value.Balance, _ => PointAmount.Zero);
        AddLedger(
            db,
            hostId,
            PointLedgerKind.Remove,
            target.Login,
            -amount.Value,
            next,
            actorLogin,
            null,
            null,
            note,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(next, amount);
    }

    public PointMutationIO DeleteBalance(
        int hostId,
        string targetLogin,
        string actorLogin,
        string note
    ) =>
        PointMutationIO.Create(ct => DeleteBalanceAsync(hostId, targetLogin, actorLogin, note, ct));

    private async ValueTask<PointMutationResult> DeleteBalanceAsync(
        int hostId,
        string targetLogin,
        string actorLogin,
        string note,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var normalized = LoginName.Parse(targetLogin).Value;
        var deleted = await MainDatabaseStatements.DeletePointBalanceAsync(
            db,
            new(hostId, normalized),
            ct
        );
        var before = deleted.Match<PointAmount?>(
            value => new PointAmount(value.Before.ToBigInteger()),
            _ => null
        );
        if (before is null)
        {
            return Failure(new PointBalanceMutationFailure.UnknownUser());
        }
        var current = before.Value;
        var now = DateTime.UtcNow;
        AddLedger(
            db,
            hostId,
            PointLedgerKind.DeleteBalance,
            normalized,
            -current.Value,
            PointAmount.Zero,
            actorLogin,
            null,
            null,
            note,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(PointAmount.Zero, current);
    }

    public PointMutationIO Transfer(
        int hostId,
        string fromLogin,
        string toLogin,
        PointAmount amount
    ) => PointMutationIO.Create(ct => TransferAsync(hostId, fromLogin, toLogin, amount, ct));

    private async ValueTask<PointMutationResult> TransferAsync(
        int hostId,
        string fromLogin,
        string toLogin,
        PointAmount amount,
        CancellationToken ct
    )
    {
        if (amount.IsZero)
        {
            return Failure(new PointBalanceMutationFailure.InvalidAmount());
        }

        var from = LoginName.Parse(fromLogin).Value;
        var to = LoginName.Parse(toLogin).Value;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(new PointBalanceMutationFailure.InvalidAmount());
        }

        var result = await RetryCreditAsync(
            async (db, now) =>
            {
                async ValueTask<PointMutationResult> DebitAsync()
                {
                    var source = new PointBalanceTarget(hostId, from);
                    await MainDatabaseStatements.EnsurePointBalanceAsync(db, source, now, ct);
                    var debit = await MainDatabaseStatements.ApplyPointDeltaAsync(
                        db,
                        source,
                        CanonicalPointInteger.From(-amount.Value),
                        new(
                            CanonicalPointInteger.From(amount.Value),
                            CanonicalPointInteger.From(PointAmount.MaximumValue)
                        ),
                        now,
                        ct
                    );
                    return debit.Match(
                        applied => Success(new PointAmount(applied.After.ToBigInteger()), amount),
                        rejected =>
                            Failure(
                                new PointBalanceMutationFailure.InsufficientBalance(
                                    CurrentAmount(rejected.Current),
                                    amount
                                )
                            )
                    );
                }
                // Both rows are changed in normalized login order; either failure rolls back the whole attempt.
                var creditFirst = StringComparer.Ordinal.Compare(to, from) < 0;
                var first = creditFirst
                    ? await CreditAsync(db, hostId, to, amount, now, ct)
                    : await DebitAsync();
                return await first.Match<ValueTask<PointMutationResult>>(
                    async firstMutation =>
                    {
                        var second = creditFirst
                            ? await DebitAsync()
                            : await CreditAsync(db, hostId, to, amount, now, ct);
                        return second.Map(secondMutation =>
                        {
                            var debit = creditFirst ? secondMutation : firstMutation;
                            var credit = creditFirst ? firstMutation : secondMutation;
                            AddLedger(
                                db,
                                hostId,
                                PointLedgerKind.TransferOut,
                                from,
                                -amount.Value,
                                debit.Balance,
                                from,
                                to,
                                null,
                                string.Empty,
                                now
                            );
                            AddLedger(
                                db,
                                hostId,
                                PointLedgerKind.TransferIn,
                                to,
                                amount.Value,
                                credit.Balance,
                                from,
                                from,
                                null,
                                string.Empty,
                                now
                            );
                            return new PointBalanceMutation(debit.Balance, amount);
                        });
                    },
                    async failure =>
                    {
                        if (!creditFirst)
                        {
                            return Failure(failure);
                        }
                        var current = await ReadCurrentAmountAsync(db, hostId, from, ct);
                        return current < amount
                            ? Failure(
                                new PointBalanceMutationFailure.InsufficientBalance(current, amount)
                            )
                            : Failure(failure);
                    }
                );
            },
            ct
        );
        return result.Map(value => value.Mutation);
    }

    public PointMutationIO ApplyGamble(
        int hostId,
        string login,
        PointAmount stake,
        PointGambleOutcome outcome
    ) => PointMutationIO.Create(ct => ApplyGambleAsync(hostId, login, stake, outcome, ct));

    private async ValueTask<PointMutationResult> ApplyGambleAsync(
        int hostId,
        string login,
        PointAmount stake,
        PointGambleOutcome outcome,
        CancellationToken ct
    )
    {
        if (stake.IsZero)
        {
            return Failure(new PointBalanceMutationFailure.InvalidAmount());
        }

        var normalized = LoginName.Parse(login).Value;
        var result = await RetryCreditAsync(
            async (db, now) =>
            {
                var ceiling = await PointCreditCapacity.LoadCeilingAsync(
                    db,
                    hostId,
                    normalized,
                    ct
                );
                var won = outcome.Match(_ => true, _ => false);
                var maximum = won
                    ? ceiling.Match(
                        value => value.Amount.Value,
                        _ => System.Numerics.BigInteger.MinusOne
                    )
                    : PointAmount.MaximumValue;
                if (maximum.Sign < 0)
                {
                    var current = await ReadCurrentAmountAsync(db, hostId, normalized, ct);
                    return current < stake
                        ? Failure(
                            new PointBalanceMutationFailure.InsufficientBalance(current, stake)
                        )
                        : Failure(new PointBalanceMutationFailure.CapExceeded(current, stake));
                }
                var target = new PointBalanceTarget(hostId, normalized);
                await MainDatabaseStatements.EnsurePointBalanceAsync(db, target, now, ct);
                var delta = won ? stake.Value : -stake.Value;
                var applied = await MainDatabaseStatements.ApplyPointDeltaAsync(
                    db,
                    target,
                    CanonicalPointInteger.From(delta),
                    new(
                        CanonicalPointInteger.From(stake.Value),
                        CanonicalPointInteger.From(maximum)
                    ),
                    now,
                    ct
                );
                return applied.Match(
                    value =>
                    {
                        var next = new PointAmount(value.After.ToBigInteger());
                        AddLedger(
                            db,
                            hostId,
                            won ? PointLedgerKind.GambleWin : PointLedgerKind.GambleLoss,
                            normalized,
                            delta,
                            next,
                            login,
                            null,
                            null,
                            string.Empty,
                            now
                        );
                        return Success(next, stake);
                    },
                    rejected =>
                        CurrentAmount(rejected.Current) < stake
                            ? Failure(
                                new PointBalanceMutationFailure.InsufficientBalance(
                                    CurrentAmount(rejected.Current),
                                    stake
                                )
                            )
                            : Failure(
                                new PointBalanceMutationFailure.CapExceeded(
                                    CurrentAmount(rejected.Current),
                                    stake
                                )
                            )
                );
            },
            ct
        );
        return result.Map(value => value.Mutation);
    }

    public PointMutationIO AwardGiveaway(
        BlokeBotDbContext db,
        int hostId,
        int giveawayId,
        string login,
        PointAmount amount,
        DateTime now
    ) =>
        PointMutationIO.Create(ct =>
            AwardGiveawayAsync(db, hostId, giveawayId, login, amount, now, ct)
        );

    private async ValueTask<PointMutationResult> AwardGiveawayAsync(
        BlokeBotDbContext db,
        int hostId,
        int giveawayId,
        string login,
        PointAmount amount,
        DateTime now,
        CancellationToken ct
    )
    {
        var normalized = LoginName.Parse(login).Value;
        var mutation = await CreditAsync(db, hostId, normalized, amount, now, ct);
        return mutation.Map(value =>
        {
            AddLedger(
                db,
                hostId,
                PointLedgerKind.GiveawayWin,
                normalized,
                amount.Value,
                value.Balance,
                null,
                null,
                giveawayId,
                string.Empty,
                now
            );
            return value;
        });
    }

    public PointMutationIO AwardGuessWin(
        BlokeBotDbContext db,
        int hostId,
        int roundId,
        string login,
        PointAmount amount,
        DateTime now
    ) =>
        PointMutationIO.Create(ct =>
            AwardGuessWinAsync(db, hostId, roundId, login, amount, now, ct)
        );

    private async ValueTask<PointMutationResult> AwardGuessWinAsync(
        BlokeBotDbContext db,
        int hostId,
        int roundId,
        string login,
        PointAmount amount,
        DateTime now,
        CancellationToken ct
    )
    {
        if (amount.IsZero)
        {
            return Failure(new PointBalanceMutationFailure.InvalidAmount());
        }
        var normalized = LoginName.Parse(login).Value;
        var mutation = await CreditAsync(db, hostId, normalized, amount, now, ct);
        return mutation.Map(value =>
        {
            AddLedger(
                db,
                hostId,
                PointLedgerKind.GuessWin,
                normalized,
                amount.Value,
                value.Balance,
                null,
                null,
                null,
                $"guess round {roundId}",
                now
            );
            return value;
        });
    }

    private static PointMutationResult Success(PointAmount balance, PointAmount amount) =>
        PointMutationResult.Success(new PointBalanceMutation(balance, amount));

    private static PointMutationResult Failure(PointBalanceMutationFailure failure) =>
        PointMutationResult.Error(failure);

    private static void AddLedger(
        BlokeBotDbContext db,
        int hostId,
        PointLedgerKind kind,
        string login,
        BigInteger delta,
        PointAmount balanceAfter,
        string? actorLogin,
        string? counterpartyLogin,
        int? giveawayId,
        string note,
        DateTime now
    ) =>
        db.PointLedgerEntries.Add(
            new PointLedgerEntry
            {
                HostId = hostId,
                CreatedAtUtc = now,
                Kind = kind,
                Login = login,
                Delta = delta.ToString(CultureInfo.InvariantCulture),
                BalanceAfter = balanceAfter.ToString(),
                ActorLogin = actorLogin is null ? null : LoginName.Parse(actorLogin).Value,
                CounterpartyLogin = counterpartyLogin is null
                    ? null
                    : LoginName.Parse(counterpartyLogin).Value,
                GiveawayId = giveawayId,
                Note = note,
            }
        );

    private static PointAmount CurrentAmount(PointBalanceRead read) =>
        read.Match(value => new PointAmount(value.Amount.ToBigInteger()), _ => PointAmount.Zero);

    private static async ValueTask<PointMutationResult> CreditAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        PointAmount amount,
        DateTime now,
        CancellationToken ct
    )
    {
        var ceiling = await PointCreditCapacity.LoadCeilingAsync(db, hostId, login, ct);
        return await ceiling.Match<ValueTask<PointMutationResult>>(
            async available =>
            {
                var result = await MainDatabaseStatements.ApplyCreatingPointCreditAsync(
                    db,
                    new(hostId, login),
                    CanonicalPointInteger.From(amount.Value),
                    CanonicalPointInteger.From(available.Amount.Value),
                    now,
                    ct
                );
                return result.Match(
                    applied => Success(new PointAmount(applied.After.ToBigInteger()), amount),
                    rejected =>
                        Failure(
                            new PointBalanceMutationFailure.CapExceeded(
                                CurrentAmount(rejected.Current),
                                amount
                            )
                        )
                );
            },
            async _ =>
                Failure(
                    new PointBalanceMutationFailure.CapExceeded(
                        await ReadCurrentAmountAsync(db, hostId, login, ct),
                        amount
                    )
                )
        );
    }

    private static async Task<PointAmount> ReadCurrentAmountAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        CancellationToken ct
    )
    {
        var amount = await db
            .PointBalances.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Login == login)
            .Select(value => value.Amount)
            .SingleOrDefaultAsync(ct);
        return amount is null ? PointAmount.Zero : PointAmount.ParseAbsolute(amount);
    }

    private sealed record CommittedPointMutation(PointBalanceMutation Mutation, int LedgerId);

    private async ValueTask<
        Result<CommittedPointMutation, PointBalanceMutationFailure>
    > RetryCreditAsync(
        Func<BlokeBotDbContext, DateTime, ValueTask<PointMutationResult>> operation,
        CancellationToken ct
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            var committed = false;
            try
            {
                transaction = await db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct
                );
                var result = await operation(db, DateTime.UtcNow);
                return await result.Match<
                    ValueTask<Result<CommittedPointMutation, PointBalanceMutationFailure>>
                >(
                    async mutation =>
                    {
                        _ = await db.SaveChangesAsync(ct);
                        var ledgerId = db
                            .ChangeTracker.Entries<PointLedgerEntry>()
                            .Select(value => value.Entity.Id)
                            .First();
                        await transaction.CommitAsync(ct);
                        committed = true;
                        return Result<CommittedPointMutation, PointBalanceMutationFailure>.Success(
                            new(mutation, ledgerId)
                        );
                    },
                    failure =>
                        ValueTask.FromResult(
                            Result<CommittedPointMutation, PointBalanceMutationFailure>.Error(
                                failure
                            )
                        )
                );
            }
            catch (Exception exception)
                when (!committed
                    && attempt < 20
                    && !ct.IsCancellationRequested
                    && MainDatabaseFailureClassifier.IsContention(exception)
                )
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(5 * attempt), ct);
        }
    }
}

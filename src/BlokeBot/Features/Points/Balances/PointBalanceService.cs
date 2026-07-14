using System.Numerics;
using BlokeBot.Functional;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using PointMutationIO = BlokeBot.Functional.IO<
    BlokeBot.Features.Points.Balances.PointBalanceMutation,
    BlokeBot.Features.Points.Balances.PointBalanceMutationFailure
>;
using PointMutationResult = BlokeBot.Functional.Result<
    BlokeBot.Features.Points.Balances.PointBalanceMutation,
    BlokeBot.Features.Points.Balances.PointBalanceMutationFailure
>;

namespace BlokeBot.Features.Points.Balances;

public sealed class PointBalanceService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
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
        return await db
            .PointLedgerEntries.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(count)
            .Select(x => new PointLedgerEntryView(
                x.CreatedAtUtc,
                x.Kind,
                x.Login,
                x.Delta,
                x.BalanceAfter,
                x.ActorLogin,
                x.CounterpartyLogin,
                x.Note
            ))
            .ToListAsync(ct);
    }

    public PointMutationIO Add(
        int hostId,
        string targetLogin,
        PointAmount amount,
        string actorLogin,
        string note
    )
    {
        return PointMutationIO.Create(ct =>
            AddAsync(hostId, targetLogin, amount, actorLogin, note, ct)
        );
    }

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

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var target = await LoadBalanceForUpdateAsync(db, hostId, targetLogin, now, ct);
        var current = PointAmount.ParseAbsolute(target.Amount);
        if (current.Value + amount.Value > PointAmount.MaximumValue)
        {
            return Failure(new PointBalanceMutationFailure.CapExceeded(current, amount));
        }

        var next = current.Add(amount);
        target.Amount = next.ToString();
        target.UpdatedAtUtc = now;
        AddLedger(
            db,
            hostId,
            PointLedgerKind.Add,
            target.Login,
            amount.Value,
            next,
            actorLogin,
            null,
            null,
            note,
            now
        );
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(next, amount);
    }

    public PointMutationIO Remove(
        int hostId,
        string targetLogin,
        PointAmount amount,
        string actorLogin,
        string note
    )
    {
        return PointMutationIO.Create(ct =>
            RemoveAsync(hostId, targetLogin, amount, actorLogin, note, ct)
        );
    }

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
        var target = await LoadBalanceForUpdateAsync(db, hostId, targetLogin, now, ct);
        var current = PointAmount.ParseAbsolute(target.Amount);
        if (current.Value < amount.Value)
        {
            return Failure(new PointBalanceMutationFailure.InsufficientBalance(current, amount));
        }

        var next = current.Subtract(amount);
        target.Amount = next.ToString();
        target.UpdatedAtUtc = now;
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
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(next, amount);
    }

    public PointMutationIO DeleteBalance(
        int hostId,
        string targetLogin,
        string actorLogin,
        string note
    )
    {
        return PointMutationIO.Create(ct =>
            DeleteBalanceAsync(hostId, targetLogin, actorLogin, note, ct)
        );
    }

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
        var row = await db.PointBalances.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.Login == normalized,
            ct
        );
        if (row is null)
        {
            return Failure(new PointBalanceMutationFailure.UnknownUser());
        }

        var current = PointAmount.ParseAbsolute(row.Amount);
        var now = DateTime.UtcNow;
        db.PointBalances.Remove(row);
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
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(PointAmount.Zero, current);
    }

    public PointMutationIO Transfer(
        int hostId,
        string fromLogin,
        string toLogin,
        PointAmount amount
    )
    {
        return PointMutationIO.Create(ct => TransferAsync(hostId, fromLogin, toLogin, amount, ct));
    }

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

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var source = await LoadBalanceForUpdateAsync(db, hostId, from, now, ct);
        var target = await LoadBalanceForUpdateAsync(db, hostId, to, now, ct);
        var sourceCurrent = PointAmount.ParseAbsolute(source.Amount);
        var targetCurrent = PointAmount.ParseAbsolute(target.Amount);
        if (sourceCurrent.Value < amount.Value)
        {
            return Failure(
                new PointBalanceMutationFailure.InsufficientBalance(sourceCurrent, amount)
            );
        }

        if (targetCurrent.Value + amount.Value > PointAmount.MaximumValue)
        {
            return Failure(new PointBalanceMutationFailure.CapExceeded(targetCurrent, amount));
        }

        var sourceNext = sourceCurrent.Subtract(amount);
        var targetNext = targetCurrent.Add(amount);
        source.Amount = sourceNext.ToString();
        source.UpdatedAtUtc = now;
        target.Amount = targetNext.ToString();
        target.UpdatedAtUtc = now;
        AddLedger(
            db,
            hostId,
            PointLedgerKind.TransferOut,
            source.Login,
            -amount.Value,
            sourceNext,
            from,
            target.Login,
            null,
            string.Empty,
            now
        );
        AddLedger(
            db,
            hostId,
            PointLedgerKind.TransferIn,
            target.Login,
            amount.Value,
            targetNext,
            from,
            source.Login,
            null,
            string.Empty,
            now
        );
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(sourceNext, amount);
    }

    public PointMutationIO ApplyGamble(
        int hostId,
        string login,
        PointAmount stake,
        PointGambleOutcome outcome
    )
    {
        return PointMutationIO.Create(ct => ApplyGambleAsync(hostId, login, stake, outcome, ct));
    }

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

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var row = await LoadBalanceForUpdateAsync(db, hostId, login, now, ct);
        var current = PointAmount.ParseAbsolute(row.Amount);
        if (current.Value < stake.Value)
        {
            return Failure(new PointBalanceMutationFailure.InsufficientBalance(current, stake));
        }

        var prepared = outcome.Match(
            _ =>
                current.Value + stake.Value > PointAmount.MaximumValue
                    ? Result<GambleMutation, PointBalanceMutationFailure>.Error(
                        new PointBalanceMutationFailure.CapExceeded(current, stake)
                    )
                    : Result<GambleMutation, PointBalanceMutationFailure>.Success(
                        new GambleMutation(
                            current.Add(stake),
                            stake.Value,
                            PointLedgerKind.GambleWin
                        )
                    ),
            _ =>
                Result<GambleMutation, PointBalanceMutationFailure>.Success(
                    new GambleMutation(
                        current.Subtract(stake),
                        -stake.Value,
                        PointLedgerKind.GambleLoss
                    )
                )
        );

        return await prepared.Match(CommitAsync, failure => ValueTask.FromResult(Failure(failure)));

        async ValueTask<PointMutationResult> CommitAsync(GambleMutation mutation)
        {
            row.Amount = mutation.Balance.ToString();
            row.UpdatedAtUtc = now;
            AddLedger(
                db,
                hostId,
                mutation.LedgerKind,
                row.Login,
                mutation.Delta,
                mutation.Balance,
                login,
                null,
                null,
                string.Empty,
                now
            );
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Success(mutation.Balance, stake);
        }
    }

    public PointMutationIO AwardGiveaway(
        BlokeBotDbContext db,
        int hostId,
        int giveawayId,
        string login,
        PointAmount amount,
        DateTime now
    )
    {
        return PointMutationIO.Create(ct =>
            AwardGiveawayAsync(db, hostId, giveawayId, login, amount, now, ct)
        );
    }

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
        var row = await LoadBalanceForUpdateAsync(db, hostId, login, now, ct);
        var current = PointAmount.ParseAbsolute(row.Amount);
        if (current.Value + amount.Value > PointAmount.MaximumValue)
        {
            return Failure(new PointBalanceMutationFailure.CapExceeded(current, amount));
        }

        var next = current.Add(amount);
        row.Amount = next.ToString();
        row.UpdatedAtUtc = now;
        AddLedger(
            db,
            hostId,
            PointLedgerKind.GiveawayWin,
            row.Login,
            amount.Value,
            next,
            null,
            null,
            giveawayId,
            string.Empty,
            now
        );
        return Success(next, amount);
    }

    public PointMutationIO AwardGuessWin(
        BlokeBotDbContext db,
        int hostId,
        int roundId,
        string login,
        PointAmount amount,
        DateTime now
    )
    {
        return PointMutationIO.Create(ct =>
            AwardGuessWinAsync(db, hostId, roundId, login, amount, now, ct)
        );
    }

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

        var row = await LoadBalanceForUpdateAsync(db, hostId, login, now, ct);
        var current = PointAmount.ParseAbsolute(row.Amount);
        if (current.Value + amount.Value > PointAmount.MaximumValue)
        {
            return Failure(new PointBalanceMutationFailure.CapExceeded(current, amount));
        }

        var next = current.Add(amount);
        row.Amount = next.ToString();
        row.UpdatedAtUtc = now;
        AddLedger(
            db,
            hostId,
            PointLedgerKind.GuessWin,
            row.Login,
            amount.Value,
            next,
            null,
            null,
            null,
            $"guess round {roundId}",
            now
        );
        return Success(next, amount);
    }

    private static PointMutationResult Success(PointAmount balance, PointAmount amount)
    {
        return PointMutationResult.Success(new PointBalanceMutation(balance, amount));
    }

    private static PointMutationResult Failure(PointBalanceMutationFailure failure)
    {
        return PointMutationResult.Error(failure);
    }

    private sealed record GambleMutation(
        PointAmount Balance,
        BigInteger Delta,
        PointLedgerKind LedgerKind
    );

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
    )
    {
        db.PointLedgerEntries.Add(
            new PointLedgerEntry
            {
                HostId = hostId,
                CreatedAtUtc = now,
                Kind = kind,
                Login = login,
                Delta = delta.ToString(),
                BalanceAfter = balanceAfter.ToString(),
                ActorLogin = actorLogin is null ? null : LoginName.Parse(actorLogin).Value,
                CounterpartyLogin = counterpartyLogin is null
                    ? null
                    : LoginName.Parse(counterpartyLogin).Value,
                GiveawayId = giveawayId,
                Note = note,
            }
        );
    }

    private static async Task<PointBalance> LoadBalanceForUpdateAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        DateTime now,
        CancellationToken ct
    )
    {
        var normalized = LoginName.Parse(login).Value;
        var row = await db.PointBalances.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.Login == normalized,
            ct
        );
        if (row is not null)
        {
            return row;
        }

        row = new PointBalance
        {
            HostId = hostId,
            Login = normalized,
            Amount = "0",
            UpdatedAtUtc = now,
        };
        db.PointBalances.Add(row);
        return row;
    }
}

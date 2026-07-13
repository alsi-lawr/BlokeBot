using System.Numerics;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

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

    public async Task<PointOperationResult> AddAsync(
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
            return PointOperationResult.Failure(PointOperationFailureReason.InvalidAmount);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var target = await LoadBalanceForUpdateAsync(db, hostId, targetLogin, now, ct);
        var current = PointAmount.ParseAbsolute(target.Amount);
        if (current.Value + amount.Value > PointAmount.MaximumValue)
        {
            return PointOperationResult.Failure(PointOperationFailureReason.CapExceeded);
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
        return PointOperationResult.Successful(balance: next, amount: amount);
    }

    public async Task<PointOperationResult> RemoveAsync(
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
            return PointOperationResult.Failure(PointOperationFailureReason.InvalidAmount);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var target = await LoadBalanceForUpdateAsync(db, hostId, targetLogin, now, ct);
        var current = PointAmount.ParseAbsolute(target.Amount);
        if (current.Value < amount.Value)
        {
            return PointOperationResult.Failure(
                PointOperationFailureReason.InsufficientBalance,
                balance: current,
                amount: amount
            );
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
        return PointOperationResult.Successful(balance: next, amount: amount);
    }

    public async Task<PointOperationResult> DeleteBalanceAsync(
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
            return PointOperationResult.Failure(PointOperationFailureReason.UnknownUser);
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
        return PointOperationResult.Successful(balance: PointAmount.Zero, amount: current);
    }

    public async Task<PointOperationResult> TransferAsync(
        int hostId,
        string fromLogin,
        string toLogin,
        PointAmount amount,
        CancellationToken ct
    )
    {
        if (amount.IsZero)
        {
            return PointOperationResult.Failure(PointOperationFailureReason.InvalidAmount);
        }

        var from = LoginName.Parse(fromLogin).Value;
        var to = LoginName.Parse(toLogin).Value;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return PointOperationResult.Failure(PointOperationFailureReason.InvalidAmount);
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
            return PointOperationResult.Failure(
                PointOperationFailureReason.InsufficientBalance,
                balance: sourceCurrent,
                amount: amount
            );
        }

        if (targetCurrent.Value + amount.Value > PointAmount.MaximumValue)
        {
            return PointOperationResult.Failure(
                PointOperationFailureReason.CapExceeded,
                balance: targetCurrent,
                amount: amount
            );
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
        return PointOperationResult.Successful(balance: sourceNext, amount: amount);
    }

    public async Task<PointOperationResult> ApplyGambleAsync(
        int hostId,
        string login,
        PointAmount stake,
        bool won,
        CancellationToken ct
    )
    {
        if (stake.IsZero)
        {
            return PointOperationResult.Failure(PointOperationFailureReason.InvalidAmount);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var row = await LoadBalanceForUpdateAsync(db, hostId, login, now, ct);
        var current = PointAmount.ParseAbsolute(row.Amount);
        if (current.Value < stake.Value)
        {
            return PointOperationResult.Failure(
                PointOperationFailureReason.InsufficientBalance,
                balance: current,
                amount: stake
            );
        }

        PointAmount next;
        BigInteger delta;
        if (won)
        {
            if (current.Value + stake.Value > PointAmount.MaximumValue)
            {
                return PointOperationResult.Failure(
                    PointOperationFailureReason.CapExceeded,
                    balance: current,
                    amount: stake
                );
            }

            next = current.Add(stake);
            delta = stake.Value;
        }
        else
        {
            next = current.Subtract(stake);
            delta = -stake.Value;
        }

        row.Amount = next.ToString();
        row.UpdatedAtUtc = now;
        AddLedger(
            db,
            hostId,
            won ? PointLedgerKind.GambleWin : PointLedgerKind.GambleLoss,
            row.Login,
            delta,
            next,
            login,
            null,
            null,
            string.Empty,
            now
        );
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return PointOperationResult.Successful(balance: next, amount: stake);
    }

    public async Task<PointOperationResult> AwardGiveawayAsync(
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
            return PointOperationResult.Failure(
                PointOperationFailureReason.CapExceeded,
                balance: current,
                amount: amount
            );
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
        return PointOperationResult.Successful(balance: next, amount: amount);
    }

    public async Task<PointOperationResult> AwardGuessWinAsync(
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
            return PointOperationResult.Failure(PointOperationFailureReason.InvalidAmount);
        }

        var row = await LoadBalanceForUpdateAsync(db, hostId, login, now, ct);
        var current = PointAmount.ParseAbsolute(row.Amount);
        if (current.Value + amount.Value > PointAmount.MaximumValue)
        {
            return PointOperationResult.Failure(
                PointOperationFailureReason.CapExceeded,
                balance: current,
                amount: amount
            );
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
        return PointOperationResult.Successful(balance: next, amount: amount);
    }

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

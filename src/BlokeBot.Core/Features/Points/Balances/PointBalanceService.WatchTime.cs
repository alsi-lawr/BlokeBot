using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Core.Features.Points.WatchTime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.Points.Balances;

public sealed partial class PointBalanceService
{
    internal async Task<WatchTimeCreditOutcome> CreditWatchTimeAsync(
        WatchTimeObservation observation,
        HelixChatter viewer,
        WatchTimeRuntime runtime,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var identity = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(viewer.UserId))
        );
        var key =
            $"watch-time:{observation.Tick.Epoch:N}:{observation.Tick.Ordinal.ToString(CultureInfo.InvariantCulture)}:{identity}";
        for (var attempt = 1; ; attempt++)
        {
            if (!runtime.IsCurrent(observation))
            {
                return new WatchTimeCreditOutcome.NotAdmitted();
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            IDbContextTransaction? transaction = null;
            var committed = false;
            try
            {
                transaction = await db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct
                );
                var host = await WatchTimeHostQueries
                    .Snapshot(db, observation.Epoch.Settings.HostId)
                    .SingleOrDefaultAsync(ct);
                if (host is null || !Admits(host, observation, runtime))
                {
                    return new WatchTimeCreditOutcome.NotAdmitted();
                }
                if (await HasWatchTimeKeyAsync(db, host.HostId, key, ct))
                {
                    return new WatchTimeCreditOutcome.AlreadyCredited();
                }
                var ceiling = await PointCreditCapacity.LoadCeilingAsync(
                    db,
                    host.HostId,
                    viewer.Login,
                    ct
                );
                var maximum = ceiling.Match<System.Numerics.BigInteger?>(
                    value => value.Amount.Value,
                    _ => null
                );
                if (maximum is null)
                {
                    return new WatchTimeCreditOutcome.CapExceeded();
                }
                var target = new PointBalanceTarget(host.HostId, viewer.Login);
                var now = clock.GetUtcNow().UtcDateTime;
                var credit = await MainDatabaseStatements.ApplyCreatingPointCreditAsync(
                    db,
                    target,
                    CanonicalPointInteger.From(observation.Amount.Value),
                    CanonicalPointInteger.From(maximum.Value),
                    now,
                    ct
                );
                var after = credit.Match<CanonicalPointInteger?>(value => value.After, _ => null);
                if (after is null)
                {
                    return new WatchTimeCreditOutcome.CapExceeded();
                }
                var ledger = await MainDatabaseStatements.TryAppendWatchTimeLedgerAsync(
                    db,
                    new(
                        target,
                        CanonicalPointInteger.From(observation.Amount.Value),
                        after.Value,
                        key,
                        now
                    ),
                    ct
                );
                var inserted = ledger.Match(_ => true, _ => false);
                if (!inserted)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    await transaction.DisposeAsync();
                    transaction = null;
                    return await ResolveWatchTimeKeyAsync(host.HostId, key, ct);
                }
                ct.ThrowIfCancellationRequested();
                if (!runtime.IsCurrent(observation))
                {
                    return new WatchTimeCreditOutcome.NotAdmitted();
                }
                try
                {
                    await transaction.CommitAsync(ct);
                }
                catch (Exception exception)
                    when (!ct.IsCancellationRequested
                        && !MainDatabaseFailureClassifier.IsContention(exception)
                    )
                {
                    await transaction.DisposeAsync();
                    transaction = null;
                    return await ResolveWatchTimeKeyAsync(host.HostId, key, ct);
                }
                committed = true;
                return new WatchTimeCreditOutcome.Credited();
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
            await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), clock, ct);
        }
    }

    private static bool Admits(
        WatchTimeHostSnapshot current,
        WatchTimeObservation observation,
        WatchTimeRuntime runtime
    )
    {
        var expected = observation.Epoch.Settings;
        var account = current.Account(runtime.DefaultBotLogin);
        return current.Enabled
            && current.Generation != Guid.Empty
            && current.Revision == expected.Revision
            && current.Generation == expected.Generation
            && current.Amount == expected.Amount
            && current.Login == expected.Login
            && current.UserId == expected.UserId
            && (current.Features & HostFeatureFlags.Points) == HostFeatureFlags.Points
            && current.RuntimeState == BotChannelRuntimeState.Started
            && account == expected.Account(runtime.DefaultBotLogin)
            && account.Login == observation.BotLogin
            && (!account.Custom || account.UserId == observation.BotUserId)
            && runtime.IsCurrent(observation);
    }

    private static Task<bool> HasWatchTimeKeyAsync(
        BlokeBotDbContext db,
        int hostId,
        string key,
        CancellationToken ct
    ) =>
        db
            .PointLedgerEntries.AsNoTracking()
            .AnyAsync(value => value.HostId == hostId && value.OperationKey == key, ct);

    private async Task<WatchTimeCreditOutcome> ResolveWatchTimeKeyAsync(
        int hostId,
        string key,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await HasWatchTimeKeyAsync(db, hostId, key, ct)
            ? new WatchTimeCreditOutcome.AlreadyCredited()
            : new WatchTimeCreditOutcome.Uncertain();
    }
}

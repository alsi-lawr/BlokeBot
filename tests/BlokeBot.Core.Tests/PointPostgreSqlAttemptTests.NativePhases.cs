using System.Data.Common;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class PointPostgreSqlAttemptTests
{
    [Test, Explicit]
    public async Task PostgreSql_DrawKnownAbortReclaimsJoinedCohortWithoutResampling()
    {
        var competing = new CompetingCredit();
        var joined = new AfterRollback();
        await using var database = await PointProviderFixture.PostgreSqlAsync(competing, joined);
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        int giveawayId;
        await using (var seed = database.CreateDbContext())
        {
            var giveaway = new PointsGiveaway
            {
                HostId = hostId,
                StartedAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddMinutes(5),
                MinimumPayout = "10",
                MaximumPayout = "20",
                WinnerCount = 2,
                Entrants = [new() { Login = "viewer", JoinedAtUtc = DateTime.UtcNow }],
            };
            _ = seed.PointsGiveaways.Add(giveaway);
            _ = await seed.SaveChangesAsync();
            giveawayId = giveaway.Id;
        }
        var joiningService = CreateGiveawayService(database, new RecordingGiveawayScheduler());
        joined.Join = async () =>
            _ = (
                await joiningService.JoinOutcomeAsync(
                    hostId,
                    "streamer",
                    "arrival",
                    new Dictionary<string, string>(),
                    default
                )
            ).ShouldBeOfType<PointsGiveawayJoinOutcome.Joined>();
        var random = new RecordingRandom();
        competing.Arm(database, hostId);
        var result = (
            await new PointsGiveawayDrawService(
                database,
                new(database),
                random,
                []
            ).DrawOutcomeAsync(giveawayId, default)
        ).ShouldBeOfType<PointsGiveawayDrawOutcome.Winners>();
        result.Payouts.Select(value => value.Login).Order().ShouldBe(["arrival", "viewer"]);
        random.Ranks.ShouldBe(2);
        random.Payouts.ShouldBe(2);
        competing.OuterSnapshotReads.ShouldBe(3);
        joined.Joins.ShouldBe(1);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync(value => value.Login == "viewer")).Amount.ShouldBe(
            "15"
        );
        (await verify.PointBalances.SingleAsync(value => value.Login == "arrival")).Amount.ShouldBe(
            "10"
        );
        (await verify.PointsGiveawayWinners.CountAsync()).ShouldBe(2);
        (
            await verify.PointLedgerEntries.CountAsync(value => value.GiveawayId == giveawayId)
        ).ShouldBe(2);
    }

    [Test]
    public async Task Sqlite_CancellationAfterAcquisitionRollsBackWithoutOwnerReplay()
    {
        using var cancellation = new CancellationTokenSource();
        var fault = new CancelAcquired(cancellation);
        await CancellationAsync(
            await PointProviderFixture.SqliteFileAsync(fault),
            fault,
            cancellation
        );
    }

    [Test, Explicit]
    public async Task PostgreSql_CancellationAfterAcquisitionRollsBackWithoutOwnerReplay()
    {
        using var cancellation = new CancellationTokenSource();
        var fault = new CancelAcquired(cancellation);
        await CancellationAsync(
            await PointProviderFixture.PostgreSqlAsync(fault),
            fault,
            cancellation
        );
    }

    private static async Task CancellationAsync(
        PointProviderFixture database,
        CancelAcquired fault,
        CancellationTokenSource cancellation
    )
    {
        await using var owned = database;
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        fault.Armed = true;
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            new PointBalanceService(database)
                .Add(hostId, "viewer", new(7), "streamer", "cancel acquired")
                .ExecuteAsync(cancellation.Token)
                .AsTask()
        );
        fault.AcquiredReads.ShouldBe(1);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("0");
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test, Explicit]
    public async Task PostgreSql_RealKnownAbortWithCancellationDoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var competing = new CompetingCredit();
        var cancel = new CancelKnownAbort(cancellation);
        await using var database = await PointProviderFixture.PostgreSqlAsync(competing, cancel);
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        competing.Arm(database, hostId);
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            new PointBalanceService(database)
                .Add(hostId, "viewer", new(7), "streamer", "cancel known abort")
                .ExecuteAsync(cancellation.Token)
                .AsTask()
        );
        cancel.Rollbacks.ShouldBe(1);
        cancellation.IsCancellationRequested.ShouldBeTrue();
        competing.OuterSnapshotReads.ShouldBe(1);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("5");
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Test, Explicit]
    public async Task PostgreSql_RealTerminalNativeFailureDoesNotRetryAcquiredAttempt()
    {
        var fault = new TerminalNativeFault();
        await using var database = await PointProviderFixture.PostgreSqlAsync(fault);
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        fault.Armed = true;
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            new PointBalanceService(database)
                .Add(hostId, "viewer", new(7), "streamer", "terminal native")
                .ExecuteAsync(default)
                .AsTask()
        );
        failure.SqlState.ShouldBe(PostgresErrorCodes.DivisionByZero);
        fault.AcquiredReads.ShouldBe(1);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("0");
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task Sqlite_UnknownCommitResultNeverReplaysOwner(bool nativeCommitCompleted)
    {
        var fault = new UnknownCommit(nativeCommitCompleted);
        await UnknownCommitAsync(
            await PointProviderFixture.SqliteFileAsync(fault),
            fault,
            nativeCommitCompleted
        );
    }

    [Test, Explicit, Arguments(false), Arguments(true)]
    public async Task PostgreSql_UnknownCommitResultNeverReplaysOwner(bool nativeCommitCompleted)
    {
        var fault = new UnknownCommit(nativeCommitCompleted);
        await UnknownCommitAsync(
            await PointProviderFixture.PostgreSqlAsync(fault),
            fault,
            nativeCommitCompleted
        );
    }

    private static async Task UnknownCommitAsync(
        PointProviderFixture database,
        UnknownCommit fault,
        bool nativeCommitCompleted
    )
    {
        await using var owned = database;
        var hostId = await database.SeedHostAsync();
        fault.Armed = true;
        _ = await Should.ThrowAsync<IOException>(() =>
            new PointBalanceService(database)
                .Add(hostId, "viewer", new(7), "streamer", "ambiguous native commit")
                .ExecuteAsync(default)
                .AsTask()
        );
        fault.CommitRequests.ShouldBe(1);
        await using var verify = database.CreateDbContext();
        ((await verify.PointBalances.SingleOrDefaultAsync())?.Amount ?? "0").ShouldBe(
            nativeCommitCompleted ? "7" : "0"
        );
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(nativeCommitCompleted ? 1 : 0);
    }

    private sealed class AfterRollback : DbTransactionInterceptor
    {
        internal Func<Task>? Join { get; set; }
        internal int Joins { get; private set; }

        public override async Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Join is { } join)
            {
                Join = null;
                Joins++;
                await join();
            }
        }
    }

    private sealed class CancelAcquired(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal int AcquiredReads { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && command.CommandText.Contains("bounty_pledges", StringComparison.Ordinal))
            {
                _ = eventData.Context!.Database.CurrentTransaction.ShouldNotBeNull();
                AcquiredReads++;
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancelKnownAbort(CancellationTokenSource cancellation)
        : DbTransactionInterceptor
    {
        internal int Rollbacks { get; private set; }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Rollbacks++;
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class TerminalNativeFault : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal int AcquiredReads { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && command.CommandText.Contains("bounty_pledges", StringComparison.Ordinal))
            {
                _ = eventData.Context!.Database.CurrentTransaction.ShouldNotBeNull();
                AcquiredReads++;
                await result.DisposeAsync();
                await using var terminal = command.Connection!.CreateCommand();
                terminal.Transaction = command.Transaction;
                terminal.CommandText = "SELECT 1 / 0";
                _ = await terminal.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class UnknownCommit(bool nativeCommitCompleted) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal int CommitRequests { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                CommitRequests++;
                if (nativeCommitCompleted)
                {
                    await transaction.CommitAsync(CancellationToken.None);
                }
                throw new IOException("Owned fixture loses the commit acknowledgement.");
            }
            return result;
        }
    }
}

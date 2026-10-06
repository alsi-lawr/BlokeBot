using System.Data.Common;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointPostgreSqlAttemptTests
{
    [Test, Explicit]
    public async Task PostgreSql_ActualSerializationAbortRestartsWholeManualCredit()
    {
        var interleaving = new CompetingCredit();
        await using var database = await PointProviderFixture.PostgreSqlAsync(interleaving);
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        interleaving.Arm(database, hostId);
        var result = await new PointBalanceService(database)
            .Add(hostId, "viewer", new(7), "streamer", "outer")
            .ExecuteAsync(default);
        result.Match(value => value.Balance.ToString(), _ => "failure").ShouldBe("12");
        interleaving.OuterSnapshotReads.ShouldBe(2);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("12");
        (
            await verify
                .PointLedgerEntries.OrderBy(value => value.Id)
                .Select(value => value.BalanceAfter)
                .ToArrayAsync()
        ).ShouldBe(["5", "12"]);
    }

    [Test, Explicit]
    public async Task PostgreSql_KnownAbortsStopAtTwentyWholeAttempts()
    {
        var interleaving = new CompetingCredit { FailuresRequested = 20 };
        await using var database = await PointProviderFixture.PostgreSqlAsync(interleaving);
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        interleaving.Arm(database, hostId);
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            new PointBalanceService(database)
                .Add(hostId, "viewer", new(7), "streamer", "never commits")
                .ExecuteAsync(default)
                .AsTask()
        );
        failure.SqlState.ShouldBe(PostgresErrorCodes.SerializationFailure);
        interleaving.OuterSnapshotReads.ShouldBe(20);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("100");
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(20);
        (await verify.PointLedgerEntries.Select(value => value.Delta).ToArrayAsync()).ShouldAllBe(
            value => value == "5"
        );
    }

    [Test, Explicit]
    public async Task PostgreSql_DrawAbortPreservesPreparedRandomAndPaysOnce()
    {
        var interleaving = new CompetingCredit();
        await using var database = await PointProviderFixture.PostgreSqlAsync(interleaving);
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
                Entrants = [new() { Login = "viewer", JoinedAtUtc = DateTime.UtcNow }],
            };
            _ = seed.PointsGiveaways.Add(giveaway);
            _ = await seed.SaveChangesAsync();
            giveawayId = giveaway.Id;
        }
        var random = new RecordingRandom();
        interleaving.Arm(database, hostId);
        var service = new PointsGiveawayDrawService(database, new(database), random, []);
        var outcome = (
            await service.DrawOutcomeAsync(giveawayId, default)
        ).ShouldBeOfType<PointsGiveawayDrawOutcome.Winners>();
        outcome.Payouts.Single().Payout.ToString().ShouldBe("10");
        random.Ranks.ShouldBe(1);
        random.Payouts.ShouldBe(1);
        interleaving.OuterSnapshotReads.ShouldBe(2);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("15");
        (await verify.PointsGiveaways.SingleAsync()).Status.ShouldBe(
            PointsGiveawayStatus.Completed
        );
        (await verify.PointsGiveawayWinners.CountAsync()).ShouldBe(1);
        (
            await verify.PointLedgerEntries.CountAsync(value => value.GiveawayId == giveawayId)
        ).ShouldBe(1);
    }

    [Test, Explicit]
    public async Task PostgreSql_PostCommitFailureDoesNotReplayManualCredit()
    {
        var failure = new FailAfterCommit();
        await using var database = await PointProviderFixture.PostgreSqlAsync(failure);
        var hostId = await database.SeedHostAsync();
        failure.Armed = true;
        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
            new PointBalanceService(database)
                .Add(hostId, "viewer", new(7), "streamer", "one")
                .ExecuteAsync(default)
                .AsTask()
        );
        failure.Commits.ShouldBe(1);
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("7");
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Test, Explicit]
    public async Task PostgreSql_RealLockTimeoutIsNarrowContentionAndRollbackLeavesRowUnchanged()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        var hostId = await database.SeedHostAsync();
        await SeedBalanceAsync(database, hostId);
        await using var first = database.CreateDbContext();
        await using var firstTx = await first.Database.BeginTransactionAsync();
        _ = await first.Database.ExecuteSqlRawAsync(
            "UPDATE point_balances SET \"Amount\"='1' WHERE \"Login\"='viewer'"
        );
        await using var second = database.CreateDbContext();
        await using var secondTx = await second.Database.BeginTransactionAsync();
        _ = await second.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '20ms'");
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            MainDatabaseStatements.ApplyPointDeltaAsync(
                second,
                new(hostId, "viewer"),
                CanonicalPointInteger.From(7),
                new(
                    CanonicalPointInteger.From(0),
                    CanonicalPointInteger.From(PointAmount.MaximumValue)
                ),
                DateTime.UtcNow,
                default
            )
        );
        failure.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);
        MainDatabaseFailureClassifier.IsContention(failure).ShouldBeTrue();
        await secondTx.RollbackAsync();
        await firstTx.RollbackAsync();
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("0");
    }

    private static async Task SeedBalanceAsync(PointProviderFixture database, int id)
    {
        await using var seed = database.CreateDbContext();
        _ = seed.PointBalances.Add(
            new()
            {
                HostId = id,
                Login = "viewer",
                Amount = "0",
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await seed.SaveChangesAsync();
    }

    private sealed class CompetingCredit : DbCommandInterceptor
    {
        private PointProviderFixture? _database;
        private int _hostId;
        private bool _competing;
        private int _completed;
        internal int FailuresRequested { get; init; } = 1;
        internal int OuterSnapshotReads { get; private set; }

        internal void Arm(PointProviderFixture database, int hostId)
        {
            _database = database;
            _hostId = hostId;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                _database is null
                || _competing
                || !command.CommandText.Contains("bounty_pledges", StringComparison.Ordinal)
            )
            {
                return result;
            }
            OuterSnapshotReads++;
            if (_completed >= FailuresRequested)
            {
                return result;
            }
            _completed++;
            _competing = true;
            try
            {
                var credit = await new PointBalanceService(_database)
                    .Add(_hostId, "viewer", new(5), "streamer", "interleaving")
                    .ExecuteAsync(cancellationToken);
                credit.Match(_ => true, _ => false).ShouldBeTrue();
            }
            finally
            {
                _competing = false;
            }
            return result;
        }
    }

    private sealed class RecordingRandom : IPointsRandom
    {
        internal int Ranks { get; private set; }
        internal int Payouts { get; private set; }

        public double NextDouble()
        {
            Ranks++;
            return .5;
        }

        public int Next(int minValue, int maxValue)
        {
            if (maxValue == int.MaxValue)
            {
                Ranks++;
            }
            else
            {
                Payouts++;
            }
            return minValue;
        }
    }

    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal int Commits { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                Commits++;
                throw new InvalidOperationException("fixture failure after actual commit");
            }
            return Task.CompletedTask;
        }
    }
}

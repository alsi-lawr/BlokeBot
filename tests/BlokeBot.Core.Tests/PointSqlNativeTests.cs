using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointSqlNativeTests
{
    [Test]
    public async Task Sqlite_CurrentRowArithmeticAndLedgerAreAtomic() =>
        await CurrentRowArithmeticAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_CurrentRowArithmeticAndLedgerAreAtomic() =>
        await CurrentRowArithmeticAsync(await PointProviderFixture.PostgreSqlAsync());

    [Test]
    public async Task Sqlite_ConcurrentManualCreditsDoNotLoseUpdates() =>
        await ConcurrentCreditsAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_ConcurrentManualCreditsDoNotLoseUpdates() =>
        await ConcurrentCreditsAsync(await PointProviderFixture.PostgreSqlAsync());

    [Test]
    public async Task Sqlite_SettingsUseCurrentRowGenerationsAndRollback() =>
        await SettingsAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_SettingsUseCurrentRowGenerationsAndRollback() =>
        await SettingsAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task CurrentRowArithmeticAsync(PointProviderFixture fixture)
    {
        await using var owned = fixture;
        var hostId = await fixture.SeedHostAsync();
        var target = new PointBalanceTarget(hostId, "viewer");
        var huge = BigInteger.Pow(10, 95) + 123;
        var now = new DateTime(2026, 10, 6, 20, 0, 0, DateTimeKind.Utc);
        await using (var db = fixture.CreateDbContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var first = (
                await MainDatabaseStatements.ApplyCreatingPointCreditAsync(
                    db,
                    target,
                    CanonicalPointInteger.From(huge),
                    CanonicalPointInteger.From(PointAmount.MaximumValue),
                    now,
                    default
                )
            ).ShouldBeOfType<PointDeltaSqlOutcome.Applied>();
            first.Before.ToBigInteger().ShouldBe(BigInteger.Zero);
            first.After.ToBigInteger().ShouldBe(huge);
            var second = (
                await MainDatabaseStatements.ApplyPointDeltaAsync(
                    db,
                    target,
                    CanonicalPointInteger.From(7),
                    new(
                        CanonicalPointInteger.From(0),
                        CanonicalPointInteger.From(PointAmount.MaximumValue)
                    ),
                    now,
                    default
                )
            ).ShouldBeOfType<PointDeltaSqlOutcome.Applied>();
            second.Before.ToBigInteger().ShouldBe(huge);
            second.After.ToBigInteger().ShouldBe(huge + 7);
            var entry = new WatchTimeLedgerWrite(
                target,
                CanonicalPointInteger.From(huge + 7),
                second.After,
                "watch-native-exact",
                now
            );
            _ = (
                await MainDatabaseStatements.TryAppendWatchTimeLedgerAsync(db, entry, default)
            ).ShouldBeOfType<WatchTimeLedgerInsertOutcome.Inserted>();
            _ = (
                await MainDatabaseStatements.TryAppendWatchTimeLedgerAsync(db, entry, default)
            ).ShouldBeOfType<WatchTimeLedgerInsertOutcome.ExistingKey>();
            await tx.CommitAsync();
        }
        await using (var db = fixture.CreateDbContext())
        {
            var persisted = await db.PointBalances.AsNoTracking().SingleAsync();
            persisted.Amount.ShouldBe((huge + 7).ToString(CultureInfo.InvariantCulture));
            (await db.PointLedgerEntries.AsNoTracking().SingleAsync()).BalanceAfter.ShouldBe(
                persisted.Amount
            );
            await using var tx = await db.Database.BeginTransactionAsync();
            _ = (
                await MainDatabaseStatements.ApplyPointDeltaAsync(
                    db,
                    target,
                    CanonicalPointInteger.From(-(huge + 8)),
                    new(
                        CanonicalPointInteger.From(huge + 8),
                        CanonicalPointInteger.From(PointAmount.MaximumValue)
                    ),
                    now,
                    default
                )
            ).ShouldBeOfType<PointDeltaSqlOutcome.Rejected>();
            _ = (
                await MainDatabaseStatements.ApplyCreatingPointCreditAsync(
                    db,
                    new(hostId, "too-large"),
                    CanonicalPointInteger.From(PointAmount.MaximumValue + 1),
                    CanonicalPointInteger.From(PointAmount.MaximumValue),
                    now,
                    default
                )
            ).ShouldBeOfType<PointDeltaSqlOutcome.Rejected>();
            var deleted = (
                await MainDatabaseStatements.DeletePointBalanceAsync(db, target, default)
            ).ShouldBeOfType<PointDeleteSqlOutcome.Deleted>();
            deleted.Before.ToBigInteger().ShouldBe(huge + 7);
            var recreated = (
                await MainDatabaseStatements.ApplyCreatingPointCreditAsync(
                    db,
                    target,
                    CanonicalPointInteger.From(5),
                    CanonicalPointInteger.From(PointAmount.MaximumValue),
                    now,
                    default
                )
            ).ShouldBeOfType<PointDeltaSqlOutcome.Applied>();
            recreated.Before.ToBigInteger().ShouldBe(BigInteger.Zero);
            await tx.RollbackAsync();
        }
        await using (var db = fixture.CreateDbContext())
        {
            (await db.PointBalances.SingleAsync()).Amount.ShouldBe(
                (huge + 7).ToString(CultureInfo.InvariantCulture)
            );
            (await db.PointLedgerEntries.CountAsync()).ShouldBe(1);
        }
    }

    private static async Task ConcurrentCreditsAsync(PointProviderFixture fixture)
    {
        await using var owned = fixture;
        var hostId = await fixture.SeedHostAsync();
        var service = new PointBalanceService(fixture);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable
            .Range(0, 12)
            .Select(async _ =>
            {
                await start.Task;
                var result = await service
                    .Add(hostId, "viewer", new(17), "streamer", "concurrent")
                    .ExecuteAsync(default);
                result.Match(_ => true, _ => false).ShouldBeTrue();
            })
            .ToArray();
        start.SetResult();
        await Task.WhenAll(attempts);
        await using var db = fixture.CreateDbContext();
        (await db.PointBalances.SingleAsync()).Amount.ShouldBe("204");
        var ledgers = await db.PointLedgerEntries.OrderBy(value => value.Id).ToArrayAsync();
        ledgers.Length.ShouldBe(12);
        ledgers
            .Select(value => BigInteger.Parse(value.BalanceAfter, CultureInfo.InvariantCulture))
            .Order()
            .ShouldBe(Enumerable.Range(1, 12).Select(value => new BigInteger(value * 17)));
    }

    private static async Task SettingsAsync(PointProviderFixture fixture)
    {
        await using var owned = fixture;
        var hostId = await fixture.SeedHostAsync();
        async Task<WatchTimeSettingsWriteResult> Save(bool enabled, string? amount)
        {
            await using var db = fixture.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await MainDatabaseStatements.ApplyWatchTimeSettingsAsync(
                db,
                hostId,
                enabled,
                amount,
                Guid.NewGuid(),
                Guid.NewGuid(),
                default
            );
            await tx.CommitAsync();
            return result;
        }
        var initial = await Save(false, null);
        initial.EnableGeneration.ShouldBe(Guid.Empty);
        var enabled = await Save(true, "1");
        enabled.EnableGeneration.ShouldNotBe(Guid.Empty);
        var same = await Save(true, "1");
        same.ShouldBe(enabled);
        var edited = await Save(true, "999999999999999999999999999999");
        edited.EnableGeneration.ShouldBe(enabled.EnableGeneration);
        edited.Revision.ShouldNotBe(enabled.Revision);
        await using (var db = fixture.CreateDbContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            _ = await MainDatabaseStatements.ApplyWatchTimeSettingsAsync(
                db,
                hostId,
                false,
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                default
            );
            await tx.RollbackAsync();
        }
        (await Save(true, edited.Amount)).ShouldBe(edited);
        var disabled = await Save(false, null);
        disabled.EnableGeneration.ShouldBe(Guid.Empty);
        disabled.Amount.ShouldBeNull();
        (await Save(true, "1")).EnableGeneration.ShouldNotBe(enabled.EnableGeneration);
    }
}

internal sealed class PointProviderFixture(
    DbContextOptions<BlokeBotDbContext> options,
    IAsyncDisposable? sqlite,
    string? postgresDatabase,
    string? postgresAdmin
) : IDbContextFactory<BlokeBotDbContext>, IAsyncDisposable
{
    internal static async Task<PointProviderFixture> SqliteAsync()
    {
        var fixture = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = fixture.CreateDbContext();
        return new(
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(db.Database.GetConnectionString()!)
                .Options,
            fixture,
            null,
            null
        );
    }

    internal static async Task<PointProviderFixture> SqliteFileAsync(
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors
    )
    {
        var directory = Directory.CreateTempSubdirectory("b322-point-file-");
        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory.FullName, "points.db"),
            DefaultTimeout = 30,
            Pooling = false,
        };
        var fixture = new PointProviderFixture(
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(connection.ConnectionString)
                .AddInterceptors(interceptors)
                .Options,
            new FileDatabaseLease(directory),
            null,
            null
        );
        await using var db = fixture.CreateDbContext();
        _ = await db.Database.EnsureCreatedAsync();
        return fixture;
    }

    private sealed class FileDatabaseLease(DirectoryInfo directory) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            directory.Delete(recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    internal static async Task<PointProviderFixture> PostgreSqlAsync(
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors
    )
    {
        var connection =
            Environment.GetEnvironmentVariable("BLOKEBOT_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException(
                "Supply the explicit isolated PostgreSql fixture connection."
            );
        var database = $"b322_test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(connection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            _ = await create.ExecuteNonQueryAsync();
        }
        var builder = new NpgsqlConnectionStringBuilder(connection)
        {
            Database = database,
            Pooling = false,
        };
        var fixture = new PointProviderFixture(
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseNpgsql(builder.ConnectionString)
                .AddInterceptors(interceptors)
                .Options,
            null,
            database,
            connection
        );
        await using var db = fixture.CreateDbContext();
        _ = await db.Database.EnsureCreatedAsync();
        return fixture;
    }

    public BlokeBotDbContext CreateDbContext() => new(options);

    public ValueTask<BlokeBotDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default
    ) =>
        cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<BlokeBotDbContext>(cancellationToken)
            : ValueTask.FromResult(CreateDbContext());

    internal async Task<int> SeedHostAsync()
    {
        await using var db = CreateDbContext();
        var host = new BotHost
        {
            Login = "streamer",
            TwitchUserId = "100",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Points,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointsSettings.Add(new PointsSettings { HostId = host.Id });
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    public async ValueTask DisposeAsync()
    {
        if (sqlite is not null)
        {
            await sqlite.DisposeAsync();
        }
        if (postgresDatabase is not null)
        {
            await using var admin = new NpgsqlConnection(postgresAdmin);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE \"{postgresDatabase}\"",
                admin
            );
            _ = await drop.ExecuteNonQueryAsync();
        }
    }
}

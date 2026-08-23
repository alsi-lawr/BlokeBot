using BlokeBot.Persistence;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginLifecyclePersistenceTests
{
    [Test]
    public async Task LifecycleStore_RejectsStaleCheckpointWithoutOverwritingWinner()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var begun = (
            await store.BeginActivationAsync(
                new(package.Installation, PluginLifecycleOperationId.New(), DateTimeOffset.UtcNow),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;
        var winner = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.PreparationSucceeded(begun, DateTimeOffset.UtcNow)
        ).State;
        var stale = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.PreparationFailed(
                    begun,
                    PluginLifecycleFailureCode.PreparationFailed,
                    null,
                    DateTimeOffset.UtcNow
                )
        ).State;

        _ = (
            await store.WriteAsync(begun, winner, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Written>();
        var conflict = (
            await store.WriteAsync(begun, stale, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Conflict>();

        conflict.Current.ShouldBe(winner);
        (await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)).ShouldBe(
            winner
        );
    }

    [Test]
    public async Task LifecycleMigration_RoundTripsGenerationAndLatestRedactedOutcome()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("2.0.0", "v2");
        var begun = (
            await store.BeginActivationAsync(
                new(package.Installation, PluginLifecycleOperationId.New(), DateTimeOffset.UtcNow),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;
        var migrating = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.PreparationSucceeded(begun, DateTimeOffset.UtcNow)
        ).State;
        _ = (
            await store.WriteAsync(begun, migrating, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Written>();
        PluginLifecycleSafeDetail
            .TryCreate("Selected migration failed safely.", out var detail)
            .ShouldBeTrue();
        var faulted = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.Fault(
                    migrating,
                    PluginLifecyclePhase.Migrating,
                    PluginLifecycleFailureCode.MigrationFailed,
                    detail,
                    DateTimeOffset.UtcNow
                )
        ).State;
        _ = (
            await store.WriteAsync(migrating, faulted, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Written>();

        var reloaded = await new EfPluginLifecycleStore(database).LoadAsync(
            package.Installation.PluginId,
            CancellationToken.None
        );

        reloaded.ShouldBe(faulted);
        reloaded!.SelectedGeneration.Value.ShouldBe(1UL);
        reloaded.LatestOutcome.Detail!.Value.ShouldBe("Selected migration failed safely.");
    }

    private sealed class LifecycleDatabase(string path, DbContextOptions<BlokeBotDbContext> options)
        : IDbContextFactory<BlokeBotDbContext>,
            IAsyncDisposable
    {
        internal static async ValueTask<LifecycleDatabase> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-lifecycle-{Guid.NewGuid():N}.db"
            );
            var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite($"Data Source={path}")
                .AddInterceptors(new WeeklyAnnouncementMigrationInterceptor())
                .Options;
            var database = new LifecycleDatabase(path, options);
            await using var db = database.CreateDbContext();
            await db.Database.MigrateAsync();
            return database;
        }

        public BlokeBotDbContext CreateDbContext() => new(options);

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete($"{path}-shm");
            File.Delete($"{path}-wal");
            return ValueTask.CompletedTask;
        }
    }
}

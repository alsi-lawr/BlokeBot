using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task LifecycleStore_RejectsStalePurgeWithoutDeletingWinner()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var purging = await AdvanceToPurgingAsync(store, package);
        var winner = Applied(
            PluginLifecycleStateMachine.Fault(
                purging,
                PluginLifecyclePhase.Purging,
                PluginLifecycleFailureCode.PurgeFailed,
                null,
                DateTimeOffset.UtcNow
            )
        );
        await WriteAsync(store, purging, winner);
        var staleOutcome = PluginLifecycleOutcome.Progress(
            PluginLifecycleOutcomeCode.Purged,
            DateTimeOffset.UtcNow
        );

        var conflict = (
            await store.CompletePurgeAsync(purging, staleOutcome, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStorePurgeOutcome.Conflict>();

        conflict.Current.ShouldBe(winner);
        (await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)).ShouldBe(
            winner
        );
        (
            await store.LoadTombstoneAsync(package.Installation.PluginId, CancellationToken.None)
        ).ShouldBeNull();
    }

    [Test]
    public async Task LifecycleDatabase_RejectsHistoricalPurgedLifecycleRow()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var purging = await AdvanceToPurgingAsync(store, package);
        await using (var db = database.CreateDbContext())
        {
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"Phase\" = 'Purged';"
                )
            );
        }

        (await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)).ShouldBe(
            purging
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

    [Test]
    public async Task LifecycleMigration_PurgeRetainsOnlyLatestOutcomeAndAllowsReinstall()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var harness = new LifecycleHarness();
        var firstPackage = harness.Package("1.0.0", "v1");
        var purging = await AdvanceToPurgingAsync(store, firstPackage);
        var purged = PluginLifecycleOutcome.Progress(
            PluginLifecycleOutcomeCode.Purged,
            new DateTimeOffset(2026, 8, 23, 16, 0, 0, TimeSpan.Zero)
        );

        var completed = await store.CompletePurgeAsync(purging, purged, CancellationToken.None);
        var repeated = await store.CompletePurgeAsync(purging, purged, CancellationToken.None);

        var tombstone = completed
            .ShouldBeOfType<PluginLifecycleStorePurgeOutcome.Completed>()
            .Tombstone;
        repeated
            .ShouldBeOfType<PluginLifecycleStorePurgeOutcome.Completed>()
            .Tombstone.ShouldBe(tombstone);
        (
            await store.LoadAsync(firstPackage.Installation.PluginId, CancellationToken.None)
        ).ShouldBeNull();
        (
            await store.LoadTombstoneAsync(
                firstPackage.Installation.PluginId,
                CancellationToken.None
            )
        ).ShouldBe(tombstone);

        await using (var connection = await database.OpenConnectionAsync())
        {
            (await ReadOutcomeColumnsAsync(connection))
                .Order(StringComparer.Ordinal)
                .ToArray()
                .ShouldBe([
                    "FailureCode",
                    "OutcomeCode",
                    "OutcomeDetail",
                    "OutcomeOccurredAtUtc",
                    "PluginId",
                ]);
            await AssertRetainedOutcomeAsync(connection, tombstone);
        }

        var reinstalled = (
            await store.BeginActivationAsync(
                new(
                    harness.Package("2.0.0", "v2").Installation,
                    PluginLifecycleOperationId.New(),
                    DateTimeOffset.UtcNow
                ),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;

        reinstalled.SelectedGeneration.Value.ShouldBe(1UL);
        (
            await store.LoadTombstoneAsync(
                firstPackage.Installation.PluginId,
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task LifecycleMigration_ConvertsExistingPurgedStateToOutcomeTombstone()
    {
        await using var database = await LifecycleDatabase.CreateAsync(
            "20260823152250_v0.13.0_PluginLifecycles"
        );
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var occurredAt = new DateTimeOffset(2026, 8, 23, 15, 59, 0, TimeSpan.Zero);
        await using (var db = database.CreateDbContext())
        {
            _ = db.PluginLifecycles.Add(
                new PluginLifecycleRecord
                {
                    PluginId = package.Installation.PluginId.Value,
                    SelectedVersion = package.Installation.Release.DeclaredVersion.Value,
                    SelectedTag = package.Installation.Release.Tag.Value,
                    OperationId = PluginLifecycleOperationId.New().Value,
                    SelectedGeneration = 7,
                    Phase = PluginLifecyclePhase.Removed,
                    OperationKind = PluginLifecycleOperationKind.Purge,
                    OutcomeCode = PluginLifecycleOutcomeCode.Purged,
                    OutcomeOccurredAtUtc = occurredAt.UtcDateTime,
                    Revision = 8,
                    UpdatedAtUtc = occurredAt.UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync();
            _ = await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"plugin_lifecycles\" SET \"Phase\" = 'Purged';"
            );
        }

        await database.MigrateToLatestAsync();

        var store = new EfPluginLifecycleStore(database);
        (
            await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)
        ).ShouldBeNull();
        var tombstone = (
            await store.LoadTombstoneAsync(package.Installation.PluginId, CancellationToken.None)
        )!;
        tombstone.PluginId.ShouldBe(package.Installation.PluginId);
        tombstone.LatestOutcome.ShouldBe(
            new PluginLifecycleOutcome(PluginLifecycleOutcomeCode.Purged, null, null, occurredAt)
        );
    }

    [Test]
    public async Task LifecycleMigration_ConstrainsAndRoundTripsFaultShutdownCheckpoint()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var active = await AdvanceToActiveAsync(store, package);
        var intent = Applied(
            PluginLifecycleStateMachine.BeginFaultShutdown(
                active,
                PluginLifecycleFailureCode.GenerationExhausted,
                null,
                DateTimeOffset.UtcNow
            )
        );
        var missingSource = intent with { FaultedFrom = null };
        var invalidNonFault = active with
        {
            FaultedFrom = PluginLifecyclePhase.Active,
            Revision = active.Revision + 1,
        };

        _ = (
            await store.WriteAsync(active, missingSource, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Conflict>();
        _ = (
            await store.WriteAsync(active, invalidNonFault, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Conflict>();
        await WriteAsync(store, active, intent);
        var mismatchedSource = intent with
        {
            FaultedFrom = PluginLifecyclePhase.Migrating,
            Revision = intent.Revision + 1,
        };
        var otherInstallation = new LifecycleHarness().Package("2.0.0", "v2").Installation;
        var mismatchedInstallation = intent with
        {
            ActiveRuntime = new(otherInstallation, intent.SelectedFence),
            Revision = intent.Revision + 1,
        };
        PluginWorkerGeneration
            .TryCreate(intent.SelectedGeneration.Value + 1, out var mismatchedGeneration)
            .ShouldBeTrue();
        var mismatchedFence = intent with
        {
            ActiveRuntime = new(
                intent.SelectedInstallation,
                new(intent.OperationId, mismatchedGeneration)
            ),
            Revision = intent.Revision + 1,
        };

        _ = (
            await store.WriteAsync(intent, mismatchedSource, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Conflict>();
        _ = (
            await store.WriteAsync(intent, mismatchedInstallation, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Conflict>();
        _ = (
            await store.WriteAsync(intent, mismatchedFence, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Conflict>();
        (await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)).ShouldBe(
            intent
        );
        await using (var db = database.CreateDbContext())
        {
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"FaultedFrom\" = NULL;"
                )
            );
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"ActiveVersion\" = '2.0.0';"
                )
            );
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"ActiveGeneration\" = \"SelectedGeneration\" + 1;"
                )
            );
        }

        PluginLifecycleSafeDetail
            .TryCreate("The plugin worker could not be terminated cleanly.", out var detail)
            .ShouldBeTrue();
        var completed = Applied(
            PluginLifecycleStateMachine.CompleteFaultShutdown(
                intent,
                PluginLifecycleFailureCode.WorkerDisposalFailed,
                detail,
                DateTimeOffset.UtcNow
            )
        );
        await WriteAsync(store, intent, completed);

        var reloaded = (
            await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)
        )!;
        reloaded.ActiveRuntime.ShouldBeNull();
        reloaded.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.WorkerDisposalFailed
        );
        reloaded.LatestOutcome.Detail.ShouldBe(detail);
        _ = PluginLifecycleStateMachine
            .BeginRestart(reloaded, PluginLifecycleOperationId.New(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<PluginLifecycleTransitionOutcome.Applied>();
    }

    [Test]
    public async Task LifecycleMigration_NormalizesParentLegalRemoveFromFaultRows()
    {
        await using var database = await LifecycleDatabase.CreateAsync(
            "20260823180232_v0.13.0_PluginLifecycleFaultShutdown"
        );
        var harness = new LifecycleHarness();
        var removingId = harness.PackageFor("fault-removing", "1.0.0", "v1").Installation.PluginId;
        var removedId = harness.PackageFor("fault-removed", "1.0.0", "v1").Installation.PluginId;
        var purgingId = harness.PackageFor("fault-purging", "1.0.0", "v1").Installation.PluginId;
        var now = new DateTimeOffset(2026, 8, 23, 18, 30, 0, TimeSpan.Zero);
        await using (var db = database.CreateDbContext())
        {
            db.PluginLifecycles.AddRange(
                ParentLegalRemoveFromFaultRecord(
                    removingId,
                    PluginLifecyclePhase.Removing,
                    PluginLifecycleOperationKind.Remove,
                    now
                ),
                ParentLegalRemoveFromFaultRecord(
                    removedId,
                    PluginLifecyclePhase.Removed,
                    PluginLifecycleOperationKind.Remove,
                    now
                ),
                ParentLegalRemoveFromFaultRecord(
                    purgingId,
                    PluginLifecyclePhase.Purging,
                    PluginLifecycleOperationKind.Purge,
                    now
                )
            );
            _ = await db.SaveChangesAsync();
        }

        await database.MigrateToLatestAsync();

        var store = new EfPluginLifecycleStore(database);
        var normalized = (await store.LoadAllAsync(CancellationToken.None)).ToDictionary(state =>
            state.PluginId
        );
        normalized.Keys.ShouldBe([removingId, removedId, purgingId], ignoreOrder: true);
        foreach (var state in normalized.Values)
        {
            state.FaultedFrom.ShouldBeNull();
            PluginLifecycleStateMachine.HasValidFaultInvariant(state).ShouldBeTrue();
        }

        var coordinator = new PluginLifecycleCoordinator(
            store,
            new FakePackageResolver(),
            [new RecordingMigrationOwner()],
            [new RecordingPurgeOwner()],
            new RecordingPendingWorkCanceller(),
            new FakeLifecycleWorkers(),
            new PluginRuntimeSnapshotRegistry(),
            new PluginLifecycleSerialization(),
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );

        await coordinator.RecoverAsync(CancellationToken.None);

        (await store.LoadAsync(removingId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Removed
        );
        (await store.LoadAsync(removedId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Removed
        );
        (await store.LoadAsync(purgingId, CancellationToken.None)).ShouldBeNull();
        _ = (await store.LoadTombstoneAsync(purgingId, CancellationToken.None)).ShouldNotBeNull();
        await using (var db = database.CreateDbContext())
        {
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"FaultedFrom\" = 'Active' "
                        + "WHERE \"PluginId\" = 'fault-removing';"
                )
            );
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"Phase\" = 'Faulted' "
                        + "WHERE \"PluginId\" = 'fault-removed';"
                )
            );
        }
    }

    private static async ValueTask<PluginLifecycleState> AdvanceToPurgingAsync(
        IPluginLifecycleStore store,
        PluginLifecyclePackage package
    )
    {
        var active = await AdvanceToActiveAsync(store, package);
        var draining = Applied(
            PluginLifecycleStateMachine.BeginRemoval(
                active,
                PluginLifecycleOperationId.New(),
                purge: true,
                DateTimeOffset.UtcNow
            )
        );
        await WriteAsync(store, active, draining);
        var purging = Applied(
            PluginLifecycleStateMachine.DrainSucceeded(draining, DateTimeOffset.UtcNow)
        );
        await WriteAsync(store, draining, purging);
        return purging;
    }

    private static async ValueTask<PluginLifecycleState> AdvanceToActiveAsync(
        IPluginLifecycleStore store,
        PluginLifecyclePackage package
    )
    {
        var begun = (
            await store.BeginActivationAsync(
                new(package.Installation, PluginLifecycleOperationId.New(), DateTimeOffset.UtcNow),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;
        var migrating = Applied(
            PluginLifecycleStateMachine.PreparationSucceeded(begun, DateTimeOffset.UtcNow)
        );
        await WriteAsync(store, begun, migrating);
        var activating = Applied(
            PluginLifecycleStateMachine.MigrationSucceeded(migrating, DateTimeOffset.UtcNow)
        );
        await WriteAsync(store, migrating, activating);
        var active = Applied(
            PluginLifecycleStateMachine.ActivationSucceeded(activating, DateTimeOffset.UtcNow)
        );
        await WriteAsync(store, activating, active);
        return active;
    }

    private static PluginLifecycleRecord ParentLegalRemoveFromFaultRecord(
        PluginId pluginId,
        PluginLifecyclePhase phase,
        PluginLifecycleOperationKind operationKind,
        DateTimeOffset now
    ) =>
        new()
        {
            PluginId = pluginId.Value,
            SelectedVersion = "1.0.0",
            SelectedTag = "v1",
            OperationId = PluginLifecycleOperationId.New().Value,
            SelectedGeneration = 1,
            Phase = phase,
            OperationKind = operationKind,
            FaultedFrom = PluginLifecyclePhase.Active,
            OutcomeCode = PluginLifecycleOutcomeCode.Faulted,
            FailureCode = PluginLifecycleFailureCode.WorkerExited,
            OutcomeOccurredAtUtc = now.UtcDateTime,
            Revision = 5,
            UpdatedAtUtc = now.UtcDateTime,
        };

    private static async ValueTask WriteAsync(
        IPluginLifecycleStore store,
        PluginLifecycleState expected,
        PluginLifecycleState next
    ) =>
        _ = (
            await store.WriteAsync(expected, next, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Written>();

    private static PluginLifecycleState Applied(PluginLifecycleTransitionOutcome transition) =>
        transition.ShouldBeOfType<PluginLifecycleTransitionOutcome.Applied>().State;

    private static async ValueTask<IReadOnlyList<string>> ReadOutcomeColumnsAsync(
        SqliteConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('plugin_lifecycle_outcomes');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return columns;
    }

    private static async ValueTask AssertRetainedOutcomeAsync(
        SqliteConnection connection,
        PluginLifecycleTombstone expected
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"PluginId\", \"OutcomeCode\", \"FailureCode\", \"OutcomeDetail\" "
            + "FROM \"plugin_lifecycle_outcomes\";";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe(expected.PluginId.Value);
        reader.GetString(1).ShouldBe(nameof(PluginLifecycleOutcomeCode.Purged));
        reader.IsDBNull(2).ShouldBeTrue();
        reader.IsDBNull(3).ShouldBeTrue();
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    private sealed class LifecycleDatabase(string path, DbContextOptions<BlokeBotDbContext> options)
        : IDbContextFactory<BlokeBotDbContext>,
            IAsyncDisposable
    {
        internal static async ValueTask<LifecycleDatabase> CreateAsync(
            string? targetMigration = null
        )
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
            if (targetMigration is null)
            {
                await db.Database.MigrateAsync();
            }
            else
            {
                await db.Database.MigrateAsync(targetMigration);
            }

            return database;
        }

        public BlokeBotDbContext CreateDbContext() => new(options);

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(CreateDbContext());

        internal async ValueTask<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            return connection;
        }

        internal async ValueTask MigrateToLatestAsync()
        {
            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete($"{path}-shm");
            File.Delete($"{path}-wal");
            return ValueTask.CompletedTask;
        }
    }
}

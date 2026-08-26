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
    public async Task LifecycleStore_PersistsSameIdentityReplacementAsFreshSelection()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var harness = new LifecycleHarness();
        var activePackage = harness.Package("1.0.0", "v1");
        var replacementPackage = harness.Package("1.0.0", "v1");
        var active = await AdvanceToActiveAsync(store, activePackage);
        var operationId = PluginLifecycleOperationId.New();

        var replacement = (
            await store.BeginReplacementAsync(
                new(
                    replacementPackage.Installation,
                    replacementPackage.PackageOperationId,
                    operationId,
                    DateTimeOffset.UtcNow
                ),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;

        replacement.SelectedInstallation.ShouldBe(active.SelectedInstallation);
        replacement.SelectedPackageOperationId.ShouldBe(replacementPackage.PackageOperationId);
        replacement.SelectedPackageOperationId.ShouldNotBe(active.SelectedPackageOperationId);
        replacement.OperationId.ShouldBe(operationId);
        replacement.SelectedGeneration.Value.ShouldBe(active.SelectedGeneration.Value + 1);
        replacement.OperationKind.ShouldBe(PluginLifecycleOperationKind.Replace);
        replacement.ActiveRuntime.ShouldBe(active.ActiveRuntime);
        (
            await store.LoadAsync(replacementPackage.Installation.PluginId, CancellationToken.None)
        ).ShouldBe(replacement);
    }

    [Test]
    public async Task LifecycleMigration_BackfillsExactPackageOperationFromExistingFence()
    {
        await using var database = await LifecycleDatabase.CreateAsync(
            "20260826143119_v0.13.0_PluginLifecycleReplacement"
        );
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var operationId = PluginLifecycleOperationId.New();
        var now = new DateTimeOffset(2026, 8, 26, 16, 30, 0, TimeSpan.Zero);
        await using (var db = database.CreateDbContext())
        {
            _ = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "plugin_lifecycles" (
                    "PluginId", "SelectedVersion", "SelectedTag", "OperationId",
                    "SelectedGeneration", "ActiveVersion", "ActiveTag", "ActiveOperationId",
                    "ActiveGeneration", "Phase", "OperationKind", "FaultedFrom",
                    "AutomaticRestartConsumed", "RestartNotBeforeUtc", "OutcomeCode",
                    "FailureCode", "OutcomeDetail", "OutcomeOccurredAtUtc", "Revision",
                    "UpdatedAtUtc"
                ) VALUES (
                    {package.Installation.PluginId.Value},
                    {package.Installation.Release.DeclaredVersion.Value},
                    {package.Installation.Release.Tag.Value},
                    {operationId.Value}, 1,
                    {package.Installation.Release.DeclaredVersion.Value},
                    {package.Installation.Release.Tag.Value},
                    {operationId.Value}, 1, 'Active', 'Activate', NULL, 0, NULL,
                    'Activated', NULL, NULL, {now.UtcDateTime}, 1, {now.UtcDateTime}
                );
                """
            );
        }

        await database.MigrateToLatestAsync();

        var migrated = (
            await new EfPluginLifecycleStore(database).LoadAsync(
                package.Installation.PluginId,
                CancellationToken.None
            )
        ).ShouldNotBeNull();
        migrated.SelectedPackageOperationId.Value.ShouldBe(operationId.Value);
        migrated.ActiveRuntime!.PackageOperationId.ShouldBe(migrated.SelectedPackageOperationId);
    }

    [Test]
    public async Task LifecycleStore_RejectsStaleCheckpointWithoutOverwritingWinner()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var begun = (
            await store.BeginActivationAsync(
                new(
                    package.Installation,
                    package.PackageOperationId,
                    PluginLifecycleOperationId.New(),
                    DateTimeOffset.UtcNow
                ),
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
    public async Task LifecycleStore_RejectsStaleRemovalWithoutDeletingWinner()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var removing = await AdvanceToRemovingAsync(store, package);
        var winner = Applied(
            PluginLifecycleStateMachine.Fault(
                removing,
                PluginLifecyclePhase.Removing,
                PluginLifecycleFailureCode.RemovalFailed,
                null,
                DateTimeOffset.UtcNow
            )
        );
        await WriteAsync(store, removing, winner);
        var staleOutcome = PluginLifecycleOutcome.Progress(
            PluginLifecycleOutcomeCode.Removed,
            DateTimeOffset.UtcNow
        );

        var conflict = (
            await store.CompleteRemovalAsync(removing, staleOutcome, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreRemovalOutcome.Conflict>();

        conflict.Current.ShouldBe(winner);
        (await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)).ShouldBe(
            winner
        );
    }

    [Test]
    public async Task LifecycleDatabase_RejectsHistoricalPurgedLifecycleRow()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var removing = await AdvanceToRemovingAsync(store, package);
        await using (var db = database.CreateDbContext())
        {
            _ = await Should.ThrowAsync<SqliteException>(async () =>
                _ = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"plugin_lifecycles\" SET \"Phase\" = 'Purged';"
                )
            );
        }

        (await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)).ShouldBe(
            removing
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
                new(
                    package.Installation,
                    package.PackageOperationId,
                    PluginLifecycleOperationId.New(),
                    DateTimeOffset.UtcNow
                ),
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
    public async Task RemovalCompletion_IsIdempotentAndAllowsFreshReinstall()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var store = new EfPluginLifecycleStore(database);
        var harness = new LifecycleHarness();
        var firstPackage = harness.Package("1.0.0", "v1");
        var removing = await AdvanceToRemovingAsync(store, firstPackage);
        var removed = PluginLifecycleOutcome.Progress(
            PluginLifecycleOutcomeCode.Removed,
            new DateTimeOffset(2026, 8, 23, 16, 0, 0, TimeSpan.Zero)
        );

        var completed = await store.CompleteRemovalAsync(removing, removed, CancellationToken.None);
        var repeated = await store.CompleteRemovalAsync(removing, removed, CancellationToken.None);

        completed
            .ShouldBeOfType<PluginLifecycleStoreRemovalOutcome.Completed>()
            .PluginId.ShouldBe(firstPackage.Installation.PluginId);
        repeated
            .ShouldBeOfType<PluginLifecycleStoreRemovalOutcome.Completed>()
            .PluginId.ShouldBe(firstPackage.Installation.PluginId);
        (
            await store.LoadAsync(firstPackage.Installation.PluginId, CancellationToken.None)
        ).ShouldBeNull();
        var reinstallPackage = harness.Package("2.0.0", "v2");
        var reinstalled = (
            await store.BeginActivationAsync(
                new(
                    reinstallPackage.Installation,
                    reinstallPackage.PackageOperationId,
                    PluginLifecycleOperationId.New(),
                    DateTimeOffset.UtcNow
                ),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;

        reinstalled.SelectedGeneration.Value.ShouldBe(1UL);
    }

    [Test]
    public async Task LifecycleMigration_ConvertsLegacyPurgeCheckpointToDestructiveRemoval()
    {
        await using var database = await LifecycleDatabase.CreateAsync(
            "20260823152250_v0.13.0_PluginLifecycles"
        );
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var occurredAt = new DateTimeOffset(2026, 8, 23, 15, 59, 0, TimeSpan.Zero);
        await using (var db = database.CreateDbContext())
        {
            await InsertLegacyLifecycleRecordAsync(
                db,
                new PluginLifecycleRecord
                {
                    PluginId = package.Installation.PluginId.Value,
                    SelectedVersion = package.Installation.Release.DeclaredVersion.Value,
                    SelectedTag = package.Installation.Release.Tag.Value,
                    OperationId = PluginLifecycleOperationId.New().Value,
                    SelectedGeneration = 7,
                    Phase = PluginLifecyclePhase.Removed,
                    OperationKind = PluginLifecycleOperationKind.Remove,
                    OutcomeCode = PluginLifecycleOutcomeCode.Removed,
                    OutcomeOccurredAtUtc = occurredAt.UtcDateTime,
                    Revision = 8,
                    UpdatedAtUtc = occurredAt.UtcDateTime,
                }
            );
            _ = await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"plugin_lifecycles\" SET \"Phase\" = 'Purging', "
                    + "\"OperationKind\" = 'Purge', \"OutcomeCode\" = 'Purged';"
            );
        }

        await database.MigrateToLatestAsync();

        var store = new EfPluginLifecycleStore(database);
        var migrated = (
            await store.LoadAsync(package.Installation.PluginId, CancellationToken.None)
        ).ShouldNotBeNull();
        migrated.Phase.ShouldBe(PluginLifecyclePhase.Removing);
        migrated.OperationKind.ShouldBe(PluginLifecycleOperationKind.Remove);
        migrated.LatestOutcome.Code.ShouldBe(PluginLifecycleOutcomeCode.Removed);
        migrated.LatestOutcome.OccurredAtUtc.ShouldBe(occurredAt);
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
            ActiveRuntime = new(
                otherInstallation,
                intent.SelectedFence,
                intent.SelectedPackageOperationId
            ),
            Revision = intent.Revision + 1,
        };
        PluginWorkerGeneration
            .TryCreate(intent.SelectedGeneration.Value + 1, out var mismatchedGeneration)
            .ShouldBeTrue();
        var mismatchedFence = intent with
        {
            ActiveRuntime = new(
                intent.SelectedInstallation,
                new(intent.OperationId, mismatchedGeneration),
                intent.SelectedPackageOperationId
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
        var legacyPurgeId = harness
            .PackageFor("fault-purging", "1.0.0", "v1")
            .Installation.PluginId;
        var now = new DateTimeOffset(2026, 8, 23, 18, 30, 0, TimeSpan.Zero);
        await using (var db = database.CreateDbContext())
        {
            foreach (
                var record in new[]
                {
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
                        legacyPurgeId,
                        PluginLifecyclePhase.Removing,
                        PluginLifecycleOperationKind.Remove,
                        now
                    ),
                }
            )
            {
                await InsertLegacyLifecycleRecordAsync(db, record);
            }
            _ = await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"plugin_lifecycles\" SET \"Phase\" = 'Purging', "
                    + "\"OperationKind\" = 'Purge' WHERE \"PluginId\" = {0};",
                legacyPurgeId.Value
            );
        }

        await database.MigrateToLatestAsync();

        var store = new EfPluginLifecycleStore(database);
        var normalized = (await store.LoadAllAsync(CancellationToken.None)).ToDictionary(state =>
            state.PluginId
        );
        normalized.Keys.ShouldBe([removingId, removedId, legacyPurgeId], ignoreOrder: true);
        foreach (var state in normalized.Values)
        {
            state.Phase.ShouldBe(PluginLifecyclePhase.Removing);
            state.OperationKind.ShouldBe(PluginLifecycleOperationKind.Remove);
            state.FaultedFrom.ShouldBeNull();
            PluginLifecycleStateMachine.HasValidFaultInvariant(state).ShouldBeTrue();
        }

        var removal = new RecordingRemovalOwner();
        var coordinator = new PluginLifecycleCoordinator(
            store,
            new FakePackageResolver(),
            [new RecordingMigrationOwner()],
            [],
            [removal],
            new RecordingPendingWorkCanceller(),
            new FakeLifecycleWorkers(),
            new PluginRuntimeSnapshotRegistry(),
            new PluginLifecycleSerialization(),
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );

        await coordinator.RecoverAsync(CancellationToken.None);

        (await store.LoadAsync(removingId, CancellationToken.None)).ShouldBeNull();
        (await store.LoadAsync(removedId, CancellationToken.None)).ShouldBeNull();
        (await store.LoadAsync(legacyPurgeId, CancellationToken.None)).ShouldBeNull();
        removal.Calls.ShouldBe(3);
    }

    private static async ValueTask<PluginLifecycleState> AdvanceToRemovingAsync(
        IPluginLifecycleStore store,
        PluginLifecyclePackage package
    )
    {
        var active = await AdvanceToActiveAsync(store, package);
        var draining = Applied(
            PluginLifecycleStateMachine.BeginRemoval(
                active,
                PluginLifecycleOperationId.New(),
                DateTimeOffset.UtcNow
            )
        );
        await WriteAsync(store, active, draining);
        var removing = Applied(
            PluginLifecycleStateMachine.DrainSucceeded(draining, DateTimeOffset.UtcNow)
        );
        await WriteAsync(store, draining, removing);
        return removing;
    }

    private static async ValueTask<PluginLifecycleState> AdvanceToActiveAsync(
        IPluginLifecycleStore store,
        PluginLifecyclePackage package
    )
    {
        var begun = (
            await store.BeginActivationAsync(
                new(
                    package.Installation,
                    package.PackageOperationId,
                    PluginLifecycleOperationId.New(),
                    DateTimeOffset.UtcNow
                ),
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

    private static async ValueTask InsertLegacyLifecycleRecordAsync(
        BlokeBotDbContext db,
        PluginLifecycleRecord record
    ) =>
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "plugin_lifecycles" (
                "PluginId", "SelectedVersion", "SelectedTag", "OperationId",
                "SelectedGeneration", "ActiveVersion", "ActiveTag", "ActiveOperationId",
                "ActiveGeneration", "Phase", "OperationKind", "FaultedFrom",
                "AutomaticRestartConsumed", "RestartNotBeforeUtc", "OutcomeCode",
                "FailureCode", "OutcomeDetail", "OutcomeOccurredAtUtc", "Revision",
                "UpdatedAtUtc"
            ) VALUES (
                {record.PluginId}, {record.SelectedVersion}, {record.SelectedTag},
                {record.OperationId}, {record.SelectedGeneration}, {record.ActiveVersion},
                {record.ActiveTag}, {record.ActiveOperationId}, {record.ActiveGeneration},
                {record.Phase.ToString()}, {record.OperationKind.ToString()},
                {record.FaultedFrom?.ToString()},
                {record.AutomaticRestartConsumed}, {record.RestartNotBeforeUtc},
                {record.OutcomeCode.ToString()},
                {record.FailureCode?.ToString()},
                {record.OutcomeDetail}, {record.OutcomeOccurredAtUtc}, {record.Revision},
                {record.UpdatedAtUtc}
            );
            """
        );

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

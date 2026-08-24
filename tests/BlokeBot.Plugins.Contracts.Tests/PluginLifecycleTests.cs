using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginLifecycleTests
{
    [Test]
    public async Task Update_KeepsOldGenerationAdmittedUntilMigrationFence()
    {
        var harness = new LifecycleHarness();
        var first = await harness.ActivateAsync("1.0.0", "v1");
        var oldFence = Fence(first);
        harness.Workers.PauseValidation();

        var update = harness.ActivateAsync("2.0.0", "v2").AsTask();
        await harness.Workers.ValidationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var duringPreparation = harness.Snapshots.Admit(
            harness.PluginId,
            oldFence,
            PluginFeatureAdmissionReadiness.Ready
        );
        await duringPreparation
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission.DisposeAsync();

        harness.Workers.ResumeValidation();
        var updated = await update;

        updated.View.Phase.ShouldBe(PluginLifecyclePhase.Active);
        updated.View.Generation.Value.ShouldBe(oldFence.Generation.Value + 1);
        harness
            .Snapshots.Admit(harness.PluginId, oldFence, PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.StaleOperation);
        harness.PendingWork.CancelledFences.ShouldBe([oldFence]);
    }

    [Test]
    public async Task Admission_FencesEveryResultPathAndLeavesReadinessExternal()
    {
        var harness = new LifecycleHarness();
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var fence = Fence(active);
        var staleOperation = new PluginLifecycleFence(Operation(), fence.Generation);
        var staleGeneration = new PluginLifecycleFence(fence.OperationId, Generation(99));

        harness
            .Snapshots.Admit(harness.PluginId, fence, PluginFeatureAdmissionReadiness.Disabled)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.Disabled);
        harness
            .Snapshots.Admit(harness.PluginId, fence, PluginFeatureAdmissionReadiness.NotReady)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.NotReady);
        harness
            .Snapshots.ValidateCallbackCompletion(harness.PluginId, staleOperation)
            .ShouldBeOfType<PluginFenceOutcome.Rejected>()
            .Code.ShouldBe(PluginFenceRejectionCode.StaleOperation);
        harness
            .Snapshots.ValidateWorkerResult(harness.PluginId, staleGeneration)
            .ShouldBeOfType<PluginFenceOutcome.Rejected>()
            .Code.ShouldBe(PluginFenceRejectionCode.StaleGeneration);
        harness
            .Snapshots.ValidateCancellation(harness.PluginId, staleGeneration)
            .ShouldBeOfType<PluginFenceOutcome.Rejected>()
            .Code.ShouldBe(PluginFenceRejectionCode.StaleGeneration);
        var durable = harness.Snapshots.AdmitDurableRun(
            harness.PluginId,
            fence,
            PluginFeatureAdmissionReadiness.Ready
        );
        await durable.ShouldBeOfType<PluginAdmissionOutcome.Admitted>().Admission.DisposeAsync();
    }

    [Test]
    public async Task LifecycleChangeTrigger_PublishesOnlyAfterPreparationCompletes()
    {
        var harness = new LifecycleHarness();
        harness.Workers.PauseValidation();
        var observed = harness.Snapshots.CurrentVersion;
        var changed = harness
            .Snapshots.WaitForChangeAsync(observed, CancellationToken.None)
            .AsTask();

        var activation = harness.ActivateAsync("1.0.0", "v1").AsTask();
        await harness.Workers.ValidationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        changed.IsCompleted.ShouldBeFalse();
        harness.Workers.ResumeValidation();
        var active = await activation;
        var version = await changed;

        version.Value.ShouldBeGreaterThan(observed.Value);
        harness
            .Snapshots.Current.Entries[harness.PluginId]
            .Phase.ShouldBe(PluginLifecyclePhase.Active);
        harness.Snapshots.Current.Entries[harness.PluginId].Fence.ShouldBe(Fence(active));
    }

    [Test]
    public async Task Preparation_UsesARealStagingWorkerBesideTheAdmittedWorker()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { prepare = function() return true end }"
        );
        await using var coordinator = new PluginWorkerCoordinator();
        var admitted = await coordinator.StartAsync(
            package.StartOptions(PluginWorkerMode.Admitted, "lifecycle-admitted"),
            CancellationToken.None
        );
        await using var admittedLease = admitted
            .ShouldBeOfType<PluginWorkerReservationOutcome.Started>()
            .Lease;
        var manager = new PluginLifecycleWorkerManager(coordinator);
        var stateRoot = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-lifecycle-staging-{Guid.NewGuid():N}"
        );
        var lifecyclePackage = new PluginLifecyclePackage(
            package.Package.Descriptor.Plugin,
            package.Package,
            stateRoot,
            new ReturningTestDispatcher(new PluginValue.Nil()),
            NullLogger<PluginWorkerClient>.Instance
        );

        try
        {
            var validation = await manager.ValidateAsync(lifecyclePackage, CancellationToken.None);

            validation
                .ShouldBeOfType<PluginLifecycleWorkerStartOutcome.Started>()
                .Worker.Mode.ShouldBe(PluginWorkerMode.Staging);
        }
        finally
        {
            if (Directory.Exists(stateRoot))
            {
                Directory.Delete(stateRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task Remove_CancelsPendingWorkAndTerminatesAtDrainBoundWithoutPurgingData()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var admission = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;

        var removed = await harness.Coordinator.RemoveAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );

        removed
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.DrainTimedOut);
        harness.PendingWork.Calls.ShouldBe(1);
        harness.PendingWork.CancelledFences.ShouldBe([Fence(active)]);
        harness.Purge.Calls.ShouldBe(0);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        await admission.DisposeAsync();
    }

    [Test]
    public async Task Remove_CallerCancellationPropagatesWithoutPersistingDrainTimeout()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(30), TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var callback = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        using var cancellation = new CancellationTokenSource();
        var removal = harness
            .Coordinator.RemoveAsync(harness.PluginId, Operation(), cancellation.Token)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(1);

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await removal);

        var checkpoint = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        checkpoint.Phase.ShouldBe(PluginLifecyclePhase.Draining);
        checkpoint.LatestOutcome.FailureCode.ShouldNotBe(PluginLifecycleFailureCode.DrainTimedOut);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeFalse();

        await callback.DisposeAsync();
        await harness.Coordinator.RecoverAsync(CancellationToken.None);
        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Removed
        );
    }

    [Test]
    public async Task PendingWorkCallerCancellation_PropagatesAndLeavesRecoverableCheckpoint()
    {
        var harness = new LifecycleHarness();
        _ = await harness.ActivateAsync("1.0.0", "v1");
        harness.PendingWork.Pause();
        using var cancellation = new CancellationTokenSource();
        var removal = harness
            .Coordinator.RemoveAsync(harness.PluginId, Operation(), cancellation.Token)
            .AsTask();
        await harness.PendingWork.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await removal);

        var checkpoint = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        checkpoint.Phase.ShouldBe(PluginLifecyclePhase.Draining);
        _ = checkpoint.ActiveRuntime.ShouldNotBeNull();
        checkpoint.LatestOutcome.FailureCode.ShouldNotBe(
            PluginLifecycleFailureCode.CancellationFailed
        );
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeFalse();

        await harness.Coordinator.RecoverAsync(CancellationToken.None);
        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Removed
        );
    }

    [Test]
    public async Task RemoveCancellation_RecoveryRetainsOriginalTrackerAndWorkerUntilDrain()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(30), TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var originalWorker = harness.Workers.Admitted.Single();
        var callback = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        using var cancellation = new CancellationTokenSource();
        var removal = harness
            .Coordinator.RemoveAsync(harness.PluginId, Operation(), cancellation.Token)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(1);

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await removal);

        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();
        _ = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>();
        var recovery = harness.Coordinator.RecoverAsync(CancellationToken.None).AsTask();
        await harness.PendingWork.WaitForCallsAsync(2);
        await Task.Delay(50);
        recovery.IsCompleted.ShouldBeFalse();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();

        harness.Store.PauseNextWrite();
        await callback.DisposeAsync();
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Draining
        );
        recovery.IsCompleted.ShouldBeFalse();
        harness.Store.ResumeWrite();
        await recovery.WaitAsync(TimeSpan.FromSeconds(2));

        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Removed
        );
    }

    [Test]
    public async Task UpdateCancellation_RecoveryDisposesOriginalWorkerBeforeReplacementActivation()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(30), TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var originalWorker = harness.Workers.Admitted.Single();
        var replacement = harness.Package("2.0.0", "v2");
        harness.Packages.Add(replacement);
        var callback = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        using var cancellation = new CancellationTokenSource();
        var update = harness
            .Coordinator.ActivateAsync(Operation(), replacement, cancellation.Token)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(1);

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await update);

        var checkpoint = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        checkpoint.Phase.ShouldBe(PluginLifecyclePhase.Migrating);
        checkpoint.ActiveRuntime!.Fence.ShouldBe(Fence(active));
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();
        harness.Workers.BeforeStartAdmitted = () =>
            originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        var recovery = harness.Coordinator.RecoverAsync(CancellationToken.None).AsTask();
        await harness.PendingWork.WaitForCallsAsync(2);
        await Task.Delay(50);
        recovery.IsCompleted.ShouldBeFalse();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();

        harness.Store.PauseNextWrite();
        await callback.DisposeAsync();
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        var draining = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        draining.Phase.ShouldBe(PluginLifecyclePhase.Migrating);
        _ = draining.ActiveRuntime.ShouldNotBeNull();
        harness.Workers.Admitted.Count.ShouldBe(1);
        recovery.IsCompleted.ShouldBeFalse();
        harness.Store.ResumeWrite();
        await recovery.WaitAsync(TimeSpan.FromSeconds(2));

        var recovered = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        recovered.Phase.ShouldBe(PluginLifecyclePhase.Active);
        recovered.SelectedInstallation.ShouldBe(replacement.Installation);
        harness.Workers.Admitted.Count.ShouldBe(2);
    }

    [Test]
    public async Task UpdateCheckpointWriteFailure_RestoresOrReconcilesRuntimeOwnership()
    {
        var conflict = new LifecycleHarness();
        var conflictActive = await conflict.ActivateAsync("1.0.0", "v1");
        var conflictSlot = conflict.Snapshots.FindCurrent(
            conflict.PluginId,
            Fence(conflictActive)
        )!;
        var conflictWorker = conflict.Workers.Admitted.Single();
        conflict.Store.PauseNextWrite();
        var conflictedUpdate = conflict
            .Coordinator.ActivateAsync(
                Operation(),
                conflict.Package("2.0.0", "v2"),
                CancellationToken.None
            )
            .AsTask();
        await conflict.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var winner = ActiveState(conflict.Package("3.0.0", "v3"));
        conflict.Store.Seed(winner);
        conflict.Store.ResumeWrite();

        _ = (
            await conflictedUpdate.WaitAsync(TimeSpan.FromSeconds(2))
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Rejected>();
        await AssertWinnerReconciledAsync(conflict, conflictActive, winner, conflictWorker);
        ReferenceEquals(
                conflict.Snapshots.FindCurrent(conflict.PluginId, winner.SelectedFence),
                conflictSlot
            )
            .ShouldBeFalse();

        var retained = new LifecycleHarness();
        var retainedActive = await retained.ActivateAsync("1.0.0", "v1");
        var retainedState = (
            await retained.Store.LoadAsync(retained.PluginId, CancellationToken.None)
        )!;
        var retainedSlot = retained.Snapshots.FindCurrent(
            retained.PluginId,
            Fence(retainedActive)
        )!;
        var retainedWorker = retained.Workers.Admitted.Single();
        retained.Store.PauseNextWrite();
        var retainedUpdate = retained
            .Coordinator.ActivateAsync(
                Operation(),
                retained.Package("2.0.0", "v2"),
                CancellationToken.None
            )
            .AsTask();
        await retained.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var activeWinner = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.ActiveRecoverySucceeded(
                    retainedState,
                    DateTimeOffset.UtcNow
                )
        ).State;
        retained.Store.Seed(activeWinner);
        retained.Store.ResumeWrite();

        _ = (
            await retainedUpdate.WaitAsync(TimeSpan.FromSeconds(2))
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Rejected>();
        await AssertOriginalRuntimeRestoredAsync(
            retained,
            retainedActive,
            activeWinner,
            retainedSlot,
            retainedWorker
        );

        var canceled = new LifecycleHarness();
        var canceledActive = await canceled.ActivateAsync("1.0.0", "v1");
        var canceledSlot = canceled.Snapshots.FindCurrent(
            canceled.PluginId,
            Fence(canceledActive)
        )!;
        var canceledWorker = canceled.Workers.Admitted.Single();
        canceled.Store.PauseNextWrite();
        using var cancellation = new CancellationTokenSource();
        var canceledUpdate = canceled
            .Coordinator.ActivateAsync(
                Operation(),
                canceled.Package("2.0.0", "v2"),
                cancellation.Token
            )
            .AsTask();
        await canceled.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var canceledState = (
            await canceled.Store.LoadAsync(canceled.PluginId, CancellationToken.None)
        )!;
        canceledState.Phase.ShouldBe(PluginLifecyclePhase.Preparing);

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await canceledUpdate);
        await AssertOriginalRuntimeRestoredAsync(
            canceled,
            canceledActive,
            canceledState,
            canceledSlot,
            canceledWorker
        );

        var failed = new LifecycleHarness();
        var failedActive = await failed.ActivateAsync("1.0.0", "v1");
        var failedSlot = failed.Snapshots.FindCurrent(failed.PluginId, Fence(failedActive))!;
        var failedWorker = failed.Workers.Admitted.Single();
        failed.Store.ExceptionBeforeNextWrite = new InvalidOperationException(
            "simulated pre-write failure"
        );
        failed.Store.PauseNextWrite();
        var failedUpdate = failed
            .Coordinator.ActivateAsync(
                Operation(),
                failed.Package("2.0.0", "v2"),
                CancellationToken.None
            )
            .AsTask();
        await failed.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var failedState = (await failed.Store.LoadAsync(failed.PluginId, CancellationToken.None))!;
        failedState.Phase.ShouldBe(PluginLifecyclePhase.Preparing);

        failed.Store.ResumeWrite();
        _ = await Should.ThrowAsync<InvalidOperationException>(async () => await failedUpdate);
        await AssertOriginalRuntimeRestoredAsync(
            failed,
            failedActive,
            failedState,
            failedSlot,
            failedWorker
        );
    }

    [Test]
    public async Task RemoveCheckpointWriteFailure_RestoresOrReconcilesRuntimeOwnership()
    {
        var conflict = new LifecycleHarness();
        var conflictActive = await conflict.ActivateAsync("1.0.0", "v1");
        var conflictSlot = conflict.Snapshots.FindCurrent(
            conflict.PluginId,
            Fence(conflictActive)
        )!;
        var conflictWorker = conflict.Workers.Admitted.Single();
        conflict.Store.PauseNextWrite();
        var conflictedRemoval = conflict
            .Coordinator.RemoveAsync(conflict.PluginId, Operation(), CancellationToken.None)
            .AsTask();
        await conflict.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var winner = ActiveState(conflict.Package("2.0.0", "v2"));
        conflict.Store.Seed(winner);
        conflict.Store.ResumeWrite();

        _ = (
            await conflictedRemoval.WaitAsync(TimeSpan.FromSeconds(2))
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Rejected>();
        await AssertWinnerReconciledAsync(conflict, conflictActive, winner, conflictWorker);
        ReferenceEquals(
                conflict.Snapshots.FindCurrent(conflict.PluginId, winner.SelectedFence),
                conflictSlot
            )
            .ShouldBeFalse();

        var canceled = new LifecycleHarness();
        var canceledActive = await canceled.ActivateAsync("1.0.0", "v1");
        var canceledState = (
            await canceled.Store.LoadAsync(canceled.PluginId, CancellationToken.None)
        )!;
        var canceledSlot = canceled.Snapshots.FindCurrent(
            canceled.PluginId,
            Fence(canceledActive)
        )!;
        var canceledWorker = canceled.Workers.Admitted.Single();
        canceled.Store.PauseNextWrite();
        using var cancellation = new CancellationTokenSource();
        var canceledRemoval = canceled
            .Coordinator.RemoveAsync(canceled.PluginId, Operation(), cancellation.Token)
            .AsTask();
        await canceled.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await canceledRemoval);
        await AssertOriginalRuntimeRestoredAsync(
            canceled,
            canceledActive,
            canceledState,
            canceledSlot,
            canceledWorker
        );

        var failed = new LifecycleHarness();
        var failedActive = await failed.ActivateAsync("1.0.0", "v1");
        var failedState = (await failed.Store.LoadAsync(failed.PluginId, CancellationToken.None))!;
        var failedSlot = failed.Snapshots.FindCurrent(failed.PluginId, Fence(failedActive))!;
        var failedWorker = failed.Workers.Admitted.Single();
        failed.Store.ExceptionBeforeNextWrite = new InvalidOperationException(
            "simulated pre-write failure"
        );
        failed.Store.PauseNextWrite();
        var failedRemoval = failed
            .Coordinator.RemoveAsync(failed.PluginId, Operation(), CancellationToken.None)
            .AsTask();
        await failed.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        failed.Store.ResumeWrite();
        _ = await Should.ThrowAsync<InvalidOperationException>(async () => await failedRemoval);
        await AssertOriginalRuntimeRestoredAsync(
            failed,
            failedActive,
            failedState,
            failedSlot,
            failedWorker
        );
    }

    [Test]
    public async Task UpdateCheckpointCancellation_DoesNotReadmitWorkerTerminatedWhileStopped()
    {
        var harness = new LifecycleHarness();
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var other = await harness.ActivateOtherAsync("other-plugin", "1.0.0", "v1");
        var worker = harness.Workers.Admitted[0];
        harness.Store.PauseNextWrite();
        using var cancellation = new CancellationTokenSource();
        var update = harness
            .Coordinator.ActivateAsync(
                Operation(),
                harness.Package("2.0.0", "v2"),
                cancellation.Token
            )
            .AsTask();
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.Exit(PluginWorkerFailureCode.WorkerExited);
        cancellation.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await update);
        await AssertTerminatedRollbackFaultedAsync(harness, active, worker, other);
    }

    [Test]
    public async Task UpdateCheckpointWriteFailure_DoesNotReadmitWorkerTerminatedWhileStopped()
    {
        var harness = new LifecycleHarness();
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var other = await harness.ActivateOtherAsync("other-plugin", "1.0.0", "v1");
        var worker = harness.Workers.Admitted[0];
        harness.Store.ExceptionBeforeNextWrite = new InvalidOperationException(
            "simulated pre-write failure"
        );
        harness.Store.PauseNextWrite();
        var update = harness
            .Coordinator.ActivateAsync(
                Operation(),
                harness.Package("2.0.0", "v2"),
                CancellationToken.None
            )
            .AsTask();
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.Exit(PluginWorkerFailureCode.WorkerExited);
        harness.Store.ResumeWrite();

        _ = await Should.ThrowAsync<InvalidOperationException>(async () => await update);
        await AssertTerminatedRollbackFaultedAsync(harness, active, worker, other);
    }

    [Test]
    public async Task RemoveCheckpointConflict_DoesNotReadmitWorkerTerminatedWhileStopped()
    {
        var harness = new LifecycleHarness();
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var other = await harness.ActivateOtherAsync("other-plugin", "1.0.0", "v1");
        var original = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        var worker = harness.Workers.Admitted[0];
        harness.Store.PauseNextWrite();
        var removal = harness
            .Coordinator.RemoveAsync(harness.PluginId, Operation(), CancellationToken.None)
            .AsTask();
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.Exit(PluginWorkerFailureCode.WorkerExited);
        harness.Store.Seed(
            (
                (PluginLifecycleTransitionOutcome.Applied)
                    PluginLifecycleStateMachine.ActiveRecoverySucceeded(
                        original,
                        DateTimeOffset.UtcNow
                    )
            ).State
        );
        harness.Store.ResumeWrite();

        _ = (
            await removal.WaitAsync(TimeSpan.FromSeconds(2))
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>();
        await AssertTerminatedRollbackFaultedAsync(harness, active, worker, other);
    }

    [Test]
    public async Task CommittedUpdateCheckpoint_ContinuesAfterThrowAndCancellation()
    {
        var thrown = new LifecycleHarness();
        var thrownActive = await thrown.ActivateAsync("1.0.0", "v1");
        var thrownWorker = thrown.Workers.Admitted.Single();
        thrown.Store.ExceptionAfterNextWrite = new InvalidOperationException(
            "simulated commit-then-throw"
        );

        var thrownOutcome = await thrown.Coordinator.ActivateAsync(
            Operation(),
            thrown.Package("2.0.0", "v2"),
            CancellationToken.None
        );

        var thrownView = thrownOutcome
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View;
        thrownView.Installation.Release.DeclaredVersion.Value.ShouldBe("2.0.0");
        thrownWorker.DisposeCalls.ShouldBe(1);
        thrown.PendingWork.CancelledFences.ShouldBe([Fence(thrownActive)]);

        var canceled = new LifecycleHarness();
        var canceledActive = await canceled.ActivateAsync("1.0.0", "v1");
        var canceledWorker = canceled.Workers.Admitted.Single();
        using var cancellation = new CancellationTokenSource();
        canceled.Store.CancellationAfterNextWrite = cancellation;

        var canceledOutcome = await canceled.Coordinator.ActivateAsync(
            Operation(),
            canceled.Package("2.0.0", "v2"),
            cancellation.Token
        );

        var canceledView = canceledOutcome
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View;
        cancellation.IsCancellationRequested.ShouldBeTrue();
        canceledView.Installation.Release.DeclaredVersion.Value.ShouldBe("2.0.0");
        canceledWorker.DisposeCalls.ShouldBe(1);
        canceled.PendingWork.CancelledFences.ShouldBe([Fence(canceledActive)]);
    }

    [Test]
    public async Task CommittedRemoveCheckpoint_ContinuesAfterThrowAndCancellation()
    {
        var thrown = new LifecycleHarness();
        var thrownActive = await thrown.ActivateAsync("1.0.0", "v1");
        var thrownWorker = thrown.Workers.Admitted.Single();
        thrown.Store.ExceptionAfterNextWrite = new InvalidOperationException(
            "simulated commit-then-throw"
        );

        var thrownOutcome = await thrown.Coordinator.RemoveAsync(
            thrown.PluginId,
            Operation(),
            CancellationToken.None
        );

        thrownOutcome
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View.Phase.ShouldBe(PluginLifecyclePhase.Removed);
        thrownWorker.DisposeCalls.ShouldBe(1);
        thrown.PendingWork.CancelledFences.ShouldBe([Fence(thrownActive)]);

        var canceled = new LifecycleHarness();
        var canceledActive = await canceled.ActivateAsync("1.0.0", "v1");
        var canceledWorker = canceled.Workers.Admitted.Single();
        using var cancellation = new CancellationTokenSource();
        canceled.Store.CancellationAfterNextWrite = cancellation;

        var canceledOutcome = await canceled.Coordinator.RemoveAsync(
            canceled.PluginId,
            Operation(),
            cancellation.Token
        );

        cancellation.IsCancellationRequested.ShouldBeTrue();
        canceledOutcome
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View.Phase.ShouldBe(PluginLifecyclePhase.Removed);
        canceledWorker.DisposeCalls.ShouldBe(1);
        canceled.PendingWork.CancelledFences.ShouldBe([Fence(canceledActive)]);
    }

    [Test]
    public async Task PersistedDrainingRecovery_CancelsTheDurablePriorRuntimeFence()
    {
        var harness = new LifecycleHarness();
        var active = ActiveState(harness.Package("1.0.0", "v1"));
        var draining = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginRemoval(
                    active,
                    Operation(),
                    purge: false,
                    DateTimeOffset.UtcNow
                )
        ).State;
        harness.Store.Seed(draining);

        await harness.Coordinator.RecoverAsync(CancellationToken.None);

        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!.Phase.ShouldBe(
            PluginLifecyclePhase.Removed
        );
        harness.PendingWork.CancelledFences.ShouldBe([active.SelectedFence]);
    }

    [Test]
    public async Task RemoveThenReinstall_RetainsOwnedDataAndAdvancesChangedRelease()
    {
        var harness = new LifecycleHarness();
        var first = await harness.ActivateAsync("1.0.0", "v1");
        _ = await harness.Coordinator.RemoveAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );

        var reinstalled = await harness.ActivateAsync("2.0.0", "v2");

        reinstalled.View.Generation.Value.ShouldBe(first.View.Generation.Value + 1);
        reinstalled.View.Phase.ShouldBe(PluginLifecyclePhase.Active);
        harness.Purge.Calls.ShouldBe(0);
        (
            await harness.Store.LoadTombstoneAsync(harness.PluginId, CancellationToken.None)
        ).ShouldBeNull();
    }

    [Test]
    public async Task Purge_RetryAndRepeatConvergeToOneRetainedOutcome()
    {
        var purge = new RecordingPurgeOwner { FailuresRemaining = 1 };
        var harness = new LifecycleHarness(purge: purge);
        _ = await harness.ActivateAsync("1.0.0", "v1");
        var removed = await harness.Coordinator.RemoveAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );

        removed
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View.Phase.ShouldBe(PluginLifecyclePhase.Removed);
        purge.Calls.ShouldBe(0);

        var interrupted = await harness.Coordinator.PurgeAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );
        interrupted
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View.Phase.ShouldBe(PluginLifecyclePhase.Faulted);

        var converged = await harness.Coordinator.PurgeAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );
        var tombstone = converged.ShouldBeOfType<PluginLifecycleCommandOutcome.Purged>().Tombstone;
        var repeated = await harness.Coordinator.PurgeAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );

        repeated
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Purged>()
            .Tombstone.ShouldBe(tombstone);
        tombstone.LatestOutcome.Code.ShouldBe(PluginLifecycleOutcomeCode.Purged);
        tombstone.LatestOutcome.FailureCode.ShouldBeNull();
        purge.Calls.ShouldBe(2);
        harness.Store.Count.ShouldBe(0);
        harness.Store.TombstoneCount.ShouldBe(1);
    }

    [Test]
    public async Task Purge_RemovesRuntimeSlotAndNotifiesLifecycleListeners()
    {
        var purge = new RecordingPurgeOwner();
        var harness = new LifecycleHarness(purge: purge);
        _ = await harness.ActivateAsync("1.0.0", "v1");
        _ = await harness.Coordinator.RemoveAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );
        purge.Pause();
        var purging = harness
            .Coordinator.PurgeAsync(harness.PluginId, Operation(), CancellationToken.None)
            .AsTask();
        await purge.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var observed = harness.Snapshots.CurrentVersion;
        var changed = harness
            .Snapshots.WaitForChangeAsync(observed, CancellationToken.None)
            .AsTask();

        purge.Resume();
        _ = (await purging).ShouldBeOfType<PluginLifecycleCommandOutcome.Purged>();
        var notified = await changed;

        notified.Value.ShouldBeGreaterThan(observed.Value);
        harness.Snapshots.Current.Entries.ContainsKey(harness.PluginId).ShouldBeFalse();
    }

    [Test]
    public async Task UnexpectedWorkerExit_RestartsOnceThenFaultsOnlyThatPlugin()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(2), TimeSpan.Zero));
        var first = await harness.ActivateAsync("1.0.0", "v1");
        var other = await harness.ActivateOtherAsync("other-plugin", "1.0.0", "v1");
        var firstCallback = harness
            .Snapshots.Admit(harness.PluginId, Fence(first), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;

        harness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        await harness.PendingWork.WaitForCallsAsync(1);
        harness.Workers.Admitted.Count.ShouldBe(2);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeFalse();
        harness
            .Snapshots.Admit(harness.PluginId, Fence(first), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.StaleGeneration);
        await firstCallback.DisposeAsync();
        var restarted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state =>
                state.Phase == PluginLifecyclePhase.Active
                && state.AutomaticRestartConsumed
                && harness.Workers.Admitted.Count >= 3
        );
        restarted.SelectedGeneration.Value.ShouldBe(first.View.Generation.Value + 1);
        harness
            .Snapshots.ValidateWorkerResult(harness.PluginId, Fence(first))
            .ShouldBeOfType<PluginFenceOutcome.Rejected>()
            .Code.ShouldBe(PluginFenceRejectionCode.StaleGeneration);
        harness
            .Snapshots.AdmitDurableRun(
                harness.PluginId,
                Fence(first),
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.StaleGeneration);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();

        var restartedFence = restarted.SelectedFence;
        var restartedCallback = harness
            .Snapshots.Admit(
                harness.PluginId,
                restartedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        var restartedWorker = harness.Workers.Admitted.Last();
        restartedWorker.Exit(PluginWorkerFailureCode.WorkerExited);
        await harness.PendingWork.WaitForCallsAsync(2);
        restartedWorker.Disposed.Task.IsCompleted.ShouldBeFalse();
        harness.Workers.Admitted.Count.ShouldBe(3);
        harness
            .Snapshots.Admit(
                harness.PluginId,
                restartedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.Faulted);
        await restartedCallback.DisposeAsync();
        var faulted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        faulted.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.WorkerExited);
        restartedWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        harness.PendingWork.CancelledFences.ShouldBe([Fence(first), restartedFence]);
        var otherAdmission = harness
            .Snapshots.Admit(other.PluginId, other.Fence, PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        await otherAdmission.DisposeAsync();
    }

    [Test]
    public async Task CancellationTermination_RestartsWithoutConsumingUnexpectedExitBudget()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(2), TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var callback = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;

        harness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerTerminated);
        await harness.PendingWork.WaitForCallsAsync(1);
        harness.Workers.Admitted.Count.ShouldBe(1);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeFalse();
        harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.StaleGeneration);
        await callback.DisposeAsync();
        var restarted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state =>
                state.Phase == PluginLifecyclePhase.Active && harness.Workers.Admitted.Count >= 2
        );

        restarted.AutomaticRestartConsumed.ShouldBeFalse();
        restarted.SelectedGeneration.Value.ShouldBe(active.View.Generation.Value + 1);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        harness.PendingWork.CancelledFences.ShouldBe([Fence(active)]);
        harness
            .Snapshots.ValidateCallbackCompletion(harness.PluginId, Fence(active))
            .ShouldBeOfType<PluginFenceOutcome.Rejected>()
            .Code.ShouldBe(PluginFenceRejectionCode.StaleGeneration);
    }

    [Test]
    public async Task WorkerReplacement_StopsOldAdmissionBeforeFreshFenceWrite()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        harness.Store.PauseNextWrite();

        harness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var durable = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        durable.Phase.ShouldBe(PluginLifecyclePhase.Active);
        durable.SelectedFence.ShouldBe(Fence(active));
        harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.StaleGeneration);
        harness.PendingWork.Calls.ShouldBe(0);

        harness.Store.ResumeWrite();
        var restarted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state =>
                state.Phase == PluginLifecyclePhase.Active && state.SelectedFence != Fence(active)
        );
        restarted.AutomaticRestartConsumed.ShouldBeTrue();
    }

    [Test]
    public async Task WorkerReplacement_WriteConflictReconcilesWinnerWithoutRestoringTerminatedWorker()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var originalWorker = harness.Workers.Admitted.Single();
        harness.Store.PauseNextWrite();

        originalWorker.Exit(PluginWorkerFailureCode.WorkerExited);
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var winner = ActiveState(harness.Package("2.0.0", "v2"));
        harness.Store.Seed(winner);
        var observed = harness.Snapshots.CurrentVersion;
        harness.Store.ResumeWrite();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _ = await harness.Snapshots.WaitForChangeAsync(observed, timeout.Token);

        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None)).ShouldBe(winner);
        var entry = harness.Snapshots.Current.Entries[harness.PluginId];
        entry.Installation.ShouldBe(winner.SelectedInstallation);
        entry.Phase.ShouldBe(winner.Phase);
        entry.Fence.ShouldBe(winner.SelectedFence);
        entry.WorkerMode.ShouldBeNull();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        harness.Workers.Admitted.Count.ShouldBe(1);
        _ = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>();
        harness
            .Snapshots.Admit(
                harness.PluginId,
                winner.SelectedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.NotActive);
    }

    [Test]
    public async Task TerminalFaultIntent_StopsAdmissionBeforeWriteAndReconcilesConflictWinner()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var durable = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        harness.Store.Seed(durable with { AutomaticRestartConsumed = true });
        var originalWorker = harness.Workers.Admitted.Single();
        harness.Store.PauseNextWrite();

        originalWorker.Exit(PluginWorkerFailureCode.WorkerExited);
        await harness.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.Faulted);
        var winner = ActiveState(harness.Package("2.0.0", "v2"));
        harness.Store.Seed(winner);
        var observed = harness.Snapshots.CurrentVersion;
        harness.Store.ResumeWrite();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _ = await harness.Snapshots.WaitForChangeAsync(observed, timeout.Token);

        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None)).ShouldBe(winner);
        var entry = harness.Snapshots.Current.Entries[harness.PluginId];
        entry.Installation.ShouldBe(winner.SelectedInstallation);
        entry.Phase.ShouldBe(winner.Phase);
        entry.Fence.ShouldBe(winner.SelectedFence);
        entry.WorkerMode.ShouldBeNull();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        harness.Workers.Admitted.Count.ShouldBe(1);
        _ = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>();
    }

    [Test]
    public async Task CommittedReplacementCheckpoint_ContinuesAfterWriteUncertainty()
    {
        await AssertReplacementCompletesAsync(store =>
            store.ExceptionAfterNextWrite = new InvalidOperationException(
                "simulated commit-then-throw"
            )
        );
        await AssertReplacementCompletesAsync(store =>
            store.ExceptionAfterNextWrite = new OperationCanceledException(
                "simulated commit-then-cancellation"
            )
        );
        await AssertReplacementCompletesAsync(store => store.ConflictAfterNextWrite = true);
    }

    [Test]
    public async Task CommittedTerminalFaultCheckpoint_ContinuesAfterWriteUncertainty()
    {
        await AssertTerminalFaultCompletesAsync(store =>
            store.ExceptionAfterNextWrite = new InvalidOperationException(
                "simulated commit-then-throw"
            )
        );
        await AssertTerminalFaultCompletesAsync(store =>
            store.ExceptionAfterNextWrite = new OperationCanceledException(
                "simulated commit-then-cancellation"
            )
        );
        await AssertTerminalFaultCompletesAsync(store => store.ConflictAfterNextWrite = true);
    }

    [Test]
    public async Task ColdReplacementRecovery_CancelsPersistedOldFenceBeforeStartingWorker()
    {
        var source = new LifecycleHarness();
        var package = source.Package("1.0.0", "v1");
        var active = ActiveState(package);
        var scheduled = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.ScheduleAutomaticRestart(
                    active,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow
                )
        ).State;
        source.Store.Seed(scheduled);
        var packages = new FakePackageResolver();
        packages.Add(package);
        var pendingWork = new RecordingPendingWorkCanceller();
        var workers = new FakeLifecycleWorkers();
        var snapshots = new PluginRuntimeSnapshotRegistry();
        var coordinator = new PluginLifecycleCoordinator(
            source.Store,
            packages,
            [new RecordingMigrationOwner()],
            [new RecordingPurgeOwner()],
            pendingWork,
            workers,
            snapshots,
            new PluginLifecycleSerialization(),
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );

        await coordinator.RecoverAsync(CancellationToken.None);

        var recovered = (await source.Store.LoadAsync(source.PluginId, CancellationToken.None))!;
        recovered.Phase.ShouldBe(PluginLifecyclePhase.Active);
        recovered.SelectedGeneration.ShouldBe(scheduled.SelectedGeneration);
        pendingWork.CancelledFences.ShouldBe([active.SelectedFence]);
        workers.StartedInstallations.ShouldBe([package.Installation]);
    }

    [Test]
    public async Task WorkerReplacement_StopFailureFaultsWithoutStartingReplacement()
    {
        var drainHarness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var drainActive = await drainHarness.ActivateAsync("1.0.0", "v1");
        var callback = drainHarness
            .Snapshots.Admit(
                drainHarness.PluginId,
                Fence(drainActive),
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;

        drainHarness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        var drainFault = await drainHarness.Store.WaitForAsync(
            drainHarness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        drainFault.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.DrainTimedOut);
        drainHarness.Workers.Admitted.Count.ShouldBe(1);
        drainHarness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        drainHarness.PendingWork.CancelledFences.ShouldBe([Fence(drainActive)]);
        await callback.DisposeAsync();

        var cancellationHarness = new LifecycleHarness(
            options: new(TimeSpan.FromSeconds(2), TimeSpan.Zero)
        );
        var cancellationActive = await cancellationHarness.ActivateAsync("1.0.0", "v1");
        cancellationHarness.PendingWork.FailuresRemaining = 1;

        cancellationHarness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        var cancellationFault = await cancellationHarness.Store.WaitForAsync(
            cancellationHarness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        cancellationFault.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.CancellationFailed
        );
        cancellationHarness.Workers.Admitted.Count.ShouldBe(1);
        cancellationHarness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        cancellationHarness.PendingWork.CancelledFences.ShouldBe([Fence(cancellationActive)]);
    }

    [Test]
    public async Task ShutdownExceptions_PersistStableRedactedCommandAndSupervisionFailures()
    {
        var commandCancellation = new LifecycleHarness();
        _ = await commandCancellation.ActivateAsync("1.0.0", "v1");
        commandCancellation.PendingWork.ExceptionToThrow = new InvalidOperationException(
            "raw cancellation secret"
        );

        var cancellationFailure = await commandCancellation.Coordinator.RemoveAsync(
            commandCancellation.PluginId,
            Operation(),
            CancellationToken.None
        );

        var cancellationView = cancellationFailure
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View;
        cancellationView.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.CancellationFailed
        );
        cancellationView.LatestOutcome.Detail!.Value.ShouldBe(
            "Plugin pending-work cancellation failed."
        );
        cancellationView.LatestOutcome.Detail.Value.ShouldNotContain("secret");

        var commandDisposal = new LifecycleHarness();
        commandDisposal.Workers.NextDisposalException = new InvalidOperationException(
            "raw disposal secret"
        );
        _ = await commandDisposal.ActivateAsync("1.0.0", "v1");

        var disposalFailure = await commandDisposal.Coordinator.RemoveAsync(
            commandDisposal.PluginId,
            Operation(),
            CancellationToken.None
        );

        var disposalView = disposalFailure
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View;
        disposalView.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.WorkerDisposalFailed
        );
        disposalView.LatestOutcome.Detail!.Value.ShouldBe(
            "The plugin worker could not be terminated cleanly."
        );
        disposalView.LatestOutcome.Detail.Value.ShouldNotContain("secret");

        var supervised = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        supervised.PendingWork.ExceptionToThrow = new InvalidOperationException(
            "raw supervised cancellation secret"
        );
        _ = await supervised.ActivateAsync("1.0.0", "v1");
        supervised.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        var supervisedFault = await supervised.Store.WaitForAsync(
            supervised.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        supervisedFault.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.CancellationFailed
        );
        supervisedFault.LatestOutcome.Detail!.Value.ShouldNotContain("secret");
        supervised.Workers.Admitted.Count.ShouldBe(1);

        var terminal = new LifecycleHarness();
        var terminalPackage = terminal.Package("1.0.0", "v1");
        terminal.Store.Seed(ActiveState(terminalPackage) with { AutomaticRestartConsumed = true });
        terminal.Packages.Add(terminalPackage);
        terminal.Workers.NextDisposalException = new InvalidOperationException(
            "raw terminal disposal secret"
        );
        await terminal.Coordinator.RecoverAsync(CancellationToken.None);

        terminal.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        var terminalFault = await terminal.Store.WaitForAsync(
            terminal.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        terminalFault.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.WorkerDisposalFailed
        );
        terminalFault.LatestOutcome.Detail!.Value.ShouldNotContain("secret");
        terminal.Workers.Admitted.Count.ShouldBe(1);
    }

    [Test]
    public async Task WorkerReplacement_GenerationExhaustionFaultsAndKeepsOtherPluginAvailable()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(2), TimeSpan.Zero));
        var package = harness.Package("1.0.0", "v1");
        var active = ActiveState(package);
        var maximumGeneration = Generation(long.MaxValue);
        var exhaustedFence = new PluginLifecycleFence(active.OperationId, maximumGeneration);
        var exhausted = active with
        {
            SelectedGeneration = maximumGeneration,
            ActiveRuntime = new(package.Installation, exhaustedFence),
        };
        harness.Store.Seed(exhausted);
        harness.Packages.Add(package);
        await harness.Coordinator.RecoverAsync(CancellationToken.None);
        var other = await harness.ActivateOtherAsync("other-plugin", "1.0.0", "v1");
        var callback = harness
            .Snapshots.Admit(
                harness.PluginId,
                exhaustedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;

        harness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        await harness.PendingWork.WaitForCallsAsync(1);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeFalse();
        harness
            .Snapshots.Admit(
                harness.PluginId,
                exhaustedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.Faulted);
        await callback.DisposeAsync();
        var faulted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        faulted.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.GenerationExhausted);
        harness.PendingWork.CancelledFences.ShouldBe([exhaustedFence]);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        harness.Workers.Admitted.Count.ShouldBe(2);
        var otherAdmission = harness
            .Snapshots.Admit(other.PluginId, other.Fence, PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        await otherAdmission.DisposeAsync();
    }

    [Test]
    public async Task TerminalFaultCrashAfterIntentPersistence_RecoversWithoutRestartingCode()
    {
        var source = new LifecycleHarness();
        var package = source.Package("1.0.0", "v1");
        var active = ActiveState(package);
        var maximumGeneration = Generation(long.MaxValue);
        var fence = new PluginLifecycleFence(active.OperationId, maximumGeneration);
        active = active with
        {
            SelectedGeneration = maximumGeneration,
            ActiveRuntime = new(package.Installation, fence),
        };
        var intent = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginFaultShutdown(
                    active,
                    PluginLifecycleFailureCode.GenerationExhausted,
                    null,
                    DateTimeOffset.UtcNow
                )
        ).State;
        source.Store.Seed(intent);
        var pendingWork = new RecordingPendingWorkCanceller();
        var workers = new FakeLifecycleWorkers();
        var snapshots = new PluginRuntimeSnapshotRegistry();
        var recovery = RecoveryCoordinator(source.Store, pendingWork, workers, snapshots);

        await recovery.RecoverAsync(CancellationToken.None);

        var finalized = (await source.Store.LoadAsync(source.PluginId, CancellationToken.None))!;
        intent.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.GenerationExhausted);
        finalized.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        finalized.ActiveRuntime.ShouldBeNull();
        finalized.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.GenerationExhausted
        );
        pendingWork.CancelledFences.ShouldBe([fence]);
        workers.StartedInstallations.ShouldBeEmpty();
        snapshots.Current.Entries[source.PluginId].Phase.ShouldBe(PluginLifecyclePhase.Faulted);
    }

    [Test]
    public async Task TerminalFaultCrashAfterAdmissionStop_RecoversWithoutExtraRestart()
    {
        var source = new LifecycleHarness();
        var package = source.Package("1.0.0", "v1");
        var active = ActiveState(package) with { AutomaticRestartConsumed = true };
        source.Store.Seed(active);
        source.Packages.Add(package);
        await source.Coordinator.RecoverAsync(CancellationToken.None);
        source.PendingWork.Pause();

        source.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        await source.PendingWork.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var intent = (await source.Store.LoadAsync(source.PluginId, CancellationToken.None))!;
        source
            .Snapshots.Admit(
                source.PluginId,
                active.SelectedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.Faulted);

        var crashStore = new InMemoryLifecycleStore();
        crashStore.Seed(intent);
        var pendingWork = new RecordingPendingWorkCanceller();
        var workers = new FakeLifecycleWorkers();
        var recovery = RecoveryCoordinator(
            crashStore,
            pendingWork,
            workers,
            new PluginRuntimeSnapshotRegistry()
        );
        await recovery.RecoverAsync(CancellationToken.None);

        var finalized = (await crashStore.LoadAsync(source.PluginId, CancellationToken.None))!;
        finalized.ActiveRuntime.ShouldBeNull();
        finalized.AutomaticRestartConsumed.ShouldBeTrue();
        pendingWork.CancelledFences.ShouldBe([active.SelectedFence]);
        workers.StartedInstallations.ShouldBeEmpty();

        source.PendingWork.Resume();
        _ = await source.Store.WaitForAsync(
            source.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );
    }

    [Test]
    public async Task TerminalFaultCrashAfterShutdownBeforeFinalClear_ReplaysSafely()
    {
        var source = new LifecycleHarness();
        var package = source.Package("1.0.0", "v1");
        var active = ActiveState(package) with { AutomaticRestartConsumed = true };
        source.Store.Seed(active);
        source.Packages.Add(package);
        await source.Coordinator.RecoverAsync(CancellationToken.None);
        source.PendingWork.Pause();

        source.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        await source.PendingWork.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        source.Store.PauseNextWrite();
        source.PendingWork.Resume();
        await source.Store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        source.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        var intent = (await source.Store.LoadAsync(source.PluginId, CancellationToken.None))!;
        _ = intent.ActiveRuntime.ShouldNotBeNull();

        var crashStore = new InMemoryLifecycleStore();
        crashStore.Seed(intent);
        var pendingWork = new RecordingPendingWorkCanceller();
        var workers = new FakeLifecycleWorkers();
        var recovery = RecoveryCoordinator(
            crashStore,
            pendingWork,
            workers,
            new PluginRuntimeSnapshotRegistry()
        );
        await recovery.RecoverAsync(CancellationToken.None);

        var finalized = (await crashStore.LoadAsync(source.PluginId, CancellationToken.None))!;
        finalized.ActiveRuntime.ShouldBeNull();
        finalized.AutomaticRestartConsumed.ShouldBeTrue();
        pendingWork.CancelledFences.ShouldBe([active.SelectedFence]);
        workers.StartedInstallations.ShouldBeEmpty();

        source.Store.ResumeWrite();
        _ = await source.Store.WaitForAsync(
            source.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );
    }

    [Test]
    public async Task BotAdminRestart_ClearsFaultAndRestartBudgetForSelectedGeneration()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var activated = await harness.ActivateAsync("1.0.0", "v1");
        harness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        _ = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state.Phase == PluginLifecyclePhase.Active && state.AutomaticRestartConsumed
        );
        harness.Workers.Admitted.Last().Exit(PluginWorkerFailureCode.WorkerExited);
        var faulted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );
        harness.Packages.Add(harness.Package("1.0.0", "v1"));

        var restarted = await harness.Coordinator.RestartAsync(
            harness.PluginId,
            Operation(),
            CancellationToken.None
        );

        var view = restarted.ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>().View;
        view.Generation.Value.ShouldBe(activated.View.Generation.Value + 1);
        view.AutomaticRestartConsumed.ShouldBeFalse();
        view.LatestOutcome.Code.ShouldBe(PluginLifecycleOutcomeCode.Restarted);
        faulted.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.WorkerExited);
    }

    [Test]
    public async Task BotAdminRestart_CompletesPendingFaultShutdownBeforeRecoveringInstallation()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(30), TimeSpan.Zero));
        var package = harness.Package("1.0.0", "v1");
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var originalWorker = harness.Workers.Admitted.Single();
        var callback = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        var intent = await LeavePendingFaultShutdownAsync(harness);
        using var cancellation = new CancellationTokenSource();
        var canceledRestart = harness
            .Coordinator.RestartAsync(harness.PluginId, Operation(), cancellation.Token)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(1);

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await canceledRestart);

        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None)).ShouldBe(intent);
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();
        harness.Packages.Add(package);
        harness.Workers.BeforeStartAdmitted = () =>
            originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        var restarted = harness
            .Coordinator.RestartAsync(harness.PluginId, Operation(), CancellationToken.None)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(2);
        await Task.Delay(50);
        restarted.IsCompleted.ShouldBeFalse();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();

        await callback.DisposeAsync();
        var outcome = (
            await restarted.WaitAsync(TimeSpan.FromSeconds(2))
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();

        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        outcome.View.Installation.ShouldBe(package.Installation);
        outcome.View.Phase.ShouldBe(PluginLifecyclePhase.Active);
        outcome.View.AutomaticRestartConsumed.ShouldBeFalse();
    }

    [Test]
    public async Task DifferentVersionActivation_CompletesPendingFaultShutdownBeforePreparation()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.FromSeconds(30), TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var originalWorker = harness.Workers.Admitted.Single();
        var callback = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        var intent = await LeavePendingFaultShutdownAsync(harness);
        var replacement = harness.Package("2.0.0", "v2");
        using var cancellation = new CancellationTokenSource();
        var canceledActivation = harness
            .Coordinator.ActivateAsync(Operation(), replacement, cancellation.Token)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(1);

        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await canceledActivation
        );

        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None)).ShouldBe(intent);
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();
        harness.Workers.BeforeStartAdmitted = () =>
            originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        var activated = harness
            .Coordinator.ActivateAsync(Operation(), replacement, CancellationToken.None)
            .AsTask();
        await harness.PendingWork.WaitForCallsAsync(2);
        await Task.Delay(50);
        activated.IsCompleted.ShouldBeFalse();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeFalse();

        await callback.DisposeAsync();
        var outcome = (
            await activated.WaitAsync(TimeSpan.FromSeconds(2))
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();

        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        outcome.View.Installation.ShouldBe(replacement.Installation);
        outcome.View.Generation.Value.ShouldBeGreaterThan(active.View.Generation.Value);
    }

    [Test]
    public async Task FailedUpdatePreparation_RestoresConsumedRestartBudget()
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        _ = await harness.ActivateAsync("1.0.0", "v1");
        harness.Workers.Admitted[0].Exit(PluginWorkerFailureCode.WorkerExited);
        var restarted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state.Phase == PluginLifecyclePhase.Active && state.AutomaticRestartConsumed
        );
        var workersAfterRestart = harness.Workers.Admitted.Count;
        harness.Workers.ValidationFailure = new(
            PluginLifecycleFailureCode.PreparationRejected,
            null
        );

        var failedUpdate = await harness.Coordinator.ActivateAsync(
            Operation(),
            harness.Package("2.0.0", "v2"),
            CancellationToken.None
        );

        var restored = failedUpdate.ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>().View;
        restored.Installation.ShouldBe(restarted.SelectedInstallation);
        restored.Generation.ShouldBe(restarted.SelectedGeneration);
        restored.AutomaticRestartConsumed.ShouldBeTrue();

        harness.Workers.Admitted.Last().Exit(PluginWorkerFailureCode.WorkerExited);
        var faulted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );

        faulted.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.WorkerExited);
        harness.Workers.Admitted.Count.ShouldBe(workersAfterRestart);
    }

    [Test]
    public async Task ActivateFaultedInstallation_RejectsSameAndAllowsDifferentRelease()
    {
        var harness = new LifecycleHarness();
        var faultedPackage = harness.Package("1.0.0", "v1");
        harness.Workers.ValidationFailure = new(
            PluginLifecycleFailureCode.PreparationRejected,
            null
        );
        var faulted = (
            await harness.Coordinator.ActivateAsync(
                Operation(),
                faultedPackage,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View;

        var sameInstallation = await harness.Coordinator.ActivateAsync(
            Operation(),
            faultedPackage,
            CancellationToken.None
        );
        harness.Workers.ValidationFailure = null;
        var differentInstallation = await harness.Coordinator.ActivateAsync(
            Operation(),
            harness.Package("2.0.0", "v2"),
            CancellationToken.None
        );

        sameInstallation
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Rejected>()
            .Code.ShouldBe(PluginLifecycleCommandRejectionCode.FaultedInstallation);
        var activated = differentInstallation
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View;
        activated.Generation.Value.ShouldBe(faulted.Generation.Value + 1);
        activated.Installation.Release.DeclaredVersion.Value.ShouldBe("2.0.0");
    }

    [Test]
    public async Task BotAdminRestart_ResumesPreparationAndMigrationFaultCheckpoints()
    {
        var preparationHarness = new LifecycleHarness();
        var preparationPackage = preparationHarness.Package("1.0.0", "v1");
        preparationHarness.Workers.ValidationFailure = new(
            PluginLifecycleFailureCode.PreparationRejected,
            null
        );
        var preparationFault = (
            await preparationHarness.Coordinator.ActivateAsync(
                Operation(),
                preparationPackage,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View;
        preparationHarness.Workers.ValidationFailure = null;
        preparationHarness.Packages.Add(preparationPackage);

        var prepared = (
            await preparationHarness.Coordinator.RestartAsync(
                preparationHarness.PluginId,
                Operation(),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View;

        preparationFault.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        prepared.Phase.ShouldBe(PluginLifecyclePhase.Active);
        prepared.Generation.ShouldBe(preparationFault.Generation);
        prepared.LatestOutcome.Code.ShouldBe(PluginLifecycleOutcomeCode.Restarted);

        var migration = new RecordingMigrationOwner { FailuresRemaining = 1 };
        var migrationHarness = new LifecycleHarness(migration: migration);
        var migrationPackage = migrationHarness.Package("1.0.0", "v1");
        var migrationFault = (
            await migrationHarness.Coordinator.ActivateAsync(
                Operation(),
                migrationPackage,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View;
        migrationHarness.Packages.Add(migrationPackage);

        var migrated = (
            await migrationHarness.Coordinator.RestartAsync(
                migrationHarness.PluginId,
                Operation(),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>()
            .View;

        migrationFault.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.MigrationFailed
        );
        migrated.Phase.ShouldBe(PluginLifecyclePhase.Active);
        migrated.Generation.ShouldBe(migrationFault.Generation);
        migrated.LatestOutcome.Code.ShouldBe(PluginLifecycleOutcomeCode.Restarted);
        migration.Calls.ShouldBe(2);
    }

    [Test]
    public async Task ColdMigrationRecovery_CancelsPersistedOldFenceWithoutRestartingOldCode()
    {
        var harness = new LifecycleHarness();
        var previous = await harness.ActivateAsync("1.0.0", "v1");
        var oldFence = Fence(previous);
        var previousPackage = harness.Package("1.0.0", "v1");
        var package = harness.Package("2.0.0", "v2");
        var preparing = (
            await harness.Store.BeginActivationAsync(
                new(package.Installation, Operation(), DateTimeOffset.UtcNow),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleStoreBeginOutcome.Begun>()
            .State;
        var migrating = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.PreparationSucceeded(preparing, DateTimeOffset.UtcNow)
        ).State;
        _ = (
            await harness.Store.WriteAsync(preparing, migrating, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleStoreWriteOutcome.Written>();
        migrating.ActiveRuntime!.Fence.ShouldBe(oldFence);

        var coldPackages = new FakePackageResolver();
        coldPackages.Add(previousPackage);
        coldPackages.Add(package);
        var coldMigration = new RecordingMigrationOwner();
        var coldPendingWork = new RecordingPendingWorkCanceller();
        var coldWorkers = new FakeLifecycleWorkers();
        var coldSnapshots = new PluginRuntimeSnapshotRegistry();
        var coldCoordinator = new PluginLifecycleCoordinator(
            harness.Store,
            coldPackages,
            [coldMigration],
            [new RecordingPurgeOwner()],
            coldPendingWork,
            coldWorkers,
            coldSnapshots,
            new PluginLifecycleSerialization(),
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );
        coldSnapshots.Current.Entries.ShouldBeEmpty();

        await coldCoordinator.RecoverAsync(CancellationToken.None);

        var recovered = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        recovered.Phase.ShouldBe(PluginLifecyclePhase.Active);
        recovered.SelectedInstallation.ShouldBe(package.Installation);
        coldMigration.Calls.ShouldBe(1);
        coldPendingWork.CancelledFences.ShouldBe([oldFence]);
        coldWorkers.StartedInstallations.ShouldBe([package.Installation]);
        _ = coldSnapshots
            .Admit(harness.PluginId, oldFence, PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>();
    }

    [Test]
    public async Task MigrationOwnerException_PersistsStableRedactedFailureOnly()
    {
        var harness = new LifecycleHarness(migration: new ThrowingMigrationOwner());

        var outcome = await harness.Coordinator.ActivateAsync(
            Operation(),
            harness.Package("1.0.0", "v1"),
            CancellationToken.None
        );

        var failed = outcome.ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>().View;
        failed.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.MigrationFailed);
        failed.LatestOutcome.Detail!.Value.ShouldBe("A plugin migration data owner failed.");
        failed.LatestOutcome.Detail.Value.ShouldNotContain("raw-secret");
    }

    [Test]
    public async Task ActivationFailure_FaultsSelectedGenerationWithoutRestoringOldRuntime()
    {
        var harness = new LifecycleHarness();
        var previous = await harness.ActivateAsync("1.0.0", "v1");
        harness.Workers.AdmittedFailuresRemaining = 1;

        var failed = (
            await harness.Coordinator.ActivateAsync(
                Operation(),
                harness.Package("2.0.0", "v2"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>()
            .View;

        failed.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        failed.Installation.Release.DeclaredVersion.Value.ShouldBe("2.0.0");
        failed.Generation.Value.ShouldBe(previous.View.Generation.Value + 1);
        harness.Workers.Admitted[0].Disposed.Task.IsCompleted.ShouldBeTrue();
        _ = harness
            .Snapshots.Admit(
                harness.PluginId,
                Fence(previous),
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>();
    }

    [Test]
    public async Task PreparationRecovery_FailureKeepsPreviousRuntimeAndGeneration()
    {
        var harness = new LifecycleHarness();
        var previous = await harness.ActivateAsync("1.0.0", "v1");
        var previousPackage = harness.Package("1.0.0", "v1");
        var selectedPackage = harness.Package("2.0.0", "v2");
        harness.Packages.Add(previousPackage);
        harness.Packages.Add(selectedPackage);
        var active = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        var preparing = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginActivation(
                    active,
                    selectedPackage.Installation,
                    Operation(),
                    DateTimeOffset.UtcNow
                )
        ).State;
        harness.Store.Seed(preparing);
        harness.Workers.ValidationFailure = new(
            PluginLifecycleFailureCode.PreparationRejected,
            null
        );

        await harness.Coordinator.RecoverAsync(CancellationToken.None);

        var recovered = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        recovered.Phase.ShouldBe(PluginLifecyclePhase.Active);
        recovered.SelectedInstallation.ShouldBe(previous.View.Installation);
        recovered.SelectedGeneration.ShouldBe(previous.View.Generation);
        var admission = harness
            .Snapshots.Admit(
                harness.PluginId,
                Fence(previous),
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>();
        await admission.Admission.DisposeAsync();
    }

    [Test]
    public async Task StartupRecovery_UnavailablePluginFaultsWithoutBlockingRecoverablePlugin()
    {
        var harness = new LifecycleHarness();
        var unavailablePackage = harness.Package("1.0.0", "v1");
        var recoverablePackage = harness.PackageFor("other-plugin", "1.0.0", "v1");
        var unavailable = ActiveState(unavailablePackage);
        var recoverable = ActiveState(recoverablePackage);
        harness.Store.Seed(unavailable);
        harness.Store.Seed(recoverable);
        harness.Packages.Add(recoverablePackage);

        await harness.Coordinator.RecoverAsync(CancellationToken.None);

        var faulted = (
            await harness.Store.LoadAsync(unavailable.PluginId, CancellationToken.None)
        )!;
        var active = (await harness.Store.LoadAsync(recoverable.PluginId, CancellationToken.None))!;
        faulted.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        faulted.ActiveRuntime.ShouldBeNull();
        faulted.LatestOutcome.FailureCode.ShouldBe(
            PluginLifecycleFailureCode.RecoveryPackageUnavailable
        );
        harness.PendingWork.CancelledFences.ShouldContain(unavailable.SelectedFence);
        harness.Workers.StartedInstallations.ShouldBe([recoverablePackage.Installation]);
        var admission = harness
            .Snapshots.Admit(
                recoverable.PluginId,
                active.SelectedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        await admission.DisposeAsync();
    }

    [Test]
    public async Task ActivationRecovery_StartsSelectedGenerationWithoutRepeatingMigration()
    {
        var migration = new RecordingMigrationOwner();
        var harness = new LifecycleHarness(migration: migration);
        var package = harness.Package("1.0.0", "v1");
        harness.Packages.Add(package);
        var preparing = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginActivation(
                    null,
                    package.Installation,
                    Operation(),
                    DateTimeOffset.UtcNow
                )
        ).State;
        var migrating = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.PreparationSucceeded(preparing, DateTimeOffset.UtcNow)
        ).State;
        var activating = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.MigrationSucceeded(migrating, DateTimeOffset.UtcNow)
        ).State;
        harness.Store.Seed(activating);

        await harness.Coordinator.RecoverAsync(CancellationToken.None);

        var recovered = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        recovered.Phase.ShouldBe(PluginLifecyclePhase.Active);
        recovered.SelectedFence.ShouldBe(activating.SelectedFence);
        migration.Calls.ShouldBe(0);
        harness.Workers.Admitted.Count.ShouldBe(1);
    }

    [Test]
    public async Task RemovalAndPurgeRecovery_ConvergeFromDurableCheckpoints()
    {
        var removeHarness = new LifecycleHarness();
        var removing = RemovingState(removeHarness.Package("1.0.0", "v1"));
        removeHarness.Store.Seed(removing);

        await removeHarness.Coordinator.RecoverAsync(CancellationToken.None);

        (
            await removeHarness.Store.LoadAsync(removeHarness.PluginId, CancellationToken.None)
        )!.Phase.ShouldBe(PluginLifecyclePhase.Removed);
        removeHarness.Purge.Calls.ShouldBe(0);

        var purgeHarness = new LifecycleHarness();
        var removed = RemovingState(purgeHarness.Package("1.0.0", "v1"));
        removed = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.RemovalSucceeded(removed, DateTimeOffset.UtcNow)
        ).State;
        var purging = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginRemoval(
                    removed,
                    Operation(),
                    purge: true,
                    DateTimeOffset.UtcNow
                )
        ).State;
        purgeHarness.Store.Seed(purging);

        await purgeHarness.Coordinator.RecoverAsync(CancellationToken.None);

        (
            await purgeHarness.Store.LoadAsync(purgeHarness.PluginId, CancellationToken.None)
        ).ShouldBeNull();
        (
            await purgeHarness.Store.LoadTombstoneAsync(
                purgeHarness.PluginId,
                CancellationToken.None
            )
        )!.LatestOutcome.Code.ShouldBe(PluginLifecycleOutcomeCode.Purged);
        purgeHarness.Purge.Calls.ShouldBe(1);
    }

    [Test]
    public void FaultTransition_RejectsMismatchedAndTerminalPhases()
    {
        var package = new LifecycleHarness().Package("1.0.0", "v1");
        var active = ActiveState(package);
        var mismatched = PluginLifecycleStateMachine.Fault(
            active,
            PluginLifecyclePhase.Migrating,
            PluginLifecycleFailureCode.MigrationFailed,
            null,
            DateTimeOffset.UtcNow
        );
        var directActiveFault = PluginLifecycleStateMachine.Fault(
            active,
            PluginLifecyclePhase.Active,
            PluginLifecycleFailureCode.WorkerExited,
            null,
            DateTimeOffset.UtcNow
        );
        var faultIntent = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginFaultShutdown(
                    active,
                    PluginLifecycleFailureCode.WorkerExited,
                    null,
                    DateTimeOffset.UtcNow
                )
        ).State;
        var faulted = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.CompleteFaultShutdown(
                    faultIntent,
                    null,
                    null,
                    DateTimeOffset.UtcNow
                )
        ).State;
        var removing = RemovingState(package);
        var removed = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.RemovalSucceeded(removing, DateTimeOffset.UtcNow)
        ).State;
        mismatched
            .ShouldBeOfType<PluginLifecycleTransitionOutcome.Rejected>()
            .Code.ShouldBe(PluginLifecycleTransitionFailureCode.InvalidTransition);
        directActiveFault
            .ShouldBeOfType<PluginLifecycleTransitionOutcome.Rejected>()
            .Code.ShouldBe(PluginLifecycleTransitionFailureCode.InvalidTransition);
        AssertFaultRejected(removed);
        AssertFaultRejected(faulted);
    }

    private static PluginLifecycleFence Fence(PluginLifecycleCommandOutcome.Succeeded outcome) =>
        new(outcome.View.OperationId, outcome.View.Generation);

    private static PluginLifecycleOperationId Operation() => PluginLifecycleOperationId.New();

    private static PluginWorkerGeneration Generation(ulong value) =>
        PluginWorkerGeneration.TryCreate(value, out var generation)
            ? generation
            : throw new InvalidOperationException("Invalid test generation.");

    private static async ValueTask AssertTerminatedRollbackFaultedAsync(
        LifecycleHarness harness,
        PluginLifecycleCommandOutcome.Succeeded active,
        FakeLifecycleWorkerSession worker,
        (PluginId PluginId, PluginLifecycleFence Fence) other
    )
    {
        var faulted = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        faulted.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        faulted.ActiveRuntime.ShouldBeNull();
        faulted.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.WorkerExited);
        var entry = harness.Snapshots.Current.Entries[harness.PluginId];
        entry.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        entry.Fence.ShouldBe(faulted.SelectedFence);
        entry.WorkerMode.ShouldBeNull();
        worker.DisposeCalls.ShouldBe(1);
        harness.PendingWork.CancelledFences.ShouldBe([Fence(active)]);
        harness.Workers.Admitted.Count.ShouldBe(2);
        harness
            .Workers.StartedInstallations.Count(installation =>
                installation.PluginId == harness.PluginId
            )
            .ShouldBe(1);
        harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.Faulted);
        var otherAdmission = harness
            .Snapshots.Admit(other.PluginId, other.Fence, PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        await otherAdmission.DisposeAsync();
    }

    private static async ValueTask AssertReplacementCompletesAsync(
        Action<InMemoryLifecycleStore> arrangeUncertainty
    )
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var worker = harness.Workers.Admitted.Single();
        arrangeUncertainty(harness.Store);

        worker.Exit(PluginWorkerFailureCode.WorkerExited);

        var restarted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state =>
                state.Phase == PluginLifecyclePhase.Active && state.SelectedFence != Fence(active)
        );
        restarted.AutomaticRestartConsumed.ShouldBeTrue();
        worker.DisposeCalls.ShouldBe(1);
        harness.PendingWork.CancelledFences.ShouldBe([Fence(active)]);
        harness.Workers.Admitted.Count.ShouldBe(2);
        harness.Snapshots.Current.Entries[harness.PluginId].Fence.ShouldBe(restarted.SelectedFence);
    }

    private static async ValueTask AssertTerminalFaultCompletesAsync(
        Action<InMemoryLifecycleStore> arrangeUncertainty
    )
    {
        var harness = new LifecycleHarness(options: new(TimeSpan.Zero, TimeSpan.Zero));
        var active = await harness.ActivateAsync("1.0.0", "v1");
        var durable = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        harness.Store.Seed(durable with { AutomaticRestartConsumed = true });
        var worker = harness.Workers.Admitted.Single();
        arrangeUncertainty(harness.Store);

        worker.Exit(PluginWorkerFailureCode.WorkerExited);

        var faulted = await harness.Store.WaitForAsync(
            harness.PluginId,
            state => state is { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: null }
        );
        faulted.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.WorkerExited);
        worker.DisposeCalls.ShouldBe(1);
        harness.PendingWork.CancelledFences.ShouldBe([Fence(active)]);
        harness.Workers.Admitted.Count.ShouldBe(1);
        harness
            .Snapshots.Current.Entries[harness.PluginId]
            .Phase.ShouldBe(PluginLifecyclePhase.Faulted);
    }

    private static async ValueTask AssertOriginalRuntimeRestoredAsync(
        LifecycleHarness harness,
        PluginLifecycleCommandOutcome.Succeeded active,
        PluginLifecycleState expectedState,
        PluginRuntimeSlot expectedSlot,
        FakeLifecycleWorkerSession expectedWorker
    )
    {
        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None)).ShouldBe(
            expectedState
        );
        ReferenceEquals(
                harness.Snapshots.FindCurrent(harness.PluginId, Fence(active)),
                expectedSlot
            )
            .ShouldBeTrue();
        expectedWorker.Disposed.Task.IsCompleted.ShouldBeFalse();
        harness.Workers.Admitted.Count.ShouldBe(1);
        var admission = harness
            .Snapshots.Admit(harness.PluginId, Fence(active), PluginFeatureAdmissionReadiness.Ready)
            .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
            .Admission;
        await admission.DisposeAsync();
    }

    private static async ValueTask AssertWinnerReconciledAsync(
        LifecycleHarness harness,
        PluginLifecycleCommandOutcome.Succeeded original,
        PluginLifecycleState winner,
        FakeLifecycleWorkerSession originalWorker
    )
    {
        (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None)).ShouldBe(winner);
        var entry = harness.Snapshots.Current.Entries[harness.PluginId];
        entry.Installation.ShouldBe(winner.SelectedInstallation);
        entry.Phase.ShouldBe(winner.Phase);
        entry.Fence.ShouldBe(winner.SelectedFence);
        entry.WorkerMode.ShouldBeNull();
        originalWorker.Disposed.Task.IsCompleted.ShouldBeTrue();
        harness.Workers.Admitted.Count.ShouldBe(1);
        _ = harness
            .Snapshots.Admit(
                harness.PluginId,
                Fence(original),
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>();
        harness
            .Snapshots.Admit(
                harness.PluginId,
                winner.SelectedFence,
                PluginFeatureAdmissionReadiness.Ready
            )
            .ShouldBeOfType<PluginAdmissionOutcome.Rejected>()
            .Code.ShouldBe(PluginAdmissionRejectionCode.NotActive);
    }

    private static PluginLifecycleCoordinator RecoveryCoordinator(
        InMemoryLifecycleStore store,
        RecordingPendingWorkCanceller pendingWork,
        FakeLifecycleWorkers workers,
        PluginRuntimeSnapshotRegistry snapshots
    ) =>
        new(
            store,
            new FakePackageResolver(),
            [new RecordingMigrationOwner()],
            [new RecordingPurgeOwner()],
            pendingWork,
            workers,
            snapshots,
            new PluginLifecycleSerialization(),
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );

    private static async ValueTask<PluginLifecycleState> LeavePendingFaultShutdownAsync(
        LifecycleHarness harness
    )
    {
        var active = (await harness.Store.LoadAsync(harness.PluginId, CancellationToken.None))!;
        active = active with { AutomaticRestartConsumed = true };
        var intent = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginFaultShutdown(
                    active,
                    PluginLifecycleFailureCode.WorkerExited,
                    null,
                    DateTimeOffset.UtcNow
                )
        ).State;
        harness.Store.Seed(intent);
        return intent;
    }

    private static void AssertFaultRejected(PluginLifecycleState state) =>
        PluginLifecycleStateMachine
            .Fault(
                state,
                state.Phase,
                PluginLifecycleFailureCode.RecoveryFailed,
                null,
                DateTimeOffset.UtcNow
            )
            .ShouldBeOfType<PluginLifecycleTransitionOutcome.Rejected>()
            .Code.ShouldBe(PluginLifecycleTransitionFailureCode.InvalidTransition);

    private static PluginLifecycleState ActiveState(PluginLifecyclePackage package)
    {
        var preparing = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginActivation(
                    null,
                    package.Installation,
                    Operation(),
                    DateTimeOffset.UtcNow
                )
        ).State;
        var migrating = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.PreparationSucceeded(preparing, DateTimeOffset.UtcNow)
        ).State;
        var activating = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.MigrationSucceeded(migrating, DateTimeOffset.UtcNow)
        ).State;
        return (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.ActivationSucceeded(activating, DateTimeOffset.UtcNow)
        ).State;
    }

    private static PluginLifecycleState RemovingState(PluginLifecyclePackage package)
    {
        var active = ActiveState(package);
        var draining = (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.BeginRemoval(
                    active,
                    Operation(),
                    purge: false,
                    DateTimeOffset.UtcNow
                )
        ).State;
        return (
            (PluginLifecycleTransitionOutcome.Applied)
                PluginLifecycleStateMachine.DrainSucceeded(draining, DateTimeOffset.UtcNow)
        ).State;
    }
}

internal sealed class LifecycleHarness
{
    internal LifecycleHarness(
        PluginLifecycleOptions? options = null,
        IPluginMigrationDataOwner? migration = null,
        RecordingPurgeOwner? purge = null
    )
    {
        Migration = migration ?? new RecordingMigrationOwner();
        Purge = purge ?? new RecordingPurgeOwner();
        PluginId = Id("test-plugin");
        Coordinator = new(
            Store,
            Packages,
            [Migration],
            [Purge],
            PendingWork,
            Workers,
            Snapshots,
            Serialization,
            options ?? new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );
    }

    internal PluginId PluginId { get; }

    internal InMemoryLifecycleStore Store { get; } = new();

    internal FakePackageResolver Packages { get; } = new();

    internal IPluginMigrationDataOwner Migration { get; }

    internal RecordingPurgeOwner Purge { get; }

    internal RecordingPendingWorkCanceller PendingWork { get; } = new();

    internal FakeLifecycleWorkers Workers { get; } = new();

    internal PluginRuntimeSnapshotRegistry Snapshots { get; } = new();

    internal PluginLifecycleSerialization Serialization { get; } = new();

    internal PluginLifecycleCoordinator Coordinator { get; }

    internal async ValueTask<PluginLifecycleCommandOutcome.Succeeded> ActivateAsync(
        string version,
        string tag
    ) =>
        (
            await Coordinator.ActivateAsync(
                PluginLifecycleOperationId.New(),
                Package(version, tag),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();

    internal async ValueTask<(PluginId PluginId, PluginLifecycleFence Fence)> ActivateOtherAsync(
        string plugin,
        string version,
        string tag
    )
    {
        var package = Package(plugin, version, tag);
        var active = (
            await Coordinator.ActivateAsync(
                PluginLifecycleOperationId.New(),
                package,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();
        return (
            package.Installation.PluginId,
            new(active.View.OperationId, active.View.Generation)
        );
    }

    internal PluginLifecyclePackage Package(string version, string tag) =>
        Package(PluginId.Value, version, tag);

    internal PluginLifecyclePackage PackageFor(string plugin, string version, string tag) =>
        Package(plugin, version, tag);

    private static PluginLifecyclePackage Package(string plugin, string version, string tag)
    {
        var pluginId = Id(plugin);
        var installation = new PluginInstallationIdentity(
            pluginId,
            new(Version(version), Tag(tag))
        );
        var module = Module();
        var prepared = new PreparedPluginWorkerPackage(
            new(
                installation,
                PluginRuntimeIdentifier.LinuxX64,
                module,
                [new PluginWorkerLuaModule(module, "lua/main.lua")]
            ),
            Path.Combine(Path.GetTempPath(), "blokebot-lifecycle-tests", plugin, tag)
        );
        return new(
            installation,
            prepared,
            Path.Combine(Path.GetTempPath(), "blokebot-lifecycle-state", plugin),
            new ReturningTestDispatcher(new PluginValue.Nil()),
            NullLogger<PluginWorkerClient>.Instance
        );
    }

    private static PluginId Id(string value) =>
        PluginId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid test plugin ID.");

    private static SemanticVersion Version(string value) =>
        SemanticVersion.TryCreate(value, out var version)
            ? version
            : throw new InvalidOperationException("Invalid test version.");

    private static PluginGitTag Tag(string value) =>
        PluginGitTag.TryCreate(value, out var tag)
            ? tag
            : throw new InvalidOperationException("Invalid test tag.");

    private static PluginLuaModuleId Module() =>
        PluginLuaModuleId.TryCreate("main", out var module)
            ? module
            : throw new InvalidOperationException("Invalid test module.");
}

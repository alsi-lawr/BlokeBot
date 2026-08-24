using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginCheckpointWriteOutcome> WriteCheckpointAsync(
        PluginLifecycleState expected,
        PluginLifecycleState checkpoint,
        PluginAdmissionStopPublication publication,
        PluginCheckpointRollbackPolicy rollbackPolicy,
        CancellationToken cancellationToken
    )
    {
        PluginLifecycleStoreWriteOutcome written;
        try
        {
            written = await _store.WriteAsync(expected, checkpoint, cancellationToken);
        }
        catch (Exception writeException)
        {
            try
            {
                var current = await _store.LoadAsync(expected.PluginId, CancellationToken.None);
                var reconciled = await ReconcileObservedCheckpointAsync(
                    expected,
                    checkpoint,
                    publication,
                    current,
                    rollbackPolicy
                );
                if (reconciled is PluginCheckpointWriteOutcome.Committed reconciledCommitted)
                {
                    return reconciledCommitted with
                    {
                        Continuation = PluginCheckpointContinuation.LifecycleOwned,
                    };
                }
            }
            catch (Exception reconciliationException)
            {
                _logger.LogError(
                    reconciliationException,
                    "Plugin checkpoint exception reconciliation failed for {PluginId}.",
                    expected.PluginId.Value
                );
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(writeException).Throw();
            throw new InvalidOperationException("The checkpoint write exception was not rethrown.");
        }

        return written is PluginLifecycleStoreWriteOutcome.Written committed
            ? new PluginCheckpointWriteOutcome.Committed(
                committed.State,
                PluginCheckpointContinuation.CallerCancellation
            )
            : await ReconcileObservedCheckpointAsync(
                expected,
                checkpoint,
                publication,
                ((PluginLifecycleStoreWriteOutcome.Conflict)written).Current,
                rollbackPolicy
            );
    }

    private async ValueTask<PluginCheckpointWriteOutcome> ReconcileObservedCheckpointAsync(
        PluginLifecycleState expected,
        PluginLifecycleState checkpoint,
        PluginAdmissionStopPublication publication,
        PluginLifecycleState? current,
        PluginCheckpointRollbackPolicy rollbackPolicy
    )
    {
        if (current == checkpoint)
        {
            return new PluginCheckpointWriteOutcome.Committed(
                current,
                PluginCheckpointContinuation.LifecycleOwned
            );
        }

        if (
            rollbackPolicy == PluginCheckpointRollbackPolicy.RestoreLiveOriginal
            && RetainsOriginalRuntime(expected, current, publication)
        )
        {
            var restoration = _snapshots.TryRestoreOriginal(publication);
            if (restoration == PluginRuntimeRollbackOutcome.Restored)
            {
                if (publication.Original?.Worker is not { Termination.IsCompleted: true })
                {
                    return new PluginCheckpointWriteOutcome.Rejected(Conflict(current));
                }

                publication = _snapshots.StopAdmission(current!);
                restoration = PluginRuntimeRollbackOutcome.WorkerTerminated;
            }

            if (restoration == PluginRuntimeRollbackOutcome.WorkerTerminated)
            {
                return new PluginCheckpointWriteOutcome.Rejected(
                    await FaultTerminatedRollbackAsync(current!, publication.Ownership)
                );
            }
        }

        return new PluginCheckpointWriteOutcome.Rejected(
            await SettleAndPublishConflictAsync(checkpoint, publication.Ownership, current)
        );
    }

    private async ValueTask<PluginLifecycleCommandOutcome> FaultTerminatedRollbackAsync(
        PluginLifecycleState current,
        PluginRuntimeSlot? ownership
    )
    {
        if (current is { Phase: PluginLifecyclePhase.Preparing, ActiveRuntime: not null })
        {
            var restored = Applied(
                PluginLifecycleStateMachine.PreparationFailed(
                    current,
                    PluginLifecycleFailureCode.WorkerExited,
                    SafeDetail("The active plugin worker exited while lifecycle work was pending."),
                    Now()
                )
            );
            var written = await _store.WriteAsync(current, restored, CancellationToken.None);
            if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
            {
                return await SettleAndPublishConflictAsync(current, ownership, conflict.Current);
            }

            current = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        }

        return current is { Phase: PluginLifecyclePhase.Active, ActiveRuntime: not null }
            ? await FaultAsync(
                current,
                PluginLifecyclePhase.Active,
                PluginLifecycleFailureCode.WorkerExited,
                SafeDetail("The active plugin worker exited while lifecycle work was pending."),
                CancellationToken.None
            )
            : await SettleAndPublishConflictAsync(current, ownership, current);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> SettleAndPublishConflictAsync(
        PluginLifecycleState stopped,
        PluginRuntimeSlot? ownership,
        PluginLifecycleState? current
    )
    {
        _ = await StopRuntimeAsync(stopped, ownership, CancellationToken.None);
        return PublishConflict(stopped.PluginId, current);
    }

    private static bool RetainsOriginalRuntime(
        PluginLifecycleState expected,
        PluginLifecycleState? current,
        PluginAdmissionStopPublication publication
    ) =>
        current == expected
        || (
            current is { Phase: PluginLifecyclePhase.Active, ActiveRuntime: { } activeRuntime }
            && publication.Original
                is {
                    Entry.Phase: PluginLifecyclePhase.Active,
                    Entry: var entry,
                    RuntimeFence: { } runtimeFence,
                    Worker: not null,
                }
            && entry.Installation == current.SelectedInstallation
            && entry.Fence == current.SelectedFence
            && runtimeFence == entry.Fence
            && activeRuntime.Installation == entry.Installation
            && activeRuntime.Fence == runtimeFence
        );

    private PluginLifecycleCommandOutcome PublishConflict(
        PluginId pluginId,
        PluginLifecycleState? current
    )
    {
        if (current is null)
        {
            _ = _snapshots.Remove(pluginId);
        }
        else
        {
            _ = _snapshots.Publish(current, worker: null);
        }

        return Conflict(current);
    }

    private abstract record PluginCheckpointWriteOutcome
    {
        private PluginCheckpointWriteOutcome() { }

        internal sealed record Committed(
            PluginLifecycleState State,
            PluginCheckpointContinuation Continuation
        ) : PluginCheckpointWriteOutcome;

        internal sealed record Rejected(PluginLifecycleCommandOutcome Outcome)
            : PluginCheckpointWriteOutcome;
    }

    private enum PluginCheckpointRollbackPolicy
    {
        RestoreLiveOriginal,
        SettleRuntime,
    }

    private enum PluginCheckpointContinuation
    {
        CallerCancellation,
        LifecycleOwned,
    }
}

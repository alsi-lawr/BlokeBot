using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private void ObserveWorkerTermination(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        IPluginLifecycleWorkerSession worker
    ) => _ = MonitorWorkerTerminationAsync(state, package, worker);

    private async Task MonitorWorkerTerminationAsync(
        PluginLifecycleState activated,
        PluginLifecyclePackage package,
        IPluginLifecycleWorkerSession worker
    )
    {
        try
        {
            var failure = await worker.Termination;
            if (!_snapshots.IsCurrent(activated.PluginId, activated.SelectedFence, worker))
            {
                return;
            }

            PluginWorkerTerminationCheckpointOutcome handled;
            await using (
                await _serialization.AcquireAsync(activated.PluginId, CancellationToken.None)
            )
            {
                var current = await _store.LoadAsync(activated.PluginId, CancellationToken.None);
                if (
                    current is not { Phase: PluginLifecyclePhase.Active }
                    || current.SelectedFence != activated.SelectedFence
                )
                {
                    return;
                }

                handled = await ApplyWorkerTerminationAsync(current, failure);
            }

            if (handled is not PluginWorkerTerminationCheckpointOutcome.Replacement replacement)
            {
                return;
            }

            await DelayUntilAsync(
                replacement.State.RestartNotBeforeUtc!.Value,
                CancellationToken.None
            );
            await using (
                await _serialization.AcquireAsync(activated.PluginId, CancellationToken.None)
            )
            {
                var current = await _store.LoadAsync(activated.PluginId, CancellationToken.None);
                if (
                    current is not { Phase: PluginLifecyclePhase.Activating }
                    || current.SelectedFence != replacement.State.SelectedFence
                )
                {
                    return;
                }

                _ = await StartAndPublishAsync(
                    current,
                    package,
                    recovered: true,
                    CancellationToken.None
                );
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Plugin worker supervision failed for {PluginId}.",
                activated.PluginId.Value
            );
        }
    }

    private ValueTask<PluginWorkerTerminationCheckpointOutcome> ApplyWorkerTerminationAsync(
        PluginLifecycleState current,
        PluginWorkerFailure failure
    )
    {
        var now = Now();
        var transition = PluginLifecycleStateMachine.ApplyWorkerTermination(
            current,
            failure,
            now + _options.RestartBackoff,
            now
        );
        return transition.Match(
            replacementScheduled => PersistReplacementScheduleAsync(current, replacementScheduled),
            faultShutdownScheduled => PersistTerminationFaultAsync(current, faultShutdownScheduled),
            code =>
                ValueTask.FromResult<PluginWorkerTerminationCheckpointOutcome>(
                    new PluginWorkerTerminationCheckpointOutcome.Rejected(code)
                )
        );
    }

    private async ValueTask<PluginWorkerTerminationCheckpointOutcome> PersistReplacementScheduleAsync(
        PluginLifecycleState current,
        PluginLifecycleState scheduled
    )
    {
        var publication = _snapshots.StopAdmission(scheduled);
        var checkpoint = await WriteCheckpointAsync(
            current,
            scheduled,
            publication,
            PluginCheckpointRollbackPolicy.SettleRuntime,
            CancellationToken.None
        );
        if (checkpoint is PluginCheckpointWriteOutcome.Rejected rejected)
        {
            return new PluginWorkerTerminationCheckpointOutcome.Completed(rejected.Outcome);
        }

        var drain = await CancelDrainAndCheckpointAsync(
            ((PluginCheckpointWriteOutcome.Committed)checkpoint).State,
            publication.Ownership,
            CancellationToken.None
        );
        return drain is PluginRuntimeDrainOutcome.Ready ready
            ? new PluginWorkerTerminationCheckpointOutcome.Replacement(ready.State)
            : new PluginWorkerTerminationCheckpointOutcome.Completed(
                ((PluginRuntimeDrainOutcome.Failed)drain).Outcome
            );
    }

    private async ValueTask<PluginWorkerTerminationCheckpointOutcome> PersistTerminationFaultAsync(
        PluginLifecycleState current,
        PluginLifecycleState intent
    )
    {
        var publication = _snapshots.StopAdmission(intent);
        var checkpoint = await WriteCheckpointAsync(
            current,
            intent,
            publication,
            PluginCheckpointRollbackPolicy.SettleRuntime,
            CancellationToken.None
        );
        if (checkpoint is PluginCheckpointWriteOutcome.Rejected rejected)
        {
            return new PluginWorkerTerminationCheckpointOutcome.Completed(rejected.Outcome);
        }

        var committed = ((PluginCheckpointWriteOutcome.Committed)checkpoint).State;
        return new PluginWorkerTerminationCheckpointOutcome.Completed(
            await CompleteFaultShutdownAsync(
                committed,
                publication.Ownership,
                CancellationToken.None
            )
        );
    }

    private Task DelayUntilAsync(DateTimeOffset notBeforeUtc, CancellationToken cancellationToken)
    {
        var delay = notBeforeUtc - Now();
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, _timeProvider, cancellationToken);
    }

    private abstract record PluginWorkerTerminationCheckpointOutcome
    {
        private PluginWorkerTerminationCheckpointOutcome() { }

        internal sealed record Replacement(PluginLifecycleState State)
            : PluginWorkerTerminationCheckpointOutcome;

        internal sealed record Completed(PluginLifecycleCommandOutcome Outcome)
            : PluginWorkerTerminationCheckpointOutcome;

        internal sealed record Rejected(PluginLifecycleTransitionFailureCode Code)
            : PluginWorkerTerminationCheckpointOutcome;
    }
}

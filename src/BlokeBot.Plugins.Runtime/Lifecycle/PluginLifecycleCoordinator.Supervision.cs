using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private void ObserveUnexpectedExit(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        IPluginLifecycleWorkerSession worker
    ) => _ = MonitorUnexpectedExitAsync(state, package, worker);

    private async Task MonitorUnexpectedExitAsync(
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

            PluginLifecycleState? scheduled = null;
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

                if (
                    failure.Code != PluginWorkerFailureCode.WorkerTerminated
                    && current.AutomaticRestartConsumed
                )
                {
                    _ = await FaultAsync(
                        current,
                        PluginLifecyclePhase.Active,
                        PluginLifecycleFailureCode.WorkerExited,
                        SafeDetail("The admitted plugin worker exited again."),
                        CancellationToken.None
                    );
                    return;
                }

                var restartAt = Now() + _options.RestartBackoff;
                var transition =
                    failure.Code == PluginWorkerFailureCode.WorkerTerminated
                        ? PluginLifecycleStateMachine.ScheduleExpectedRestart(
                            current,
                            restartAt,
                            Now()
                        )
                        : PluginLifecycleStateMachine.ScheduleAutomaticRestart(
                            current,
                            restartAt,
                            Now()
                        );
                var next = Applied(transition);
                var written = await _store.WriteAsync(current, next, CancellationToken.None);
                if (written is not PluginLifecycleStoreWriteOutcome.Written restarted)
                {
                    return;
                }

                scheduled = restarted.State;
                _ = _snapshots.Publish(scheduled, worker: null);
                await worker.DisposeAsync();
            }

            await DelayUntilAsync(scheduled!.RestartNotBeforeUtc!.Value, CancellationToken.None);
            await using (
                await _serialization.AcquireAsync(activated.PluginId, CancellationToken.None)
            )
            {
                var current = await _store.LoadAsync(activated.PluginId, CancellationToken.None);
                if (
                    current is not { Phase: PluginLifecyclePhase.Activating }
                    || current.SelectedFence != scheduled.SelectedFence
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

    private Task DelayUntilAsync(DateTimeOffset notBeforeUtc, CancellationToken cancellationToken)
    {
        var delay = notBeforeUtc - Now();
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, _timeProvider, cancellationToken);
    }
}

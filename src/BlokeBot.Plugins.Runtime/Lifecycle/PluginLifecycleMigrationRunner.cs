using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal abstract record PluginLifecycleMigrationSessionOutcome
{
    private PluginLifecycleMigrationSessionOutcome() { }

    public sealed record Started(IPluginLifecycleMigrationSession Session)
        : PluginLifecycleMigrationSessionOutcome;

    public sealed record Failed(PluginWorkerFailure Failure)
        : PluginLifecycleMigrationSessionOutcome;
}

internal interface IPluginLifecycleMigrationSession : IAsyncDisposable
{
    ValueTask<PluginWorkerInvocationResult> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginMigrationDescriptor migration,
        PluginValue input,
        CancellationToken cancellationToken
    );
}

internal interface IPluginLifecycleMigrationRunner
{
    ValueTask<PluginLifecycleMigrationSessionOutcome> StartAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    );
}

internal sealed class PluginLifecycleMigrationRunner(PluginWorkerCoordinator workers)
    : IPluginLifecycleMigrationRunner
{
    public async ValueTask<PluginLifecycleMigrationSessionOutcome> StartAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        var started = await workers.StartAsync(
            new(
                package.PreparedPackage,
                package.StateRoot,
                PluginWorkerMode.Staging,
                package.HostCalls,
                package.WorkerLogger
            ),
            cancellationToken
        );
        return started switch
        {
            PluginWorkerReservationOutcome.Started worker =>
                new PluginLifecycleMigrationSessionOutcome.Started(new Session(worker.Lease)),
            PluginWorkerReservationOutcome.Rejected => Failed(
                "A plugin migration worker is already running."
            ),
            PluginWorkerReservationOutcome.StartFailed failure =>
                new PluginLifecycleMigrationSessionOutcome.Failed(
                    failure.Failure switch
                    {
                        PluginWorkerStartOutcome.Rejected rejected => new(
                            PluginWorkerFailureCode.ProtocolViolation,
                            $"Plugin migration worker rejected {rejected.Failure.Code}."
                        ),
                        PluginWorkerStartOutcome.Failed failed => failed.Failure,
                        _ => new(
                            PluginWorkerFailureCode.WorkerTerminated,
                            "Plugin migration worker did not start."
                        ),
                    }
                ),
            _ => Failed("Plugin migration worker did not start."),
        };
    }

    private static PluginLifecycleMigrationSessionOutcome.Failed Failed(string message) =>
        new(new(PluginWorkerFailureCode.WorkerTerminated, message));

    private sealed class Session(PluginWorkerLease lease) : IPluginLifecycleMigrationSession
    {
        public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginMigrationDescriptor migration,
            PluginValue input,
            CancellationToken cancellationToken
        ) =>
            PluginHostOperationId.TryCreate(migration.EntryPoint, out var operation)
                ? lease.Client.PrepareAsync(
                    identity,
                    new(migration.Module, operation, input),
                    cancellationToken
                )
                : ValueTask.FromResult(
                    new PluginWorkerInvocationResult(
                        new PluginWorkerInvocationOutcome.Failed(
                            new(
                                PluginWorkerFailureCode.ProtocolViolation,
                                "Plugin migration entry point is invalid."
                            )
                        ),
                        PluginWorkerInvocationMetrics.Empty,
                        []
                    )
                );

        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}

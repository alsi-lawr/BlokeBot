using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal abstract record PluginLifecycleWorkerStartOutcome
{
    private PluginLifecycleWorkerStartOutcome() { }

    internal sealed record Started(IPluginLifecycleWorkerSession Worker)
        : PluginLifecycleWorkerStartOutcome;

    internal sealed record Failed(
        PluginLifecycleFailureCode Code,
        PluginLifecycleSafeDetail? Detail
    ) : PluginLifecycleWorkerStartOutcome;
}

internal interface IPluginLifecycleWorkerSession : IAsyncDisposable
{
    PluginWorkerMode Mode { get; }

    Task<PluginWorkerFailure> Termination { get; }

    ValueTask<PluginWorkerInvocationResult> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginLiveInvocation invocation,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(
            new PluginWorkerInvocationResult(
                new PluginWorkerInvocationOutcome.Failed(
                    new(
                        PluginWorkerFailureCode.WorkerTerminated,
                        "Plugin worker invocation is unavailable."
                    )
                ),
                PluginWorkerInvocationMetrics.Empty,
                []
            )
        );
}

internal interface IPluginLifecycleWorkerManager
{
    ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    );
}

internal sealed class PluginLifecycleWorkerManager(PluginWorkerCoordinator coordinator)
    : IPluginLifecycleWorkerManager
{
    public async ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        var started = await StartAsync(package, PluginWorkerMode.Staging, cancellationToken);
        if (started is PluginLifecycleWorkerStartOutcome.Started validated)
        {
            await validated.Worker.DisposeAsync();
        }

        return started;
    }

    public ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    ) => StartAsync(package, PluginWorkerMode.Admitted, cancellationToken);

    private async ValueTask<PluginLifecycleWorkerStartOutcome> StartAsync(
        PluginLifecyclePackage package,
        PluginWorkerMode mode,
        CancellationToken cancellationToken
    )
    {
        if (!package.MatchesIdentity)
        {
            return Failed(
                PluginLifecycleFailureCode.PreparationRejected,
                "Package identity does not match the lifecycle selection."
            );
        }

        var outcome = await coordinator.StartAsync(
            new(
                package.PreparedPackage,
                package.StateRoot,
                mode,
                package.HostCalls,
                package.WorkerLogger
            ),
            cancellationToken
        );
        return outcome is PluginWorkerReservationOutcome.Started started
                ? new PluginLifecycleWorkerStartOutcome.Started(new Session(started.Lease))
            : outcome is PluginWorkerReservationOutcome.Rejected rejected
                ? Failed(
                    PluginLifecycleFailureCode.WorkerStartFailed,
                    rejected.Failure.Code == PluginWorkerReservationFailureCode.AdmittedWorkerExists
                        ? "An admitted worker already exists."
                        : "A staging worker already exists."
                )
            : Map(((PluginWorkerReservationOutcome.StartFailed)outcome).Failure);
    }

    private static PluginLifecycleWorkerStartOutcome Map(PluginWorkerStartOutcome failure) =>
        failure is PluginWorkerStartOutcome.Rejected rejected
            ? Failed(
                PluginLifecycleFailureCode.PreparationRejected,
                $"Worker handshake rejected {rejected.Failure.Code}."
            )
        : failure is PluginWorkerStartOutcome.Failed failed
            ? Failed(
                PluginLifecycleFailureCode.WorkerStartFailed,
                $"Worker start failed with {failed.Failure.Code}."
            )
        : throw new InvalidOperationException("A started worker was reported as a start failure.");

    private static PluginLifecycleWorkerStartOutcome.Failed Failed(
        PluginLifecycleFailureCode code,
        string detail
    ) =>
        new(
            code,
            PluginLifecycleSafeDetail.TryCreate(detail, out var safeDetail) ? safeDetail : null
        );

    private sealed class Session(PluginWorkerLease lease) : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => lease.Mode;

        public Task<PluginWorkerFailure> Termination => lease.Client.Termination;

        public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        ) => lease.Client.InvokeAsync(identity, invocation, cancellationToken);

        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}

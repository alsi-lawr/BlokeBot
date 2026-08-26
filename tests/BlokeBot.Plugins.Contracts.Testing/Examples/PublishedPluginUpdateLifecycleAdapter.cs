using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PublishedPluginUpdateLifecycleAdapter
{
    internal static async ValueTask<PublishedPluginExampleScenarioExecution> RunAsync(
        PreparedPluginWorkerPackage updatePackage,
        PublishedPluginExampleScenario scenario,
        PluginWorkerExecutable workerExecutable,
        string stateRoot,
        CancellationToken cancellationToken
    )
    {
        var store = new IsolatedLifecycleStore();
        var packages = new IsolatedPackageResolver();
        var workers = new IsolatedLifecycleWorkers();
        var snapshots = new PluginRuntimeSnapshotRegistry();
        var migration = new UpdateFailureMigrationOwner(scenario, workerExecutable, stateRoot);
        var coordinator = new PluginLifecycleCoordinator(
            store,
            packages,
            [migration],
            [],
            [],
            new IsolatedPendingWorkCanceller(),
            workers,
            snapshots,
            new PluginLifecycleSerialization(),
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            TimeProvider.System,
            NullLogger<PluginLifecycleCoordinator>.Instance
        );
        var old = LifecyclePackage(OldPackage(updatePackage), Path.Combine(stateRoot, "old"));
        var update = LifecyclePackage(updatePackage, Path.Combine(stateRoot, "update"));
        packages.Add(update);

        var activated = await coordinator.ActivateAsync(
            PluginLifecycleOperationId.New(),
            old,
            cancellationToken
        );
        if (activated is not PluginLifecycleCommandOutcome.Succeeded oldActive)
        {
            return Failed("old activation", activated.GetType().Name);
        }

        var oldFence = new PluginLifecycleFence(
            oldActive.View.OperationId,
            oldActive.View.Generation
        );
        var replacement = await coordinator.ReplaceAsync(
            PluginLifecycleOperationId.New(),
            update,
            cancellationToken
        );
        if (
            replacement
                is not PluginLifecycleCommandOutcome.Failed
                {
                    View:
                    {
                        Phase: PluginLifecyclePhase.Faulted,
                        LatestOutcome.FailureCode: PluginLifecycleFailureCode.MigrationFailed,
                    },
                }
            || migration.ObservedEngineFailures != 1
            || !workers.FirstAdmittedDisposed
            || snapshots.Admit(
                update.Installation.PluginId,
                oldFence,
                PluginFeatureAdmissionReadiness.Ready
            )
                is not PluginAdmissionOutcome.Rejected
        )
        {
            return Failed("replace", replacement.GetType().Name);
        }

        var recovery = await coordinator.RestartAsync(
            update.Installation.PluginId,
            PluginLifecycleOperationId.New(),
            cancellationToken
        );
        return
            recovery
                is PluginLifecycleCommandOutcome.Failed
                {
                    View:
                    {
                        Phase: PluginLifecyclePhase.Faulted,
                        LatestOutcome.FailureCode: PluginLifecycleFailureCode.MigrationFailed,
                    },
                }
            && migration.Calls == 2
            && migration.ObservedEngineFailures == 2
            && !workers.StartedInstallations.Skip(1).Any()
            ? PublishedPluginExampleScenarioExecution.UpdateFailurePassed()
            : Failed("recovery", recovery.GetType().Name);
    }

    private static PublishedPluginExampleScenarioExecution Failed(string stage, string outcome) =>
        PublishedPluginExampleScenarioExecution.Failed(
            PublishedPluginExampleFailureCode.InvocationExpectationMismatch,
            stage,
            outcome
        );

    private static PluginLifecyclePackage LifecyclePackage(
        PreparedPluginWorkerPackage package,
        string stateRoot
    ) =>
        new(
            package.Descriptor.Plugin,
            PluginPackageOperationId.New(),
            package,
            stateRoot,
            new PublishedPluginExampleHost(delayFirstCall: false),
            NullLogger<PluginWorkerClient>.Instance
        );

    private static PreparedPluginWorkerPackage OldPackage(PreparedPluginWorkerPackage update)
    {
        _ = SemanticVersion.TryCreate("1.0.0", out var version);
        _ = PluginGitTag.TryCreate("examples-update-failure-v1", out var tag);
        var descriptor = update.Descriptor with
        {
            Plugin = new(update.Descriptor.Plugin.PluginId, new(version, tag)),
        };
        return new(descriptor, update.PackageRoot);
    }

    private sealed class UpdateFailureMigrationOwner(
        PublishedPluginExampleScenario scenario,
        PluginWorkerExecutable workerExecutable,
        string stateRoot
    ) : IPluginMigrationDataOwner
    {
        internal int Calls { get; private set; }

        internal int ObservedEngineFailures { get; private set; }

        public async ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
            PluginMigrationContext context,
            CancellationToken cancellationToken
        )
        {
            if (context.Installation.Release.DeclaredVersion.Value == "1.0.0")
            {
                return new PluginLifecycleOwnerOutcome.Succeeded();
            }

            Calls++;
            var package = context.Package!.PreparedPackage;
            var started = await PluginWorkerClient.StartAsync(
                new(
                    package,
                    Path.Combine(stateRoot, $"migration-{Calls}"),
                    PluginWorkerMode.Staging,
                    new PublishedPluginExampleHost(delayFirstCall: false),
                    NullLogger<PluginWorkerClient>.Instance,
                    workerExecutable
                ),
                cancellationToken
            );
            if (started is not PluginWorkerStartOutcome.Started worker)
            {
                return new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Unavailable,
                    null
                );
            }

            await using var client = worker.Client;
            var result = await client.PrepareAsync(
                PublishedPluginExampleInvocationFactory.Identity(
                    package,
                    PublishedPluginExampleInvocationKind.Migration
                ),
                new(scenario.Module, scenario.Operation, new PluginValue.Nil()),
                cancellationToken
            );
            var engineFailure =
                result.Outcome is PluginWorkerInvocationOutcome.Failed
                {
                    Failure.Code: PluginWorkerFailureCode.EngineFailure,
                };
            if (engineFailure)
            {
                ObservedEngineFailures++;
            }

            return engineFailure
                ? new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Failed,
                    null
                )
                : new PluginLifecycleOwnerOutcome.Succeeded();
        }
    }

    private sealed class IsolatedLifecycleWorkers : IPluginLifecycleWorkerManager
    {
        private readonly List<IsolatedLifecycleWorker> _admitted = [];

        internal bool FirstAdmittedDisposed => _admitted.FirstOrDefault()?.Disposed is true;

        internal IReadOnlyList<PluginInstallationIdentity> StartedInstallations =>
            _admitted.Select(worker => worker.Installation).ToArray();

        public ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecycleWorkerStartOutcome>(
                new PluginLifecycleWorkerStartOutcome.Started(
                    new IsolatedLifecycleWorker(package.Installation, PluginWorkerMode.Staging)
                )
            );

        public ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        )
        {
            var worker = new IsolatedLifecycleWorker(
                package.Installation,
                PluginWorkerMode.Admitted
            );
            _admitted.Add(worker);
            return ValueTask.FromResult<PluginLifecycleWorkerStartOutcome>(
                new PluginLifecycleWorkerStartOutcome.Started(worker)
            );
        }
    }

    private sealed class IsolatedLifecycleWorker(
        PluginInstallationIdentity installation,
        PluginWorkerMode mode
    ) : IPluginLifecycleWorkerSession
    {
        private readonly TaskCompletionSource<PluginWorkerFailure> _termination = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal PluginInstallationIdentity Installation { get; } = installation;

        internal bool Disposed { get; private set; }

        public PluginWorkerMode Mode { get; } = mode;

        public Task<PluginWorkerFailure> Termination => _termination.Task;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IsolatedPendingWorkCanceller : IPluginPendingWorkCanceller
    {
        public ValueTask<PluginLifecycleOwnerOutcome> CancelAsync(
            PluginId pluginId,
            PluginLifecycleFence fence,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Succeeded()
            );
    }

    private sealed class IsolatedPackageResolver : IPluginLifecyclePackageResolver
    {
        private readonly Dictionary<
            (PluginInstallationIdentity, PluginPackageOperationId),
            PluginLifecyclePackage
        > _packages = [];

        internal void Add(PluginLifecyclePackage package) =>
            _packages[(package.Installation, package.PackageOperationId)] = package;

        public ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
            PluginInstallationIdentity installation,
            PluginPackageOperationId packageOperationId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecyclePackageResolution>(
                _packages.TryGetValue((installation, packageOperationId), out var package)
                    ? new PluginLifecyclePackageResolution.Available(package)
                    : new PluginLifecyclePackageResolution.Unavailable()
            );
    }

    private sealed class IsolatedLifecycleStore : IPluginLifecycleStore
    {
        private PluginLifecycleState? _state;

        public ValueTask<PluginLifecycleState?> LoadAsync(
            PluginId pluginId,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(_state?.PluginId == pluginId ? _state : null);

        public ValueTask<IReadOnlyList<PluginLifecycleState>> LoadAllAsync(
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<IReadOnlyList<PluginLifecycleState>>(
                _state is null ? [] : [_state]
            );

        public ValueTask<PluginLifecycleStoreBeginOutcome> BeginActivationAsync(
            PluginLifecycleBeginRequest request,
            CancellationToken cancellationToken
        ) => BeginAsync(request, replace: false);

        public ValueTask<PluginLifecycleStoreBeginOutcome> BeginReplacementAsync(
            PluginLifecycleBeginRequest request,
            CancellationToken cancellationToken
        ) => BeginAsync(request, replace: true);

        public ValueTask<PluginLifecycleStoreWriteOutcome> WriteAsync(
            PluginLifecycleState expected,
            PluginLifecycleState next,
            CancellationToken cancellationToken
        )
        {
            if (_state != expected || !PluginLifecycleStateMachine.HasValidFaultInvariant(next))
            {
                return ValueTask.FromResult<PluginLifecycleStoreWriteOutcome>(
                    new PluginLifecycleStoreWriteOutcome.Conflict(_state)
                );
            }

            _state = next;
            return ValueTask.FromResult<PluginLifecycleStoreWriteOutcome>(
                new PluginLifecycleStoreWriteOutcome.Written(next)
            );
        }

        public ValueTask<PluginLifecycleStoreRemovalOutcome> CompleteRemovalAsync(
            PluginLifecycleState expected,
            PluginLifecycleOutcome outcome,
            CancellationToken cancellationToken
        )
        {
            if (_state != expected)
            {
                return ValueTask.FromResult<PluginLifecycleStoreRemovalOutcome>(
                    new PluginLifecycleStoreRemovalOutcome.Conflict(_state)
                );
            }

            _state = null;
            return ValueTask.FromResult<PluginLifecycleStoreRemovalOutcome>(
                new PluginLifecycleStoreRemovalOutcome.Completed(expected.PluginId)
            );
        }

        private ValueTask<PluginLifecycleStoreBeginOutcome> BeginAsync(
            PluginLifecycleBeginRequest request,
            bool replace
        )
        {
            var transition = replace
                ? PluginLifecycleStateMachine.BeginReplacement(
                    _state,
                    request.Installation,
                    request.PackageOperationId,
                    request.OperationId,
                    request.OccurredAtUtc
                )
                : PluginLifecycleStateMachine.BeginActivation(
                    _state,
                    request.Installation,
                    request.PackageOperationId,
                    request.OperationId,
                    request.OccurredAtUtc
                );
            if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
            {
                return ValueTask.FromResult<PluginLifecycleStoreBeginOutcome>(
                    new PluginLifecycleStoreBeginOutcome.Rejected(rejected.Code, _state)
                );
            }

            _state = ((PluginLifecycleTransitionOutcome.Applied)transition).State;
            return ValueTask.FromResult<PluginLifecycleStoreBeginOutcome>(
                new PluginLifecycleStoreBeginOutcome.Begun(_state)
            );
        }
    }
}

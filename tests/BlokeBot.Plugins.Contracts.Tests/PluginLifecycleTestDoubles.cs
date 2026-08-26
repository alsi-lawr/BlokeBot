using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Tests;

internal sealed class InMemoryLifecycleStore : IPluginLifecycleStore
{
    private readonly object _sync = new();
    private readonly Dictionary<PluginId, PluginLifecycleState> _states = [];
    private TaskCompletionSource? _writeGate;

    internal int Count
    {
        get
        {
            lock (_sync)
            {
                return _states.Count;
            }
        }
    }

    internal TaskCompletionSource WriteStarted { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Exception? ExceptionAfterNextWrite { get; set; }

    internal Exception? ExceptionBeforeNextWrite { get; set; }

    internal CancellationTokenSource? CancellationAfterNextWrite { get; set; }

    internal bool ConflictAfterNextWrite { get; set; }

    internal List<PluginLifecycleState> WrittenStates { get; } = [];

    internal void PauseNextWrite()
    {
        WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _writeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal void ResumeWrite() => _writeGate?.TrySetResult();

    public ValueTask<PluginLifecycleState?> LoadAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            _ = _states.TryGetValue(pluginId, out var state);
            return ValueTask.FromResult(state);
        }
    }

    public ValueTask<IReadOnlyList<PluginLifecycleState>> LoadAllAsync(
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            return ValueTask.FromResult<IReadOnlyList<PluginLifecycleState>>(
                _states.Values.ToArray()
            );
        }
    }

    public ValueTask<PluginLifecycleStoreBeginOutcome> BeginActivationAsync(
        PluginLifecycleBeginRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            _ = _states.TryGetValue(request.Installation.PluginId, out var current);
            var transition = PluginLifecycleStateMachine.BeginActivation(
                current,
                request.Installation,
                request.PackageOperationId,
                request.OperationId,
                request.OccurredAtUtc
            );
            if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
            {
                return ValueTask.FromResult<PluginLifecycleStoreBeginOutcome>(
                    new PluginLifecycleStoreBeginOutcome.Rejected(rejected.Code, current)
                );
            }

            var begun = ((PluginLifecycleTransitionOutcome.Applied)transition).State;
            _states[begun.PluginId] = begun;
            return ValueTask.FromResult<PluginLifecycleStoreBeginOutcome>(
                new PluginLifecycleStoreBeginOutcome.Begun(begun)
            );
        }
    }

    public ValueTask<PluginLifecycleStoreBeginOutcome> BeginReplacementAsync(
        PluginLifecycleBeginRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            _ = _states.TryGetValue(request.Installation.PluginId, out var current);
            var transition = PluginLifecycleStateMachine.BeginReplacement(
                current,
                request.Installation,
                request.PackageOperationId,
                request.OperationId,
                request.OccurredAtUtc
            );
            if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
            {
                return ValueTask.FromResult<PluginLifecycleStoreBeginOutcome>(
                    new PluginLifecycleStoreBeginOutcome.Rejected(rejected.Code, current)
                );
            }

            var begun = ((PluginLifecycleTransitionOutcome.Applied)transition).State;
            _states[begun.PluginId] = begun;
            return ValueTask.FromResult<PluginLifecycleStoreBeginOutcome>(
                new PluginLifecycleStoreBeginOutcome.Begun(begun)
            );
        }
    }

    public ValueTask<PluginLifecycleStoreRemovalOutcome> CompleteRemovalAsync(
        PluginLifecycleState expected,
        PluginLifecycleOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(expected.PluginId, out var current))
            {
                return ValueTask.FromResult<PluginLifecycleStoreRemovalOutcome>(
                    new PluginLifecycleStoreRemovalOutcome.Completed(expected.PluginId)
                );
            }

            if (
                current != expected
                || expected.Phase != PluginLifecyclePhase.Removing
                || outcome is not { Code: PluginLifecycleOutcomeCode.Removed, FailureCode: null }
            )
            {
                return ValueTask.FromResult<PluginLifecycleStoreRemovalOutcome>(
                    new PluginLifecycleStoreRemovalOutcome.Conflict(current)
                );
            }

            _ = _states.Remove(expected.PluginId);
            return ValueTask.FromResult<PluginLifecycleStoreRemovalOutcome>(
                new PluginLifecycleStoreRemovalOutcome.Completed(expected.PluginId)
            );
        }
    }

    public async ValueTask<PluginLifecycleStoreWriteOutcome> WriteAsync(
        PluginLifecycleState expected,
        PluginLifecycleState next,
        CancellationToken cancellationToken
    )
    {
        if (_writeGate is { } gate)
        {
            _ = WriteStarted.TrySetResult();
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _writeGate = null;
            }
        }

        if (ExceptionBeforeNextWrite is { } beforeWrite)
        {
            ExceptionBeforeNextWrite = null;
            throw beforeWrite;
        }

        PluginLifecycleStoreWriteOutcome outcome;
        Exception? exception;
        CancellationTokenSource? cancellation;
        bool conflict;
        lock (_sync)
        {
            if (
                !_states.TryGetValue(expected.PluginId, out var current)
                || current != expected
                || !PluginLifecycleStateMachine.HasValidFaultInvariant(next)
            )
            {
                return new PluginLifecycleStoreWriteOutcome.Conflict(current);
            }

            _states[expected.PluginId] = next;
            WrittenStates.Add(next);
            outcome = new PluginLifecycleStoreWriteOutcome.Written(next);
            exception = ExceptionAfterNextWrite;
            ExceptionAfterNextWrite = null;
            cancellation = CancellationAfterNextWrite;
            CancellationAfterNextWrite = null;
            conflict = ConflictAfterNextWrite;
            ConflictAfterNextWrite = false;
        }

        cancellation?.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return exception is not null ? throw exception
            : conflict ? new PluginLifecycleStoreWriteOutcome.Conflict(next)
            : outcome;
    }

    internal void Seed(PluginLifecycleState state)
    {
        lock (_sync)
        {
            _states[state.PluginId] = state;
        }
    }

    internal async Task<PluginLifecycleState> WaitForAsync(
        PluginId pluginId,
        Func<PluginLifecycleState, bool> predicate
    )
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await LoadAsync(pluginId, CancellationToken.None);
            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Lifecycle state did not reach the expected condition.");
    }
}

internal sealed class FakePackageResolver : IPluginLifecyclePackageResolver
{
    private readonly Dictionary<
        (PluginInstallationIdentity Installation, PluginPackageOperationId PackageOperationId),
        PluginLifecyclePackage
    > _packages = [];

    internal void Add(PluginLifecyclePackage package) =>
        _packages[(package.Installation, package.PackageOperationId)] = package;

    internal List<(
        PluginInstallationIdentity Installation,
        PluginPackageOperationId PackageOperationId
    )> Resolutions { get; } = [];

    public ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    )
    {
        Resolutions.Add((installation, packageOperationId));
        return ValueTask.FromResult<PluginLifecyclePackageResolution>(
            _packages.TryGetValue((installation, packageOperationId), out var package)
                ? new PluginLifecyclePackageResolution.Available(package)
                : new PluginLifecyclePackageResolution.Unavailable()
        );
    }
}

internal sealed class FakeLifecycleWorkers : IPluginLifecycleWorkerManager
{
    private TaskCompletionSource? _validationGate;

    internal List<FakeLifecycleWorkerSession> Admitted { get; } = [];

    internal List<PluginInstallationIdentity> StartedInstallations { get; } = [];

    internal List<PluginLifecyclePackage> StartedPackages { get; } = [];

    internal int AdmittedFailuresRemaining { get; set; }

    internal Exception? NextDisposalException { get; set; }

    internal Action? BeforeStartAdmitted { get; set; }

    internal TaskCompletionSource ValidationStarted { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PluginLifecycleWorkerStartOutcome.Failed? ValidationFailure { get; set; }

    internal void PauseValidation()
    {
        ValidationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _validationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal void ResumeValidation() => _validationGate?.TrySetResult();

    public async ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        _ = ValidationStarted.TrySetResult();
        if (_validationGate is not null)
        {
            await _validationGate.Task.WaitAsync(cancellationToken);
            _validationGate = null;
        }

        return ValidationFailure is null
            ? new PluginLifecycleWorkerStartOutcome.Started(
                new FakeLifecycleWorkerSession(PluginWorkerMode.Staging)
            )
            : ValidationFailure;
    }

    public ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        BeforeStartAdmitted?.Invoke();
        if (AdmittedFailuresRemaining > 0)
        {
            AdmittedFailuresRemaining--;
            return ValueTask.FromResult<PluginLifecycleWorkerStartOutcome>(
                new PluginLifecycleWorkerStartOutcome.Failed(
                    PluginLifecycleFailureCode.WorkerStartFailed,
                    null
                )
            );
        }

        var worker = new FakeLifecycleWorkerSession(
            PluginWorkerMode.Admitted,
            NextDisposalException
        );
        NextDisposalException = null;
        Admitted.Add(worker);
        StartedInstallations.Add(package.Installation);
        StartedPackages.Add(package);
        return ValueTask.FromResult<PluginLifecycleWorkerStartOutcome>(
            new PluginLifecycleWorkerStartOutcome.Started(worker)
        );
    }
}

internal sealed class FakeLifecycleWorkerSession(
    PluginWorkerMode mode,
    Exception? disposalException = null
) : IPluginLifecycleWorkerSession
{
    private readonly TaskCompletionSource<PluginWorkerFailure> _termination = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _disposeCalls;

    internal TaskCompletionSource Disposed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public PluginWorkerMode Mode { get; } = mode;

    public Task<PluginWorkerFailure> Termination => _termination.Task;

    internal void Exit(PluginWorkerFailureCode code) =>
        _termination.TrySetResult(new(code, "Test worker exit."));

    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Increment(ref _disposeCalls);
        _ = Disposed.TrySetResult();
        return disposalException is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(disposalException);
    }
}

internal sealed class RecordingMigrationOwner : IPluginMigrationDataOwner
{
    internal int Calls { get; private set; }

    internal int FailuresRemaining { get; set; }

    public ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
        PluginMigrationContext context,
        CancellationToken cancellationToken
    )
    {
        Calls++;
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Failed(PluginLifecycleOwnerFailureCode.Failed, null)
            );
        }

        return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
            new PluginLifecycleOwnerOutcome.Succeeded()
        );
    }
}

internal sealed class RecordingActivationPublisher : IPluginLifecycleActivationPublisher
{
    internal List<PluginLifecycleActivationContext> Published { get; } = [];

    internal List<PluginLifecycleActivationContext> Withdrawn { get; } = [];

    internal int FailuresRemaining { get; set; }

    public ValueTask<PluginLifecycleOwnerOutcome> PublishAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    )
    {
        Published.Add(context);
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Failed(PluginLifecycleOwnerFailureCode.Failed, null)
            );
        }

        return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
            new PluginLifecycleOwnerOutcome.Succeeded()
        );
    }

    public ValueTask WithdrawAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    )
    {
        Withdrawn.Add(context);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ThrowingMigrationOwner : IPluginMigrationDataOwner
{
    public ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
        PluginMigrationContext context,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("raw-secret exception text");
}

internal sealed class RecordingRemovalOwner : IPluginRemovalDataOwner
{
    private TaskCompletionSource? _gate;

    internal int Calls { get; private set; }

    internal List<PluginRemovalContext> Contexts { get; } = [];

    internal int FailuresRemaining { get; set; }

    internal TaskCompletionSource Started { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Pause()
    {
        Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal void Resume() => _gate?.TrySetResult();

    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        Calls++;
        Contexts.Add(context);
        _ = Started.TrySetResult();
        if (_gate is not null)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            _gate = null;
        }

        var failed = FailuresRemaining > 0;
        FailuresRemaining = Math.Max(0, FailuresRemaining - 1);
        return failed
            ? new PluginLifecycleOwnerOutcome.Failed(PluginLifecycleOwnerFailureCode.Failed, null)
            : new PluginLifecycleOwnerOutcome.Succeeded();
    }
}

internal sealed class RecordingPendingWorkCanceller : IPluginPendingWorkCanceller
{
    private TaskCompletionSource? _gate;

    internal int Calls { get; private set; }

    internal List<PluginLifecycleFence> CancelledFences { get; } = [];

    internal int FailuresRemaining { get; set; }

    internal Exception? ExceptionToThrow { get; set; }

    internal TaskCompletionSource Started { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Pause()
    {
        Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal void Resume() => _gate?.TrySetResult();

    internal async Task WaitForCallsAsync(int expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Calls >= expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Pending work cancellation was not observed.");
    }

    public async ValueTask<PluginLifecycleOwnerOutcome> CancelAsync(
        PluginId pluginId,
        PluginLifecycleFence fence,
        CancellationToken cancellationToken
    )
    {
        Calls++;
        CancelledFences.Add(fence);
        _ = Started.TrySetResult();
        if (_gate is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _gate = null;
            }
        }

        if (ExceptionToThrow is { } exception)
        {
            ExceptionToThrow = null;
            throw exception;
        }

        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            return new PluginLifecycleOwnerOutcome.Failed(
                PluginLifecycleOwnerFailureCode.Failed,
                null
            );
        }

        return new PluginLifecycleOwnerOutcome.Succeeded();
    }
}

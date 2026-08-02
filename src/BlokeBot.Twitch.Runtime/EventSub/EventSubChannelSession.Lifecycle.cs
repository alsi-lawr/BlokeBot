using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession(
    string sessionId,
    IEventSubChannelOperations operations,
    EventSubChannelRecoveryPipeline recovery,
    EventSubSubscriptionReconciliationStore pendingDeletions,
    EventSubChannelStatusStore.EventSubChannelStatusScope statusScope,
    BotRuntimeStatusStore runtimeStatus,
    IEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveEventSubSubscription> _subscriptions = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, EventSubChannelStatus> _states = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, EventSubChannelFailureContext> _failures = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> _authorizedChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _sessionStop = new();
    private CancellationTokenSource? _lifetime;
    private Task _currentWork = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    internal IReadOnlyList<string> ActiveChannels
    {
        get
        {
            string[] active;
            lock (_gate)
            {
                active = _subscriptions.Keys.ToArray();
            }

            return active
                .Union(pendingDeletions.PendingDeletionChannels, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    internal void Start(IReadOnlyList<string> desiredChannels, CancellationToken cancellationToken)
    {
        var desired = BotChannelList.Normalize(desiredChannels);
        var desiredSet = desired.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var initial = desired
            .Union(pendingDeletions.ReconciliationChannels, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException(
                    "EventSub channel recovery has already started for this session."
                );
            }

            _started = true;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                _sessionStop.Token,
                cancellationToken
            );
        }

        statusScope.Activate();
        runtimeStatus.ActivateEventSubScope(statusScope.Id);
        lock (_gate)
        {
            UpdateRuntimeStatusLocked();
            ScheduleLocked(token =>
                Task.WhenAll(
                    initial.Select(channel =>
                        RunImmediateAsync(
                            channel,
                            desiredSet.Contains(channel)
                                ? EventSubChannelReconciliationTarget.Present
                                : EventSubChannelReconciliationTarget.Absent,
                            EventSubChannelRecoveryTrigger.Startup,
                            token
                        )
                    )
                )
            );
        }
    }

    internal void TriggerReconciliation(
        IReadOnlyList<string> desiredChannels,
        EventSubChannelRecoveryTrigger trigger
    )
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                throw new InvalidOperationException(
                    "EventSub channel recovery must start before reconciliation is triggered."
                );
            }

            if (!_currentWork.IsCompleted)
            {
                return;
            }

            _currentWork.GetAwaiter().GetResult();
            var desired = BotChannelList.Normalize(desiredChannels);
            ScheduleLocked(token => RunReconciliationAsync(desired, trigger, token));
        }
    }

    internal async Task DrainAsync()
    {
        Task work;
        lock (_gate)
        {
            work = _currentWork;
        }

        await work;
    }

    internal async Task TriggerReconciliationAndDrainAsync(
        IReadOnlyList<string> desiredChannels,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var desired = BotChannelList.Normalize(desiredChannels);
        while (true)
        {
            Task work;
            var scheduled = false;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_started)
                {
                    throw new InvalidOperationException(
                        "EventSub channel recovery must start before reconciliation is triggered."
                    );
                }

                if (_currentWork.IsCompleted)
                {
                    _currentWork.GetAwaiter().GetResult();
                    ScheduleLocked(token => RunReconciliationAsync(desired, trigger, token));
                    scheduled = true;
                }

                work = _currentWork;
            }

            await work.WaitAsync(cancellationToken);
            if (scheduled)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task work;
        CancellationTokenSource? linkedLifetime;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            work = _currentWork;
            linkedLifetime = _lifetime;
        }

        Exception? failure = null;
        try
        {
            _sessionStop.Cancel();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await work;
        }
        catch (OperationCanceledException) when (_sessionStop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            runtimeStatus.DeactivateEventSubScope(statusScope.Id);
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            statusScope.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            linkedLifetime?.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            _sessionStop.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void ScheduleLocked(Func<CancellationToken, Task> operation)
    {
        var token =
            _lifetime?.Token
            ?? throw new InvalidOperationException(
                "EventSub channel recovery does not have a session lifetime."
            );
        _currentWork = Task.Run(() => operation(token), CancellationToken.None);
    }

    private static Exception CombineCleanupFailures(Exception? previous, Exception current) =>
        previous is null
            ? current
            : new AggregateException("EventSub channel session cleanup failed.", previous, current);
}

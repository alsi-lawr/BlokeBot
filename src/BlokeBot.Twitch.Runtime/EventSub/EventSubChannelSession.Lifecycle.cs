using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession(
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

    // Desired channels and the remote subscription inventory are loaded inside the scheduled
    // slot: a snapshot taken before queueing goes stale behind in-flight work and tears down
    // subscriptions that work just created.
    internal async Task TriggerReconciliationAndDrainAsync(
        Func<CancellationToken, ValueTask<IReadOnlyList<string>>> loadDesiredChannels,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    ) =>
        await ScheduleAndDrainAsync(
            async token =>
                await RunReconciliationAsync(
                    BotChannelList.Normalize(await loadDesiredChannels(token)),
                    trigger,
                    token
                ),
            cancellationToken
        );

    internal async Task RepairMissingSubscriptionsAndDrainAsync(
        Func<CancellationToken, Task<IReadOnlySet<string>>> listEnabledRemoteSubscriptionIds,
        Func<CancellationToken, ValueTask<IReadOnlyList<string>>> loadDesiredChannels,
        CancellationToken cancellationToken
    ) =>
        await ScheduleAndDrainAsync(
            async token =>
                await RepairMissingSubscriptionsAsync(
                    await listEnabledRemoteSubscriptionIds(token),
                    BotChannelList.Normalize(await loadDesiredChannels(token)),
                    token
                ),
            cancellationToken
        );

    internal async Task RepairRevokedSubscriptionAndDrainAsync(
        string subscriptionId,
        Func<CancellationToken, ValueTask<IReadOnlyList<string>>> loadDesiredChannels,
        CancellationToken cancellationToken
    ) =>
        await ScheduleAndDrainAsync(
            async token =>
                await RepairSubscriptionsAsync(
                    FindChannels(subscription =>
                        ContainsSubscription(subscription, subscriptionId)
                    ),
                    BotChannelList.Normalize(await loadDesiredChannels(token)),
                    EventSubChannelRecoveryTrigger.Explicit,
                    token
                ),
            cancellationToken
        );

    private async Task ScheduleAndDrainAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken
    )
    {
        // The scheduled slot must never fault: a stored fault would poison _currentWork and
        // block every later reconciliation, so the outcome travels back through the completion.
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        async Task GuardedAsync(CancellationToken token)
        {
            try
            {
                await work(token);
                _ = completion.TrySetResult();
            }
            catch (Exception exception)
            {
                _ = completion.TrySetException(exception);
            }
        }

        while (true)
        {
            Task currentWork;
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
                    ScheduleLocked(GuardedAsync);
                    scheduled = true;
                }

                currentWork = _currentWork;
            }

            await currentWork.WaitAsync(cancellationToken);
            if (scheduled)
            {
                await completion.Task;
                return;
            }
        }
    }

    private async Task RepairMissingSubscriptionsAsync(
        IReadOnlySet<string> enabledRemoteSubscriptionIds,
        IReadOnlyList<string> desiredChannels,
        CancellationToken cancellationToken
    )
    {
        var missing = FindChannels(subscription =>
            SubscriptionIds(subscription).Any(id => !enabledRemoteSubscriptionIds.Contains(id))
        );
        await RepairSubscriptionsAsync(
            missing,
            desiredChannels,
            EventSubChannelRecoveryTrigger.Periodic,
            cancellationToken
        );
    }

    private async Task RepairSubscriptionsAsync(
        IReadOnlyList<string> channels,
        IReadOnlyList<string> desiredChannels,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var desired = desiredChannels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(
            channels.Select(channel =>
                RunImmediateAsync(
                    channel,
                    desired.Contains(channel)
                        ? EventSubChannelReconciliationTarget.Replacing
                        : EventSubChannelReconciliationTarget.Absent,
                    trigger,
                    cancellationToken
                )
            )
        );
        await RunReconciliationAsync(desiredChannels, trigger, cancellationToken);
    }

    private IReadOnlyList<string> FindChannels(Func<ActiveEventSubSubscription, bool> predicate)
    {
        lock (_gate)
        {
            return
            [
                .. _subscriptions
                    .Where(pair => predicate(pair.Value))
                    .Select(static pair => pair.Key),
            ];
        }
    }

    private static bool ContainsSubscription(
        ActiveEventSubSubscription subscription,
        string subscriptionId
    ) => SubscriptionIds(subscription).Contains(subscriptionId, StringComparer.Ordinal);

    private static IEnumerable<string> SubscriptionIds(ActiveEventSubSubscription subscription)
    {
        yield return subscription.SubscriptionId;
        foreach (var id in subscription.AdditionalSubscriptionIds)
        {
            yield return id;
        }

        foreach (
            var operation in _operationSubscriptionKinds.Select(kind =>
                GetOperationState(subscription, kind)
            )
        )
        {
            var active = operation switch
            {
                EventSubOperationSubscriptionState.Active value => value.Subscription,
                EventSubOperationSubscriptionState.CleanupPending value => value.Subscription,
                _ => null,
            };
            if (active is null)
            {
                continue;
            }

            foreach (var id in SubscriptionIds(active))
            {
                yield return id;
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

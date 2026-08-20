namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
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
}

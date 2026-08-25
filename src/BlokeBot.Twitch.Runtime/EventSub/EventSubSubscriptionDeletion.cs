using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal abstract record EventSubSubscriptionDeletionOutcome
{
    private EventSubSubscriptionDeletionOutcome() { }

    internal sealed record Deleted : EventSubSubscriptionDeletionOutcome;

    internal sealed record Unresolved : EventSubSubscriptionDeletionOutcome
    {
        internal required EventSubChannelFailureDetails Failure { get; init; }
    }
}

internal abstract record EventSubPendingDeletionState
{
    private EventSubPendingDeletionState() { }

    internal sealed record Scheduled : EventSubPendingDeletionState;

    internal sealed record Unresolved : EventSubPendingDeletionState
    {
        internal required EventSubChannelFailureDetails Failure { get; init; }
    }
}

internal sealed record EventSubPendingDeletion
{
    internal required ActiveEventSubSubscription Subscription { get; init; }

    internal required BotChannelTarget RuntimeTarget { get; init; }

    internal required EventSubPendingDeletionState State { get; init; }
}

internal sealed class EventSubSubscriptionReconciliationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EventSubPendingDeletion> _pending = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, BotChannelTarget> _pendingLifecycleReconciliations = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal IReadOnlyList<EventSubPendingDeletion> PendingDeletions
    {
        get
        {
            lock (_gate)
            {
                return _pending
                    .Values.OrderBy(
                        static deletion => deletion.Subscription.Channel,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToArray();
            }
        }
    }

    internal IReadOnlyList<string> PendingDeletionChannels =>
        PendingDeletions.Select(static deletion => deletion.Subscription.Channel).ToArray();

    internal IReadOnlyList<string> ReconciliationChannels
    {
        get
        {
            lock (_gate)
            {
                return _pending
                    .Keys.Union(
                        _pendingLifecycleReconciliations.Keys,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    internal bool HasPendingReconciliation
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0 || _pendingLifecycleReconciliations.Count > 0;
            }
        }
    }

    internal bool TryGet(string channel, out EventSubPendingDeletion deletion)
    {
        lock (_gate)
        {
            return _pending.TryGetValue(channel, out deletion!);
        }
    }

    internal void Begin(ActiveEventSubSubscription subscription, BotChannelTarget runtimeTarget)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(subscription.Channel, out var existing))
            {
                EnsureSameSubscription(existing.Subscription, subscription);
                return;
            }

            if (_pendingLifecycleReconciliations.ContainsKey(subscription.Channel))
            {
                throw new UnreachableException(
                    "An EventSub deletion cannot begin before the prior deletion lifecycle is reconciled."
                );
            }

            _pending[subscription.Channel] = new EventSubPendingDeletion
            {
                Subscription = subscription,
                RuntimeTarget = runtimeTarget,
                State = new EventSubPendingDeletionState.Scheduled(),
            };
        }
    }

    internal void UpdateSubscription(ActiveEventSubSubscription subscription)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(subscription.Channel, out var existing))
            {
                throw new UnreachableException(
                    "An EventSub deletion update has no pending local evidence."
                );
            }

            EnsureSameSubscription(existing.Subscription, subscription);
            _pending[subscription.Channel] = existing with { Subscription = subscription };
        }
    }

    internal void RetainUnresolved(
        ActiveEventSubSubscription subscription,
        EventSubChannelFailureDetails failure
    )
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(subscription.Channel, out var existing))
            {
                throw new UnreachableException(
                    "An unresolved EventSub deletion has no pending local evidence."
                );
            }

            EnsureSameSubscription(existing.Subscription, subscription);

            _pending[subscription.Channel] = new EventSubPendingDeletion
            {
                Subscription = subscription,
                RuntimeTarget = existing.RuntimeTarget,
                State = new EventSubPendingDeletionState.Unresolved { Failure = failure },
            };
        }
    }

    internal void ConfirmDeleted(ActiveEventSubSubscription subscription)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(subscription.Channel, out var existing))
            {
                throw new UnreachableException(
                    "A confirmed EventSub deletion has no pending local evidence."
                );
            }

            EnsureSameSubscription(existing.Subscription, subscription);
            _ = _pending.Remove(subscription.Channel);
            _pendingLifecycleReconciliations[subscription.Channel] = existing.RuntimeTarget;
        }
    }

    internal bool HasPendingLifecycleReconciliation(string channel)
    {
        lock (_gate)
        {
            return _pendingLifecycleReconciliations.ContainsKey(channel);
        }
    }

    internal bool TryGetRuntimeTarget(string channel, out BotChannelTarget target)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(channel, out var deletion))
            {
                target = deletion.RuntimeTarget;
                return true;
            }

            return _pendingLifecycleReconciliations.TryGetValue(channel, out target!);
        }
    }

    internal void ConfirmLifecycleReconciled(string channel)
    {
        lock (_gate)
        {
            if (!_pendingLifecycleReconciliations.Remove(channel))
            {
                throw new UnreachableException(
                    "A reconciled EventSub deletion lifecycle has no pending local evidence."
                );
            }
        }
    }

    private static void EnsureSameSubscription(
        ActiveEventSubSubscription expected,
        ActiveEventSubSubscription actual
    )
    {
        if (
            expected.Channel.Equals(actual.Channel, StringComparison.OrdinalIgnoreCase)
            && expected.SubscriptionId.Equals(actual.SubscriptionId, StringComparison.Ordinal)
        )
        {
            return;
        }

        throw new UnreachableException(
            "A channel cannot replace EventSub pending-deletion evidence before reconciliation."
        );
    }
}

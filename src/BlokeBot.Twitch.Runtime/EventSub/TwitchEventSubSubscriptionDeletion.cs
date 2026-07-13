using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal abstract record TwitchEventSubSubscriptionDeletionOutcome
{
    private TwitchEventSubSubscriptionDeletionOutcome() { }

    internal sealed record Deleted : TwitchEventSubSubscriptionDeletionOutcome;

    internal sealed record Unresolved : TwitchEventSubSubscriptionDeletionOutcome
    {
        internal required TwitchEventSubChannelFailureDetails Failure { get; init; }
    }
}

internal abstract record TwitchEventSubPendingDeletionState
{
    private TwitchEventSubPendingDeletionState() { }

    internal sealed record Scheduled : TwitchEventSubPendingDeletionState;

    internal sealed record Unresolved : TwitchEventSubPendingDeletionState
    {
        internal required TwitchEventSubChannelFailureDetails Failure { get; init; }
    }
}

internal sealed record TwitchEventSubPendingDeletion
{
    internal required ActiveEventSubSubscription Subscription { get; init; }

    internal required TwitchEventSubPendingDeletionState State { get; init; }
}

internal sealed class TwitchEventSubSubscriptionReconciliationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TwitchEventSubPendingDeletion> _pending = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> _pendingStops = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<TwitchEventSubPendingDeletion> PendingDeletions
    {
        get
        {
            lock (_gate)
            {
                return _pending
                    .Values.OrderBy(
                        deletion => deletion.Subscription.Channel,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToArray();
            }
        }
    }

    internal IReadOnlyList<string> PendingDeletionChannels =>
        PendingDeletions.Select(deletion => deletion.Subscription.Channel).ToArray();

    internal IReadOnlyList<string> ReconciliationChannels
    {
        get
        {
            lock (_gate)
            {
                return _pending
                    .Keys.Union(_pendingStops, StringComparer.OrdinalIgnoreCase)
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
                return _pending.Count > 0 || _pendingStops.Count > 0;
            }
        }
    }

    internal bool TryGet(string channel, out TwitchEventSubPendingDeletion deletion)
    {
        lock (_gate)
        {
            return _pending.TryGetValue(channel, out deletion!);
        }
    }

    internal void Begin(ActiveEventSubSubscription subscription)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(subscription.Channel, out var existing))
            {
                EnsureSameSubscription(existing.Subscription, subscription);
                return;
            }

            if (_pendingStops.Contains(subscription.Channel))
            {
                throw new UnreachableException(
                    "An EventSub deletion cannot begin before the prior channel stop is reconciled."
                );
            }

            _pending[subscription.Channel] = new TwitchEventSubPendingDeletion
            {
                Subscription = subscription,
                State = new TwitchEventSubPendingDeletionState.Scheduled(),
            };
        }
    }

    internal void RetainUnresolved(
        ActiveEventSubSubscription subscription,
        TwitchEventSubChannelFailureDetails failure
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

            _pending[subscription.Channel] = new TwitchEventSubPendingDeletion
            {
                Subscription = subscription,
                State = new TwitchEventSubPendingDeletionState.Unresolved { Failure = failure },
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
            _pending.Remove(subscription.Channel);
            _pendingStops.Add(subscription.Channel);
        }
    }

    internal bool HasPendingStop(string channel)
    {
        lock (_gate)
        {
            return _pendingStops.Contains(channel);
        }
    }

    internal void ConfirmStopped(string channel)
    {
        lock (_gate)
        {
            if (!_pendingStops.Remove(channel))
            {
                throw new UnreachableException(
                    "A confirmed EventSub channel stop has no pending local evidence."
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

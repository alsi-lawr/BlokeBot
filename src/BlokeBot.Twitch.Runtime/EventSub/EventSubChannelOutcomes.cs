using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal abstract record EventSubSubscriptionSetupOutcome
{
    private protected EventSubSubscriptionSetupOutcome() { }

    internal sealed record Created(ActiveEventSubSubscription Subscription)
        : EventSubSubscriptionSetupOutcome;

    internal sealed record MissingChannel : EventSubSubscriptionSetupOutcome;

    internal sealed record MissingBot : EventSubSubscriptionSetupOutcome;

    internal sealed record PartiallyCreated(
        ActiveEventSubSubscription Subscription,
        Exception Failure
    ) : EventSubSubscriptionSetupOutcome;
}

internal abstract record EventSubChannelReconciliationOutcome
{
    private EventSubChannelReconciliationOutcome() { }

    internal TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot,
        Func<StartupMessageRejected, TResult> startupMessageRejected,
        Func<TokenUnavailable, TResult> tokenUnavailable,
        Func<UnresolvedDeletion, TResult> unresolvedDeletion
    )
    {
        return this switch
        {
            Completed outcome => completed(outcome),
            MissingChannel outcome => missingChannel(outcome),
            MissingBot outcome => missingBot(outcome),
            StartupMessageRejected outcome => startupMessageRejected(outcome),
            TokenUnavailable outcome => tokenUnavailable(outcome),
            UnresolvedDeletion outcome => unresolvedDeletion(outcome),
            _ => throw new UnreachableException("Unknown EventSub channel reconciliation outcome."),
        };
    }

    internal sealed record Completed : EventSubChannelReconciliationOutcome;

    internal sealed record MissingChannel : EventSubChannelReconciliationOutcome;

    internal sealed record MissingBot : EventSubChannelReconciliationOutcome;

    internal sealed record StartupMessageRejected : EventSubChannelReconciliationOutcome;

    internal sealed record TokenUnavailable(AccessTokenUnavailableReason Reason)
        : EventSubChannelReconciliationOutcome;

    internal sealed record UnresolvedDeletion : EventSubChannelReconciliationOutcome
    {
        internal required EventSubChannelFailureDetails Failure { get; init; }

        public override string ToString()
        {
            return nameof(UnresolvedDeletion);
        }
    }
}

internal abstract record EventSubStartupDeliveryOutcome
{
    private EventSubStartupDeliveryOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<Rejected, TResult> rejected
    );

    internal sealed record Completed : EventSubStartupDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<Rejected, TResult> rejected
        )
        {
            return completed(this);
        }
    }

    internal sealed record Rejected : EventSubStartupDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }
    }
}

namespace BlokeBot.Twitch.Runtime;

public enum EventSubChannelPhase
{
    AccountResolution,
    SubscriptionSetup,
    SubscriptionDeletion,
    Reconciliation,
}

public enum EventSubChannelFailureClassification
{
    Cancellation,
    Timeout,
    Transient,
    Terminal,
    Unexpected,
}

public enum EventSubChannelRecoveryTrigger
{
    Startup,
    Explicit,
    Periodic,
}

public enum EventSubChannelNextAction
{
    BeginRecoveryCycle,
    ContinueRecoveryCycle,
    RetryOnNextReconciliation,
    NoFurtherAction,
}

public sealed record EventSubChannelFailure
{
    public required EventSubChannelFailureClassification Classification { get; init; }

    public required string FailureType { get; init; }
}

public abstract record EventSubChannelStatus
{
    private EventSubChannelStatus() { }

    public abstract string Channel { get; init; }

    public abstract EventSubChannelPhase Phase { get; init; }

    public abstract int Attempt { get; init; }

    public abstract DateTimeOffset ChangedAt { get; init; }

    public abstract EventSubChannelRecoveryTrigger Trigger { get; init; }

    public abstract TResult Match<TResult>(
        Func<Healthy, TResult> healthy,
        Func<Recovering, TResult> recovering,
        Func<Degraded, TResult> degraded
    );

    public sealed record Healthy : EventSubChannelStatus
    {
        public override required string Channel { get; init; }

        public override required EventSubChannelPhase Phase { get; init; }

        public override required int Attempt { get; init; }

        public override required DateTimeOffset ChangedAt { get; init; }

        public override required EventSubChannelRecoveryTrigger Trigger { get; init; }

        public override TResult Match<TResult>(
            Func<Healthy, TResult> healthy,
            Func<Recovering, TResult> recovering,
            Func<Degraded, TResult> degraded
        ) => healthy(this);
    }

    public sealed record Recovering : EventSubChannelStatus
    {
        public override required string Channel { get; init; }

        public override required EventSubChannelPhase Phase { get; init; }

        public override required int Attempt { get; init; }

        public override required DateTimeOffset ChangedAt { get; init; }

        public override required EventSubChannelRecoveryTrigger Trigger { get; init; }

        public required EventSubChannelFailure Failure { get; init; }

        public required EventSubChannelNextAction NextAction { get; init; }

        public override TResult Match<TResult>(
            Func<Healthy, TResult> healthy,
            Func<Recovering, TResult> recovering,
            Func<Degraded, TResult> degraded
        ) => recovering(this);
    }

    public sealed record Degraded : EventSubChannelStatus
    {
        public override required string Channel { get; init; }

        public override required EventSubChannelPhase Phase { get; init; }

        public override required int Attempt { get; init; }

        public override required DateTimeOffset ChangedAt { get; init; }

        public override required EventSubChannelRecoveryTrigger Trigger { get; init; }

        public required EventSubChannelFailure Failure { get; init; }

        public required EventSubChannelNextAction NextAction { get; init; }

        public override TResult Match<TResult>(
            Func<Healthy, TResult> healthy,
            Func<Recovering, TResult> recovering,
            Func<Degraded, TResult> degraded
        ) => degraded(this);
    }
}

public sealed record EventSubChannelStatusSnapshot
{
    public required IReadOnlyList<EventSubChannelStatus> Channels { get; init; }
}

public interface IEventSubChannelStatusAccessor
{
    event Action? Changed;

    EventSubChannelStatusSnapshot Current { get; }
}

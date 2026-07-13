namespace BlokeBot.Twitch.Runtime;

public enum TwitchEventSubChannelPhase
{
    AccountResolution,
    SubscriptionSetup,
    SubscriptionDeletion,
    Reconciliation,
}

public enum TwitchEventSubChannelFailureClassification
{
    Cancellation,
    Timeout,
    Transient,
    Terminal,
    Unexpected,
}

public enum TwitchEventSubChannelRecoveryTrigger
{
    Startup,
    Keepalive,
    Explicit,
}

public enum TwitchEventSubChannelNextAction
{
    BeginRecoveryCycle,
    ContinueRecoveryCycle,
    RetryOnNextReconciliation,
}

public sealed record TwitchEventSubChannelFailure
{
    public required TwitchEventSubChannelFailureClassification Classification
    {
        get;
        init;
    }

    public required string FailureType { get; init; }
}

public abstract record TwitchEventSubChannelStatus
{
    private protected TwitchEventSubChannelStatus() { }

    public abstract string Channel { get; init; }

    public abstract TwitchEventSubChannelPhase Phase { get; init; }

    public abstract int Attempt { get; init; }

    public abstract DateTimeOffset ChangedAt { get; init; }

    public abstract TwitchEventSubChannelRecoveryTrigger Trigger { get; init; }

    public abstract TResult Match<TResult>(
        Func<Healthy, TResult> healthy,
        Func<Recovering, TResult> recovering,
        Func<Degraded, TResult> degraded
    );

    private protected abstract void Seal();

    public sealed record Healthy : TwitchEventSubChannelStatus
    {
        public required override string Channel { get; init; }

        public required override TwitchEventSubChannelPhase Phase { get; init; }

        public required override int Attempt { get; init; }

        public required override DateTimeOffset ChangedAt { get; init; }

        public required override TwitchEventSubChannelRecoveryTrigger Trigger { get; init; }

        public override TResult Match<TResult>(
            Func<Healthy, TResult> healthy,
            Func<Recovering, TResult> recovering,
            Func<Degraded, TResult> degraded
        )
        {
            return healthy(this);
        }

        private protected override void Seal() { }
    }

    public sealed record Recovering : TwitchEventSubChannelStatus
    {
        public required override string Channel { get; init; }

        public required override TwitchEventSubChannelPhase Phase { get; init; }

        public required override int Attempt { get; init; }

        public required override DateTimeOffset ChangedAt { get; init; }

        public required override TwitchEventSubChannelRecoveryTrigger Trigger { get; init; }

        public required TwitchEventSubChannelFailure Failure { get; init; }

        public required TwitchEventSubChannelNextAction NextAction { get; init; }

        public override TResult Match<TResult>(
            Func<Healthy, TResult> healthy,
            Func<Recovering, TResult> recovering,
            Func<Degraded, TResult> degraded
        )
        {
            return recovering(this);
        }

        private protected override void Seal() { }
    }

    public sealed record Degraded : TwitchEventSubChannelStatus
    {
        public required override string Channel { get; init; }

        public required override TwitchEventSubChannelPhase Phase { get; init; }

        public required override int Attempt { get; init; }

        public required override DateTimeOffset ChangedAt { get; init; }

        public required override TwitchEventSubChannelRecoveryTrigger Trigger { get; init; }

        public required TwitchEventSubChannelFailure Failure { get; init; }

        public required TwitchEventSubChannelNextAction NextAction { get; init; }

        public override TResult Match<TResult>(
            Func<Healthy, TResult> healthy,
            Func<Recovering, TResult> recovering,
            Func<Degraded, TResult> degraded
        )
        {
            return degraded(this);
        }

        private protected override void Seal() { }
    }
}

public sealed record TwitchEventSubChannelStatusSnapshot
{
    public required IReadOnlyList<TwitchEventSubChannelStatus> Channels { get; init; }
}

public interface ITwitchEventSubChannelStatusAccessor
{
    event Action? Changed;

    TwitchEventSubChannelStatusSnapshot Current { get; }
}

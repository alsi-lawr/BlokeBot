namespace BlokeBot.Twitch.Runtime;

internal enum EventSubBroadcasterOperationKind
{
    Polls,
    RewardRedemptions,
    Predictions,
}

public abstract record EventSubAuthorizationContext
{
    private EventSubAuthorizationContext() { }

    public abstract TResult Match<TResult>(
        Func<ConfiguredBot, TResult> configuredBot,
        Func<ConfiguredBotOperations, TResult> configuredBotOperations,
        Func<Broadcaster, TResult> broadcaster
    );

    public sealed record ConfiguredBot : EventSubAuthorizationContext
    {
        public override TResult Match<TResult>(
            Func<ConfiguredBot, TResult> configuredBot,
            Func<ConfiguredBotOperations, TResult> configuredBotOperations,
            Func<Broadcaster, TResult> broadcaster
        ) => configuredBot(this);
    }

    public sealed record ConfiguredBotOperations : EventSubAuthorizationContext
    {
        public override TResult Match<TResult>(
            Func<ConfiguredBot, TResult> configuredBot,
            Func<ConfiguredBotOperations, TResult> configuredBotOperations,
            Func<Broadcaster, TResult> broadcaster
        ) => configuredBotOperations(this);
    }

    public sealed record Broadcaster : EventSubAuthorizationContext
    {
        internal Broadcaster(EventSubBroadcasterOperationKind operation) => Operation = operation;

        internal EventSubBroadcasterOperationKind Operation { get; }

        public override TResult Match<TResult>(
            Func<ConfiguredBot, TResult> configuredBot,
            Func<ConfiguredBotOperations, TResult> configuredBotOperations,
            Func<Broadcaster, TResult> broadcaster
        ) => broadcaster(this);
    }

    public static EventSubAuthorizationContext ConfiguredBotAuthority { get; } =
        new ConfiguredBot();

    public static EventSubAuthorizationContext ConfiguredBotOperationsAuthority { get; } =
        new ConfiguredBotOperations();

    public static EventSubAuthorizationContext BroadcasterAuthority { get; } =
        new Broadcaster(EventSubBroadcasterOperationKind.Polls);

    internal static EventSubAuthorizationContext RewardRedemptionsAuthority { get; } =
        new Broadcaster(EventSubBroadcasterOperationKind.RewardRedemptions);

    internal static EventSubAuthorizationContext PredictionsAuthority { get; } =
        new Broadcaster(EventSubBroadcasterOperationKind.Predictions);
}

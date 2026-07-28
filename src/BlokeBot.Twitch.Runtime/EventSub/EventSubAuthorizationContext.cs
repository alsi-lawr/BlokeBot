using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

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
        )
        {
            return configuredBot(this);
        }
    }

    public sealed record ConfiguredBotOperations : EventSubAuthorizationContext
    {
        public override TResult Match<TResult>(
            Func<ConfiguredBot, TResult> configuredBot,
            Func<ConfiguredBotOperations, TResult> configuredBotOperations,
            Func<Broadcaster, TResult> broadcaster
        )
        {
            return configuredBotOperations(this);
        }
    }

    public sealed record Broadcaster : EventSubAuthorizationContext
    {
        public override TResult Match<TResult>(
            Func<ConfiguredBot, TResult> configuredBot,
            Func<ConfiguredBotOperations, TResult> configuredBotOperations,
            Func<Broadcaster, TResult> broadcaster
        )
        {
            return broadcaster(this);
        }
    }

    public static EventSubAuthorizationContext ConfiguredBotAuthority { get; } =
        new ConfiguredBot();

    public static EventSubAuthorizationContext ConfiguredBotOperationsAuthority { get; } =
        new ConfiguredBotOperations();

    public static EventSubAuthorizationContext BroadcasterAuthority { get; } = new Broadcaster();
}

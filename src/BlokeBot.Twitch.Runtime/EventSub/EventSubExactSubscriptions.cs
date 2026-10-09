namespace BlokeBot.Twitch.Runtime;

public sealed record EventSubExactSubscription(string Type, string Version)
{
    internal EventSubAuthorizationContext Authorization =>
        Type
            is "channel.ad_break.begin"
                or "channel.goal.begin"
                or "channel.goal.progress"
                or "channel.goal.end"
                or "channel.moderate"
            ? EventSubAuthorizationContext.BroadcasterAuthority
            : EventSubAuthorizationContext.ConfiguredBotAuthority;
}

public interface IEventSubExactRequirementSource
{
    ValueTask<IReadOnlyList<EventSubExactSubscription>> GetRequirementsAsync(
        string channel,
        CancellationToken cancellationToken
    );
}

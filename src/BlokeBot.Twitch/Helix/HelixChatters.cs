using System.Collections.Immutable;

namespace BlokeBot.Twitch;

public sealed record HelixChatter(string UserId, string Login, string DisplayName);

public abstract record HelixChattersOutcome
{
    private HelixChattersOutcome() { }

    public sealed record Complete(ImmutableArray<HelixChatter> Chatters) : HelixChattersOutcome;

    public sealed record Unavailable : HelixChattersOutcome;
}

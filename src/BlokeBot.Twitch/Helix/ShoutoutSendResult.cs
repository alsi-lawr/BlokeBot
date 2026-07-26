namespace BlokeBot.Twitch;

public abstract record ShoutoutSendResult
{
    private ShoutoutSendResult() { }

    public sealed record Sent : ShoutoutSendResult;

    public sealed record InvalidTarget : ShoutoutSendResult;

    public sealed record NotLive : ShoutoutSendResult;

    public sealed record Cooldown : ShoutoutSendResult;

    public sealed record Unauthorized : ShoutoutSendResult;

    public sealed record Unavailable : ShoutoutSendResult;
}

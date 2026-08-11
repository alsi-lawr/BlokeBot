namespace BlokeBot.Twitch;

public sealed record HelixStream(
    string Id,
    string UserId,
    string UserLogin,
    string UserName,
    string GameName,
    string Title,
    string Language,
    int ViewerCount,
    DateTimeOffset StartedAt
);

public abstract record HelixRaidStartOutcome
{
    private HelixRaidStartOutcome() { }

    public sealed record Started(DateTimeOffset CreatedAt, bool IsMature) : HelixRaidStartOutcome;

    public sealed record Unauthorized : HelixRaidStartOutcome;

    public sealed record InvalidTarget : HelixRaidStartOutcome;

    public sealed record AlreadyPending : HelixRaidStartOutcome;

    public sealed record Unavailable : HelixRaidStartOutcome;
}

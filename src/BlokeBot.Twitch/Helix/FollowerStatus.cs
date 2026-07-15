namespace BlokeBot.Twitch;

public abstract record FollowerStatus
{
    private FollowerStatus() { }

    public sealed record Follows : FollowerStatus;

    public sealed record DoesNotFollow : FollowerStatus;

    public sealed record Unavailable : FollowerStatus;
}

using System.Diagnostics;

namespace BlokeBot.Twitch;

public abstract record ActiveBotFollowStatus
{
    private ActiveBotFollowStatus() { }

    public TResult Match<TResult>(
        Func<Follows, TResult> follows,
        Func<DoesNotFollow, TResult> doesNotFollow,
        Func<Unavailable, TResult> unavailable
    )
    {
        return this switch
        {
            Follows value => follows(value),
            DoesNotFollow value => doesNotFollow(value),
            Unavailable value => unavailable(value),
            _ => throw new UnreachableException("Unknown active bot follow status."),
        };
    }

    public sealed record Follows(DateTimeOffset FollowedAtUtc) : ActiveBotFollowStatus;

    public sealed record DoesNotFollow : ActiveBotFollowStatus;

    public sealed record Unavailable : ActiveBotFollowStatus;
}

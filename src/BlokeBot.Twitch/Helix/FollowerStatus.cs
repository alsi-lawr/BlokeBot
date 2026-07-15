using System.Diagnostics;

namespace BlokeBot.Twitch;

public abstract record FollowerStatus
{
    private FollowerStatus() { }

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
            _ => throw new UnreachableException("Unknown follower status."),
        };
    }

    public sealed record Follows : FollowerStatus;

    public sealed record DoesNotFollow : FollowerStatus;

    public sealed record Unavailable : FollowerStatus;
}

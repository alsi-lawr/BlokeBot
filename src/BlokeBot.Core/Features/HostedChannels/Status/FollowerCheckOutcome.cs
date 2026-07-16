using System.Diagnostics;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public abstract record FollowerCheckOutcome
{
    private FollowerCheckOutcome() { }

    public TResult Match<TResult>(
        Func<Eligible, TResult> eligible,
        Func<NotEligible, TResult> notEligible,
        Func<Unavailable, TResult> unavailable
    )
    {
        return this switch
        {
            Eligible value => eligible(value),
            NotEligible value => notEligible(value),
            Unavailable value => unavailable(value),
            _ => throw new UnreachableException("Unknown follower check outcome."),
        };
    }

    public sealed record Eligible : FollowerCheckOutcome;

    public sealed record NotEligible : FollowerCheckOutcome;

    public sealed record Unavailable : FollowerCheckOutcome;
}

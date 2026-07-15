namespace BlokeBot.Features.HostedChannels.Status;

public abstract record FollowerCheckOutcome
{
    private FollowerCheckOutcome() { }

    public sealed record Eligible : FollowerCheckOutcome;

    public sealed record NotEligible : FollowerCheckOutcome;

    public sealed record Unavailable : FollowerCheckOutcome;
}

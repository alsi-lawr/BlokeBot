namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public sealed record ShoutoutDashboardState(
    DateTime? GlobalEligibleAtUtc,
    ShoutoutTargetCooldownReadiness TargetCooldown,
    IReadOnlyList<ShoutoutHistoryView> History
);

public abstract record ShoutoutTargetCooldownReadiness
{
    private ShoutoutTargetCooldownReadiness() { }

    public sealed record Unknown : ShoutoutTargetCooldownReadiness;

    public sealed record EligibleAt(DateTime Value) : ShoutoutTargetCooldownReadiness;
}

public sealed record ShoutoutHistoryView(
    ShoutoutDirection Direction,
    string SourceLogin,
    string TargetLogin,
    int ViewerCount,
    DateTime OccurredAtUtc,
    DateTime? CooldownEndsAtUtc,
    DateTime? TargetCooldownEndsAtUtc
);

public enum ShoutoutDirection
{
    Sent,
    Received,
}

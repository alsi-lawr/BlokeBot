namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public sealed record ShoutoutDashboardState(
    DateTime? GlobalEligibleAtUtc,
    IReadOnlyList<ShoutoutHistoryView> History
);

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

namespace BlokeBot.Core.Features.Points.Giveaways;

internal sealed record PointsGiveawayCurrent(
    int Id,
    PointsGiveawayLifecycle Lifecycle,
    DateTime StartedAtUtc,
    DateTime EndsAtUtc,
    int EntrantCount
);

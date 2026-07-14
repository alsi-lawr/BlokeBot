using System.Collections.Immutable;
using BlokeBot.Features.Points.Balances;

namespace BlokeBot.Features.Points.Giveaways;

public sealed record PointsGiveawayView(
    int Id,
    PointsGiveawayLifecycle Lifecycle,
    DateTime StartedAtUtc,
    DateTime EndsAtUtc,
    ImmutableArray<string> Entrants,
    ImmutableArray<PointsGiveawayWinnerView> Winners
);

public sealed record PointsGiveawayWinnerView(string Login, PointAmount Payout);

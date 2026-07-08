using BlokeBot.Features.Points.Balances;

namespace BlokeBot.Features.Points.Giveaways;

public sealed record PointsGiveawayView(
    int Id,
    PointsGiveawayStatus Status,
    DateTime StartedAtUtc,
    DateTime EndsAtUtc,
    IReadOnlyList<string> Entrants,
    IReadOnlyList<PointsGiveawayWinnerView> Winners
);

public sealed record PointsGiveawayWinnerView(string Login, PointAmount Payout);

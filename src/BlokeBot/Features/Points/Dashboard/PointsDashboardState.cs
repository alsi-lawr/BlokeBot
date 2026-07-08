using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Giveaways;

namespace BlokeBot.Features.Points.Dashboard;

public sealed record PointsDashboardState(
    IReadOnlyList<PointBalanceEntry> Leaderboard,
    IReadOnlyList<PointLedgerEntryView> RecentLedger,
    PointsGiveawayView? ActiveGiveaway
);

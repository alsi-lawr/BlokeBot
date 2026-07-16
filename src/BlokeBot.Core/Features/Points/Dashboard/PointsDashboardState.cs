using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;

namespace BlokeBot.Core.Features.Points.Dashboard;

public sealed record PointsDashboardState(
    IReadOnlyList<PointBalanceEntry> Leaderboard,
    IReadOnlyList<PointLedgerEntryView> RecentLedger,
    PointsGiveawayView? ActiveGiveaway
);

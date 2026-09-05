using BlokeBot.Core.Features.ViewerPassports;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Balances;

public sealed partial class PointBalanceService
{
    internal const int MaximumSummaryCandidates = 10_000;
    internal const int MaximumSummaryAmountLength = 128;

    // Null means budget exhaustion, never a truncated leaderboard or a partial rank.
    // Keep the existing parser: historical amounts need not be canonical decimal strings.
    internal async Task<IReadOnlyList<PointBalanceEntry>?> GetBoundedLeaderboardAsync(
        int hostId,
        bool publicOnly,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var excluded = ViewerPassportPublicIdentityPolicy.ExcludedLogins(db, hostId);
        var candidates = await db
            .PointBalances.AsNoTracking()
            .Where(value =>
                value.HostId == hostId && (!publicOnly || !excluded.Contains(value.Login))
            )
            .Take(MaximumSummaryCandidates + 1)
            .Select(value => new
            {
                Login = value.Login.Substring(0, 161),
                Amount = value.Amount.Substring(0, MaximumSummaryAmountLength + 1),
                value.UpdatedAtUtc,
            })
            .ToArrayAsync(ct);
        return
            candidates.Length > MaximumSummaryCandidates
            || candidates.Any(value =>
                value.Amount.Length > MaximumSummaryAmountLength || value.Login.Length > 160
            )
            ? null
            : candidates
                .Select(value => new PointBalanceEntry(
                    value.Login,
                    PointAmount.ParseAbsolute(value.Amount),
                    value.UpdatedAtUtc
                ))
                .OrderByDescending(value => value.Balance.Value)
                .ThenBy(value => value.Login)
                .ToArray();
    }
}

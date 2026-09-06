using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ViewerPassports;

internal sealed record ViewerPassportSelfSummary(
    int HostId,
    string TwitchUserId,
    string ProfileLine,
    ViewerPassportVisibility Visibility,
    string Points,
    int? PointsRank
);

public sealed partial class ViewerPassportService
{
    internal async Task<ViewerPassportSelfSummary?> GetSelfSummaryAsync(
        int hostId,
        ViewerPassportIdentity viewer,
        CancellationToken ct
    )
    {
        var normalized = Normalize(viewer);
        if (normalized is null)
        {
            return null;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await EnabledHostAsync(db, hostId, ct) is null)
        {
            return null;
        }
        var passport =
            await db
                .ViewerPassports.AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.HostId == hostId && value.TwitchUserId == normalized.TwitchUserId,
                    ct
                )
            ?? DraftPassport(hostId, normalized);
        var history = await db
            .ViewerPassportLogins.AsNoTracking()
            .Where(value => value.PassportId == passport.Id)
            .Take(101)
            .Select(value => value.Login.Substring(0, 161))
            .ToArrayAsync(ct);
        if (history.Length > 100 || history.Any(value => value.Length > 160))
        {
            return null;
        }
        var logins = history.ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(passport.Login))
        {
            _ = logins.Add(passport.Login);
        }
        var ambiguous = await db
            .ViewerPassportAmbiguousLogins.AsNoTracking()
            .Where(value => value.HostId == hostId && logins.Contains(value.Login))
            .Select(value => value.Login)
            .ToArrayAsync(ct);
        logins.ExceptWith(ambiguous);
        var other = await OtherIdentityLogins(db, hostId, normalized.TwitchUserId)
            .Where(value => logins.Contains(value))
            .ToArrayAsync(ct);
        logins.ExceptWith(other);
        var leaderboard = await balances.GetBoundedLeaderboardAsync(hostId, ct);
        if (leaderboard is null)
        {
            return null;
        }
        var identityBalances = leaderboard.Where(value => logins.Contains(value.Login)).ToArray();
        var points = identityBalances.Aggregate(
            PointAmount.Zero,
            static (total, value) => total.Add(value.Balance)
        );
        var rank =
            identityBalances.Length == 0
                ? null
                : leaderboard
                    .Where(value => !logins.Contains(value.Login))
                    .Append(
                        new PointBalanceEntry(
                            passport.Login,
                            points,
                            identityBalances.Max(value => value.UpdatedAtUtc)
                        )
                    )
                    .OrderByDescending(value => value.Balance.Value)
                    .ThenBy(value => value.Login)
                    .Select((value, index) => new { value.Login, Rank = index + 1 })
                    .Where(value => value.Login == passport.Login)
                    .Select(value => (int?)value.Rank)
                    .SingleOrDefault();
        return new(
            hostId,
            normalized.TwitchUserId,
            passport.ProfileLine,
            passport.Visibility,
            points.ToDisplayString(),
            rank
        );
    }
}

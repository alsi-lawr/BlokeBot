using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CommunityProgression;

internal sealed record CommunitySummarySeason(string Name, CommunitySeasonStatus Status);

internal sealed record CommunitySummaryCompletion(string DefinitionName, DateTime CompletedAtUtc);

internal sealed record CommunityPublicSummary(
    CommunitySummarySeason Season,
    IReadOnlyList<CommunitySummaryCompletion> Completions
);

public sealed partial class CommunityProgressionService
{
    internal async Task<CommunityPublicSummary?> GetPublicSummaryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return null;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var seasons = db
            .CommunitySeasons.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Visibility == CommunityVisibility.Public
                && value.Status != CommunitySeasonStatus.Draft
            );
        var season = await seasons
            .OrderByDescending(value => value.Status == CommunitySeasonStatus.Open)
            .ThenByDescending(value => value.StartsAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => new CommunitySummarySeason(value.Name, value.Status))
            .FirstOrDefaultAsync(ct);
        if (season is null)
        {
            return null;
        }
        var hidden = db
            .ViewerPassports.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Visibility != ViewerPassportVisibility.Public
                && db.Hosts.Any(host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.ViewerPassports)
                        == HostFeatureFlags.ViewerPassports
                )
            );
        // Same current-passport visibility rule as this owner's public season projection.
        var completions = await db
            .CommunityCompletions.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && seasons.Any(item => item.Id == value.SeasonId)
                && (
                    value.ViewerTwitchUserId == null
                    || !hidden.Any(passport =>
                        passport.TwitchUserId == value.ViewerTwitchUserId
                        || (passport.Login != "" && passport.Login == value.ViewerLogin)
                    )
                )
            )
            .OrderByDescending(value => value.CompletedAtUtc)
            .ThenBy(value => value.Id)
            .Take(5)
            .Select(value => new CommunitySummaryCompletion(
                value.DefinitionName,
                value.CompletedAtUtc
            ))
            .ToArrayAsync(ct);
        return new(season, completions);
    }
}

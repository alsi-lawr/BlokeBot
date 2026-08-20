using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels;

internal static class HostFeatureTransitionStager
{
    public static async Task StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        HostFeatureFlags updated,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var previous = host.EnabledFeatures;
        var bountyRequirements = HostFeatureFlags.Bounties | HostFeatureFlags.Points;
        if (previous.Contains(bountyRequirements) && !updated.Contains(bountyRequirements))
        {
            host.BountiesPausedAtUtc ??= now;
        }
        if (Disabled(previous, updated, HostFeatureFlags.CommunityProgression))
        {
            host.CommunityProgressionPausedAtUtc ??= now;
        }
        if (Enabled(previous, updated, HostFeatureFlags.CommunityProgression))
        {
            host.CommunityProgressionAcceptEventsAfterUtc = now;
            host.CommunityProgressionPausedAtUtc = null;
        }
        if (Disabled(previous, updated, HostFeatureFlags.Bingo))
        {
            host.BingoPausedAtUtc ??= now;
        }
        if (Enabled(previous, updated, HostFeatureFlags.Bingo))
        {
            host.BingoAcceptEventsAfterUtc = now;
            host.BingoPausedAtUtc = null;
        }
        if (Disabled(previous, updated, HostFeatureFlags.Competitions))
        {
            host.CompetitionsPausedAtUtc ??= now;
        }
        if (Enabled(previous, updated, HostFeatureFlags.Competitions))
        {
            host.CompetitionsAcceptWorkAfterUtc = now;
            host.CompetitionsPausedAtUtc = null;
        }
        if (Disabled(previous, updated, HostFeatureFlags.RaidCollaboration))
        {
            host.RaidCollaborationPausedAtUtc ??= now;
        }
        if (Enabled(previous, updated, HostFeatureFlags.RaidCollaboration))
        {
            host.RaidCollaborationAcceptEventsAfterUtc = now;
            host.RaidCollaborationPausedAtUtc = null;
        }
        if (Disabled(previous, updated, HostFeatureFlags.CooperativeGame))
        {
            host.BlokeRaidPausedAtUtc ??= now;
        }
        if (Enabled(previous, updated, HostFeatureFlags.CooperativeGame))
        {
            host.BlokeRaidAcceptWorkAfterUtc = now;
            await ResumeBlokeRaidAsync(db, host, now, cancellationToken);
            host.BlokeRaidPausedAtUtc = null;
        }
        if (Disabled(previous, updated, HostFeatureFlags.Collectives))
        {
            host.CollectivesPausedAtUtc ??= now;
        }
        if (Enabled(previous, updated, HostFeatureFlags.Collectives))
        {
            host.CollectivesAcceptWorkAfterUtc = now;
            host.CollectivesPausedAtUtc = null;
        }
        if (Disabled(previous, updated, HostFeatureFlags.ViewerPassports))
        {
            host.ViewerPassportContinuityGeneration++;
        }
        if (Disabled(previous, updated, HostFeatureFlags.Automations))
        {
            host.AutomationGeneration++;
        }
        host.EnabledFeatures = updated;
    }

    private static async Task ResumeBlokeRaidAsync(
        BlokeBotDbContext db,
        BotHost host,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (host.BlokeRaidPausedAtUtc is { } pausedAt)
        {
            var pausedFor = now - pausedAt;
            var campaigns = await db
                .BlokeRaidCampaigns.Where(x =>
                    x.HostId == host.Id && x.Status == BlokeRaidCampaignStatus.Active
                )
                .ToArrayAsync(cancellationToken);
            foreach (var campaign in campaigns)
            {
                campaign.EndsAtUtc += pausedFor;
                campaign.Revision++;
            }
        }
        var configuration = await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
            x => x.HostId == host.Id,
            cancellationToken
        );
        if (
            configuration
                is not {
                    ResetPolicy: BlokeRaidResetPolicy.Weekly,
                    NextWeeklyResetAtUtc: { } nextReset
                }
            || nextReset > now
        )
        {
            return;
        }
        do
        {
            nextReset = nextReset.AddDays(7);
        } while (nextReset <= now);
        configuration.NextWeeklyResetAtUtc = nextReset;
        configuration.Revision++;
        configuration.UpdatedAtUtc = now;
    }

    private static bool Enabled(
        HostFeatureFlags previous,
        HostFeatureFlags updated,
        HostFeatureFlags feature
    ) => !previous.Contains(feature) && updated.Contains(feature);

    private static bool Disabled(
        HostFeatureFlags previous,
        HostFeatureFlags updated,
        HostFeatureFlags feature
    ) => previous.Contains(feature) && !updated.Contains(feature);
}

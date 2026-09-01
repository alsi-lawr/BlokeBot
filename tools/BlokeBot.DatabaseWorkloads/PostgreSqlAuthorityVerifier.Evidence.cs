using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

internal static partial class PostgreSqlAuthorityVerifier
{
    private static async Task<IReadOnlyDictionary<string, long>> ReadOutcomesAsync(
        DbContextOptions<BlokeBotDbContext> options,
        CancellationToken cancellationToken
    )
    {
        await using var db = new BlokeBotDbContext(options);
        return new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["automation_receipts"] = await db.AutomationEventReceipts.LongCountAsync(
                cancellationToken
            ),
            ["automation_runs"] = await db.AutomationFlowRuns.LongCountAsync(cancellationToken),
            ["community_receipts"] = await db.CommunitySourceEventReceipts.LongCountAsync(
                cancellationToken
            ),
            ["command_claims"] = await db.CustomCommandInvocationClaims.LongCountAsync(
                cancellationToken
            ),
            ["raid_claims"] = await db.AutomaticRaidProcessedEvents.LongCountAsync(
                cancellationToken
            ),
            ["raid_collaborations"] = await db.RaidCollaborationHistory.LongCountAsync(
                cancellationToken
            ),
            ["viewer_attendance"] = await db.ViewerPassportStreamAttendances.LongCountAsync(
                cancellationToken
            ),
            ["viewer_ambiguities"] = await db.ViewerPassportAmbiguousLogins.LongCountAsync(
                cancellationToken
            ),
            ["serialized_revision"] = await db
                .PluginFeatureStates.Where(value =>
                    value.PluginId == "synthetic-plugin"
                    && value.FeatureId == "serialized-feature"
                    && value.HostId == 1
                )
                .Select(value => value.Revision)
                .SingleAsync(cancellationToken),
            ["serialized_audits"] = await db.CommunityAudits.LongCountAsync(
                value => value.Action == "SerializedWrite" && value.OccurredAtUtc == _now,
                cancellationToken
            ),
        };
    }

    private static void Require(bool condition, string invariant)
    {
        if (!condition)
        {
            throw new InvalidDataException($"PostgreSQL authority failed: {invariant}.");
        }
    }
}

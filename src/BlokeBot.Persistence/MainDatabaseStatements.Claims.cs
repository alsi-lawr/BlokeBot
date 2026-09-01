using BlokeBot.Persistence.Models;

namespace BlokeBot.Persistence;

public sealed record RaidCollaborationInsert(
    int HostId,
    string ProviderMessageId,
    RaidDirection Direction,
    string OtherTwitchUserId,
    string OtherLogin,
    string OtherDisplayName,
    int ViewerCount,
    DateTime OccurredAtUtc,
    RaidWelcomeOutcome WelcomeOutcome,
    RaidShoutoutOutcome ShoutoutOutcome,
    DateTime RecordedAtUtc,
    long RequiredFeature,
    DateTime MessageTimestampUtc
);

public static partial class MainDatabaseStatements
{
    public static Task<int> TryClaimCommunitySourceEventAsync(
        BlokeBotDbContext db,
        int hostId,
        CommunityEventRuleKind sourceKind,
        string sourceEventId,
        DateTime processedAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO community_source_event_receipts
                ("HostId", "SourceKind", "SourceEventId", "ProcessedAtUtc")
            VALUES ({hostId}, {PersistedEnumTokens<CommunityEventRuleKind>.Format(
                sourceKind
            )}, {sourceEventId}, {processedAtUtc});
            """,
            $"""
            INSERT INTO community_source_event_receipts
                ("HostId", "SourceKind", "SourceEventId", "ProcessedAtUtc")
            VALUES ({hostId}, {PersistedEnumTokens<CommunityEventRuleKind>.Format(
                sourceKind
            )}, {sourceEventId}, {processedAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );

    public static Task<int> TryClaimCustomCommandInvocationAsync(
        BlokeBotDbContext db,
        int hostId,
        int commandId,
        string? twitchUserId,
        string? twitchStreamId,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO custom_command_invocation_claims
                ("HostId", "CustomCommandId", "TwitchUserId", "TwitchStreamId", "ClaimedAtUtc")
            VALUES ({hostId}, {commandId}, {twitchUserId}, {twitchStreamId}, {claimedAtUtc});
            """,
            $"""
            INSERT INTO custom_command_invocation_claims
                ("HostId", "CustomCommandId", "TwitchUserId", "TwitchStreamId", "ClaimedAtUtc")
            VALUES ({hostId}, {commandId}, {twitchUserId}, {twitchStreamId}, {claimedAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );

    public static Task<int> DeleteExpiredCustomCommandClaimsAsync(
        BlokeBotDbContext db,
        string currentStreamId,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.ExecuteDialectAsync(
            db,
            $"""
            DELETE FROM custom_command_invocation_claims
            WHERE Id IN (
                SELECT Id
                FROM custom_command_invocation_claims
                WHERE TwitchStreamId IS NOT NULL
                  AND TwitchStreamId <> {currentStreamId}
                  AND ClaimedAtUtc < {cutoffUtc}
                ORDER BY ClaimedAtUtc, Id
                LIMIT {batchSize}
            );
            """,
            $"""
            DELETE FROM custom_command_invocation_claims
            WHERE "Id" IN (
                SELECT "Id"
                FROM custom_command_invocation_claims
                WHERE "TwitchStreamId" IS NOT NULL
                  AND "TwitchStreamId" <> {currentStreamId}
                  AND "ClaimedAtUtc" < {cutoffUtc}
                ORDER BY "ClaimedAtUtc", "Id"
                LIMIT {batchSize}
            );
            """,
            cancellationToken
        );

    public static Task<int> TryRecordRaidCollaborationAsync(
        BlokeBotDbContext db,
        RaidCollaborationInsert raid,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO raid_collaboration_history
                ("HostId", "ProviderMessageId", "Direction", "OtherTwitchUserId", "OtherLogin",
                 "OtherDisplayName", "ViewerCount", "OccurredAtUtc", "WelcomeOutcome",
                 "ShoutoutOutcome", "RecordedAtUtc")
            SELECT
                 {raid.HostId}, {raid.ProviderMessageId}, {PersistedEnumTokens<RaidDirection>.Format(
                raid.Direction
            )}, {raid.OtherTwitchUserId},
                 {raid.OtherLogin}, {raid.OtherDisplayName}, {raid.ViewerCount}, {raid.OccurredAtUtc},
                 {PersistedEnumTokens<RaidWelcomeOutcome>.Format(
                raid.WelcomeOutcome
            )}, {PersistedEnumTokens<RaidShoutoutOutcome>.Format(
                raid.ShoutoutOutcome
            )}, {raid.RecordedAtUtc}
            FROM hosts
            WHERE "Id" = {raid.HostId}
              AND ("EnabledFeatures" & {raid.RequiredFeature}) = {raid.RequiredFeature}
              AND ("RaidCollaborationAcceptEventsAfterUtc" IS NULL
                   OR "RaidCollaborationAcceptEventsAfterUtc" <= {raid.MessageTimestampUtc});
            """,
            $"""
            INSERT INTO raid_collaboration_history
                ("HostId", "ProviderMessageId", "Direction", "OtherTwitchUserId", "OtherLogin",
                 "OtherDisplayName", "ViewerCount", "OccurredAtUtc", "WelcomeOutcome",
                 "ShoutoutOutcome", "RecordedAtUtc")
            SELECT
                 {raid.HostId}, {raid.ProviderMessageId}, {PersistedEnumTokens<RaidDirection>.Format(
                raid.Direction
            )}, {raid.OtherTwitchUserId},
                 {raid.OtherLogin}, {raid.OtherDisplayName}, {raid.ViewerCount}, {raid.OccurredAtUtc},
                 {PersistedEnumTokens<RaidWelcomeOutcome>.Format(
                raid.WelcomeOutcome
            )}, {PersistedEnumTokens<RaidShoutoutOutcome>.Format(
                raid.ShoutoutOutcome
            )}, {raid.RecordedAtUtc}
            FROM hosts
            WHERE "Id" = {raid.HostId}
              AND ("EnabledFeatures" & {raid.RequiredFeature}) = {raid.RequiredFeature}
              AND ("RaidCollaborationAcceptEventsAfterUtc" IS NULL
                   OR "RaidCollaborationAcceptEventsAfterUtc" <= {raid.MessageTimestampUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );
}

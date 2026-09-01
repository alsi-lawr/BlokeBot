namespace BlokeBot.Persistence;

public static partial class MainDatabaseStatements
{
    public static Task<int> DeleteExpiredAutomaticRaidEventsAsync(
        BlokeBotDbContext db,
        DateTime expiresBeforeUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.ExecuteDialectAsync(
            db,
            $"DELETE FROM automatic_raid_processed_events WHERE ExpiresAtUtc < {expiresBeforeUtc};",
            $"DELETE FROM automatic_raid_processed_events WHERE \"ExpiresAtUtc\" < {expiresBeforeUtc};",
            cancellationToken
        );

    public static Task<int> TryClaimAutomaticRaidEventAsync(
        BlokeBotDbContext db,
        int hostId,
        string providerMessageId,
        DateTime claimedAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO automatic_raid_processed_events
                ("HostId", "ProviderMessageId", "ClaimedAtUtc", "ExpiresAtUtc")
            VALUES ({hostId}, {providerMessageId}, {claimedAtUtc}, {expiresAtUtc});
            """,
            $"""
            INSERT INTO automatic_raid_processed_events
                ("HostId", "ProviderMessageId", "ClaimedAtUtc", "ExpiresAtUtc")
            VALUES ({hostId}, {providerMessageId}, {claimedAtUtc}, {expiresAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );
}

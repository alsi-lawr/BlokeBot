using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

internal static class CustomCommandInvocationSchemaUpgrade
{
    internal static async Task ApplyAsync(BlokeBotDbContext db, CancellationToken ct)
    {
        var columns = await ExistingColumnsAsync(db, "custom_commands", ct);
        if (columns.Count == 0)
        {
            return;
        }

        if (!columns.Contains("InvocationLimit"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE custom_commands ADD COLUMN InvocationLimit TEXT NOT NULL DEFAULT 'Unlimited';",
                ct
            );
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS custom_command_invocation_claims (
                Id INTEGER NOT NULL CONSTRAINT PK_custom_command_invocation_claims PRIMARY KEY AUTOINCREMENT,
                HostId INTEGER NOT NULL,
                CustomCommandId INTEGER NOT NULL,
                TwitchUserId TEXT NULL,
                TwitchStreamId TEXT NULL,
                ClaimedAtUtc TEXT NOT NULL,
                CONSTRAINT CK_custom_command_invocation_claims_Scope CHECK (
                    (TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL) OR
                    (TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL) OR
                    (TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL)
                ),
                CONSTRAINT FK_custom_command_invocation_claims_custom_commands_HostId_CustomCommandId
                    FOREIGN KEY (HostId, CustomCommandId) REFERENCES custom_commands (HostId, Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_custom_command_invocation_claims_HostId_CustomCommandId_TwitchStreamId
                ON custom_command_invocation_claims (HostId, CustomCommandId, TwitchStreamId)
                WHERE TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_custom_command_invocation_claims_HostId_CustomCommandId_TwitchUserId
                ON custom_command_invocation_claims (HostId, CustomCommandId, TwitchUserId)
                WHERE TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_custom_command_invocation_claims_HostId_CustomCommandId_TwitchUserId_TwitchStreamId
                ON custom_command_invocation_claims (HostId, CustomCommandId, TwitchUserId, TwitchStreamId)
                WHERE TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL;

            CREATE TABLE IF NOT EXISTS custom_command_invocation_reset_audits (
                Id INTEGER NOT NULL CONSTRAINT PK_custom_command_invocation_reset_audits PRIMARY KEY AUTOINCREMENT,
                HostId INTEGER NOT NULL,
                CustomCommandId INTEGER NULL,
                CommandName TEXT NOT NULL,
                ActorTwitchUserId TEXT NOT NULL,
                ActorLogin TEXT NOT NULL,
                Scope TEXT NOT NULL,
                TargetTwitchUserId TEXT NULL,
                TargetLogin TEXT NULL,
                AffectedClaimCount INTEGER NOT NULL,
                ResetAtUtc TEXT NOT NULL,
                CONSTRAINT CK_custom_command_invocation_reset_audits_Scope CHECK (Scope IN ('OneViewer', 'AllViewers')),
                CONSTRAINT FK_custom_command_invocation_reset_audits_hosts_HostId
                    FOREIGN KEY (HostId) REFERENCES hosts (Id) ON DELETE CASCADE,
                CONSTRAINT FK_custom_command_invocation_reset_audits_custom_commands_CustomCommandId
                    FOREIGN KEY (CustomCommandId) REFERENCES custom_commands (Id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS IX_custom_command_invocation_reset_audits_HostId_ResetAtUtc
                ON custom_command_invocation_reset_audits (HostId, ResetAtUtc);
            CREATE INDEX IF NOT EXISTS IX_custom_command_invocation_reset_audits_CustomCommandId
                ON custom_command_invocation_reset_audits (CustomCommandId);
            """,
            ct
        );
    }

    private static async Task<HashSet<string>> ExistingColumnsAsync(
        BlokeBotDbContext db,
        string table,
        CancellationToken ct
    )
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return columns;
    }
}

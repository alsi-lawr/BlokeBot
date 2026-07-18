using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

internal static class CustomAnnouncementSchemaUpgrade
{
    private const string _table = "custom_announcements";

    public static async Task ApplyAsync(BlokeBotDbContext db, CancellationToken cancellationToken)
    {
        var columns = await ExistingColumnsAsync(db, cancellationToken);
        await AddColumnIfMissingAsync(
            db,
            columns,
            "DeliveryType",
            "TEXT NOT NULL DEFAULT 'ChatMessage'",
            cancellationToken
        );
        await AddColumnIfMissingAsync(
            db,
            columns,
            "AnnouncementColor",
            "TEXT NOT NULL DEFAULT 'Primary'",
            cancellationToken
        );
        await AddColumnIfMissingAsync(
            db,
            columns,
            "LatestDeliveryResult",
            "TEXT NOT NULL DEFAULT 'None'",
            cancellationToken
        );
    }

    private static async Task<HashSet<string>> ExistingColumnsAsync(
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({_table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return columns;
    }

    private static async Task AddColumnIfMissingAsync(
        BlokeBotDbContext db,
        ISet<string> columns,
        string name,
        string definition,
        CancellationToken cancellationToken
    )
    {
        if (!columns.Add(name))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            AddColumnStatement(name, definition),
            cancellationToken
        );
    }

    private static string AddColumnStatement(string name, string definition)
    {
        return (name, definition) switch
        {
            ("DeliveryType", "TEXT NOT NULL DEFAULT 'ChatMessage'") =>
                "ALTER TABLE custom_announcements ADD COLUMN DeliveryType TEXT NOT NULL DEFAULT 'ChatMessage';",
            ("AnnouncementColor", "TEXT NOT NULL DEFAULT 'Primary'") =>
                "ALTER TABLE custom_announcements ADD COLUMN AnnouncementColor TEXT NOT NULL DEFAULT 'Primary';",
            ("LatestDeliveryResult", "TEXT NOT NULL DEFAULT 'None'") =>
                "ALTER TABLE custom_announcements ADD COLUMN LatestDeliveryResult TEXT NOT NULL DEFAULT 'None';",
            _ => throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                "Unknown announcement column."
            ),
        };
    }
}

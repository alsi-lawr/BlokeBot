using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

internal static class StartupMessageSchemaUpgrade
{
    private const string _table = "hosts";

    internal static async Task ApplyAsync(BlokeBotDbContext db, CancellationToken cancellationToken)
    {
        var columns = await ExistingColumnsAsync(db, cancellationToken);
        if (columns.Add("StartupMessageEnabled"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE hosts ADD COLUMN StartupMessageEnabled INTEGER NULL;",
                cancellationToken
            );
        }

        if (columns.Add("StartupMessageText"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE hosts ADD COLUMN StartupMessageText TEXT NULL;",
                cancellationToken
            );
        }
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
}

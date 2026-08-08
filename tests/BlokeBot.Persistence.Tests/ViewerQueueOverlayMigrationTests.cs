using System.Data.Common;
using System.Globalization;

namespace BlokeBot.Persistence.Tests;

public sealed class ViewerQueueOverlayMigrationTests
{
    private const string _previousMigration = "20260731141254_v0.6.0_OverlayAppearance";
    private const string _migration = "20260802075446_v0.6.0_ViewerQueueOverlay";

    private static async Task<long> ReadScalarAsync(DbConnection connection, string sql)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadOverlayTableSqlAsync(DbConnection connection)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'overlay_instances';""";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

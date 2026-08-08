using System.Data.Common;
using System.Globalization;

namespace BlokeBot.Persistence.Tests;

public sealed class AutomationRuntimeMigrationTests
{
    private const string _previousMigration = "20260802075446_v0.6.0_ViewerQueueOverlay";
    private const string _migration = "20260803232049_v0.7.0_AutomationRuntime";

    private static async Task<long> ScalarAsync(DbConnection connection, string sql)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}

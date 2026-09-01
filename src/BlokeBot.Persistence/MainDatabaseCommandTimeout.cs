using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public static class MainDatabaseCommandTimeout
{
    public static async Task ApplyClaimBoundAsync(
        BlokeBotDbContext db,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
        db.Database.SetCommandTimeout(seconds);
        if (db.Database.Provider() == BlokeBotDatabaseProvider.Sqlite)
        {
            ((SqliteConnection)db.Database.GetDbConnection()).DefaultTimeout = seconds;
            return;
        }
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "PostgreSQL claim bounds require the feature-owned transaction to be active."
            );
        }

        var milliseconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('lock_timeout', {milliseconds.ToString(CultureInfo.InvariantCulture)} || 'ms', true);",
            cancellationToken
        );
    }
}

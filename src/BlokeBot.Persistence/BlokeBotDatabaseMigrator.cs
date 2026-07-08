using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDatabaseMigrator(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}

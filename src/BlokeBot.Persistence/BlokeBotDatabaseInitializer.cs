using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDatabaseInitializer(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await HetznerBaselineBridge.ApplyAsync(db, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await BingoAssignmentKeyMigration.ApplyAsync(db, cancellationToken);
    }
}

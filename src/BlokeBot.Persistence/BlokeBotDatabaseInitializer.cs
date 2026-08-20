using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDatabaseInitializer(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await HetznerBaselineBridge.ApplyAsync(db, cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            WeeklyAnnouncementMigrationInterceptor.Register(db.Database.GetDbConnection());
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        await BingoAssignmentKeyMigration.ApplyAsync(db, cancellationToken);
    }
}

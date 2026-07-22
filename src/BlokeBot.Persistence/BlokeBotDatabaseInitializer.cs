using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDatabaseInitializer(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    private readonly CustomBotCredentialSchemaUpgrade _customBotCredentialSchemaUpgrade = new(
        dbFactory
    );

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await StartupMessageSchemaUpgrade.ApplyAsync(db, cancellationToken);
        await CustomAnnouncementSchemaUpgrade.ApplyAsync(db, cancellationToken);
        await _customBotCredentialSchemaUpgrade.ApplyAsync(cancellationToken);
    }
}

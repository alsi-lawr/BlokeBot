using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class BlokeBotDatabaseInitializerTests
{
    [Test]
    public async Task EmptyDatabase_Initializing_CreatesLatestSchemaWithoutDeletingExistingData()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        var initializer = new BlokeBotDatabaseInitializer(dbFactory);

        await initializer.InitializeAsync(CancellationToken.None);
        await using (var seedDb = await dbFactory.CreateDbContextAsync())
        {
            seedDb.Hosts.Add(
                new BotHost
                {
                    Login = "streamer",
                    DisplayName = "Streamer",
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
            await seedDb.SaveChangesAsync();
        }

        await initializer.InitializeAsync(CancellationToken.None);

        await using var verificationDb = await dbFactory.CreateDbContextAsync();
        (await verificationDb.Hosts.SingleAsync()).Login.ShouldBe("streamer");
    }
}

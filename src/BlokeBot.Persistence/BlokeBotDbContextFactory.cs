using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDbContextFactory : IDesignTimeDbContextFactory<BlokeBotDbContext>
{
    public BlokeBotDbContext CreateDbContext(string[] args)
    {
        var configuration = BlokeBotDesignTimeDatabaseConfiguration.Parse(
            args,
            BlokeBotDatabaseProvider.Sqlite
        );
        var builder = new DbContextOptionsBuilder<BlokeBotDbContext>();
        configuration.Configure(builder);
        return new BlokeBotDbContext(builder.Options);
    }
}

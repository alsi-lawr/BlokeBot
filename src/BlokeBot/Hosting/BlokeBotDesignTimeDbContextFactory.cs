using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace BlokeBot.Hosting;

public sealed class BlokeBotDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<BlokeBotDbContext>
{
    public BlokeBotDbContext CreateDbContext(string[] args) =>
        BlokeBotDesignTimeDatabaseConfiguration
            .Parse(args, BlokeBotDatabaseProvider.PostgreSql)
            .CreateDbContext();
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDbContextFactory : IDesignTimeDbContextFactory<BlokeBotDbContext>
{
    public BlokeBotDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<BlokeBotDbContext>();
        builder.UseSqlite("Data Source=blokebot.db");
        return new BlokeBotDbContext(builder.Options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDbContextFactory : IDesignTimeDbContextFactory<BlokeBotDbContext>
{
    public BlokeBotDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<BlokeBotDbContext>();
        _ = builder
            .UseSqlite("Data Source=blokebot.db")
            .AddInterceptors(new WeeklyAnnouncementMigrationInterceptor());
        return new BlokeBotDbContext(builder.Options);
    }
}

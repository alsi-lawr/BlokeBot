using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Persistence;

public static class BlokeBotPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPersistence(
        this IServiceCollection services,
        string databasePath
    )
    {
        services.AddDbContextFactory<BlokeBotDbContext>(db =>
        {
            var fullPath = Path.GetFullPath(databasePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
            }.ToString();
            db.UseSqlite(connectionString);
        });
        services.AddSingleton<BlokeBotDatabaseInitializer>();

        return services;
    }
}

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
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
        }.ToString();
        return services.AddBlokeBotPersistence(_ => connectionString);
    }

    public static IServiceCollection AddBlokeBotPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString
    )
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        _ = services.AddDbContextFactory<BlokeBotDbContext>(
            (provider, db) => db.UseSqlite(connectionString(provider))
        );
        _ = services.AddSingleton<BlokeBotDatabaseInitializer>();

        return services;
    }
}

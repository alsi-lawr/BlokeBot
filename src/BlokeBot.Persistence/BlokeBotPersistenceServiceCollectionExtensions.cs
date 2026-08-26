using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
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
        _ = services.AddSingleton<IPluginLifecycleStore, EfPluginLifecycleStore>();
        _ = services.AddSingleton<EfPluginMarketplaceReceiptStore>();
        _ = services.AddSingleton<IPluginMarketplaceReceiptStore>(provider =>
            provider.GetRequiredService<EfPluginMarketplaceReceiptStore>()
        );
        _ = services.AddSingleton<IPluginRemovalDataOwner>(provider =>
            provider.GetRequiredService<EfPluginMarketplaceReceiptStore>()
        );
        _ = services.AddSingleton<
            IPluginMarketplaceCatalogStore,
            EfPluginMarketplaceCatalogStore
        >();
        _ = services.AddSingleton<PluginSettingValuesCodec>();
        _ = services.AddSingleton<IPluginFeatureStore, EfPluginFeatureStore>();

        return services;
    }
}

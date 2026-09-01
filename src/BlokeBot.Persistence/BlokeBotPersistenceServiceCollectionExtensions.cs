using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Persistence;

public static class BlokeBotPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPersistence(
        this IServiceCollection services,
        string databasePath
    ) => services.AddBlokeBotPersistence(BlokeBotDatabaseConfiguration.Sqlite(databasePath));

    public static IServiceCollection AddBlokeBotPersistence(
        this IServiceCollection services,
        BlokeBotDatabaseConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddBlokeBotPersistenceServices(
            (provider, builder) => configuration.Configure(builder)
        );
    }

    public static IServiceCollection AddBlokeBotPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString
    )
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        return services.AddBlokeBotPersistenceServices(
            (provider, builder) => builder.UseSqlite(connectionString(provider))
        );
    }

    private static IServiceCollection AddBlokeBotPersistenceServices(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure
    )
    {
        _ = services.AddDbContextFactory<BlokeBotDbContext>(configure);
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

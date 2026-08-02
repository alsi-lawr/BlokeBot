namespace BlokeBot.Core.BotStatus;

internal static class OfflineBotStatusServiceCollectionExtensions
{
    internal static IServiceCollection AddOfflineBotRuntimeStatus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IBotRuntimeStatusAccessor, OfflineBotStatusAccessor>();
        return services;
    }
}

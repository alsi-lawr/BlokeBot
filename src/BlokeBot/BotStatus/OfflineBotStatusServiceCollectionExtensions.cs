using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.BotStatus;

internal static class OfflineBotStatusServiceCollectionExtensions
{
    internal static IServiceCollection AddOfflineBotRuntimeStatus(
        this IServiceCollection services
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITwitchBotRuntimeStatusAccessor, OfflineBotStatusAccessor>();
        return services;
    }
}

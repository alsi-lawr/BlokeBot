using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static class BlokeBotPublicChatServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPublicChat(this IServiceCollection services)
    {
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IPublicChatOutbox>(
            static serviceProvider => new EfPublicChatOutbox(
                serviceProvider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
                serviceProvider.GetRequiredKeyedService<PublicChatRetryPolicy>(
                    BotResiliencePipeline.PublicChatDelivery
                ),
                serviceProvider.GetRequiredService<PublicChatDeliveryLifetimePolicy>(),
                serviceProvider.GetRequiredService<PublicChatTerminalRetentionPolicy>(),
                serviceProvider.GetRequiredService<DurableAlertService>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPublicChatPinStore, EfPublicChatPinStore>()
        );
        return services;
    }
}

using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static class BlokeBotPublicChatServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPublicChat(this IServiceCollection services)
    {
        services.TryAddSingleton<IPublicChatOutbox>(serviceProvider => new EfPublicChatOutbox(
            serviceProvider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
            serviceProvider.GetRequiredKeyedService<PublicChatRetryPolicy>(
                BotResiliencePipeline.PublicChatDelivery
            ),
            serviceProvider.GetRequiredService<PublicChatDeliveryLifetimePolicy>(),
            serviceProvider.GetRequiredService<PublicChatTerminalRetentionPolicy>()
        ));
        return services;
    }
}

using BlokeBot.Features.PublicChat;
using BlokeBot.Persistence;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Hosting;

public static class BlokeBotPublicChatServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPublicChat(this IServiceCollection services)
    {
        services.TryAddSingleton<IPublicChatOutbox>(serviceProvider =>
            new EfPublicChatOutbox(
                serviceProvider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
                serviceProvider.GetRequiredKeyedService<PublicChatRetryPolicy>(
                    TwitchBotResiliencePipeline.PublicChatDelivery
                )
            )
        );
        return services;
    }
}

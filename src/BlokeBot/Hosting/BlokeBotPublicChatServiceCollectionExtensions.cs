using BlokeBot.Features.PublicChat;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Hosting;

public static class BlokeBotPublicChatServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPublicChat(this IServiceCollection services)
    {
        services.TryAddSingleton<IPublicChatOutbox, EfPublicChatOutbox>();
        return services;
    }
}

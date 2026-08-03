using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Commands;

/// <summary>
/// Adds Twitch command services to an application service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the fluent Twitch command builder and dispatcher.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>A builder for command customization.</returns>
    public static IChatBotBuilder AddChatCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ChatCommandRegistry>();
        services.TryAddSingleton(static serviceProvider => new ChatCommandDispatcher(
            serviceProvider.GetRequiredService<ChatCommandRegistry>()
        ));

        return new ChatBotBuilder(services);
    }
}

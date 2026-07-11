using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Commands;

/// <summary>
/// Adds Twitch command services to an application service collection.
/// </summary>
public static class TwitchCommandServiceCollectionExtensions
{
    /// <summary>
    /// Registers the fluent Twitch command builder and dispatcher.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>A builder for command customization.</returns>
    public static ITwitchBotBuilder AddTwitchCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TwitchCommandRegistrationOptions>();
        services.TryAddSingleton<TwitchCommandRegistry>();
        services.TryAddSingleton(serviceProvider =>
            new TwitchCommandDispatcher(
                serviceProvider.GetRequiredService<TwitchCommandRegistry>()
            )
        );

        return new TwitchBotBuilder(services);
    }
}

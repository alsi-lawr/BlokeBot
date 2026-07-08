using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alsi.TwitchBot;

/// <summary>
/// Adds Twitch bot services to an application service collection.
/// </summary>
public static class TwitchBotServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twitch bot runtime and binds options from configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration section that contains bot settings.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static ITwitchBotBuilder AddTwitchBot(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TwitchBotOptions>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return AddTwitchBotCore(services);
    }

    /// <summary>
    /// Registers the Twitch bot runtime and configures options with a callback.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The options configuration callback.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static ITwitchBotBuilder AddTwitchBot(
        this IServiceCollection services,
        Action<TwitchBotOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<TwitchBotOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return AddTwitchBotCore(services);
    }

    private static ITwitchBotBuilder AddTwitchBotCore(IServiceCollection services)
    {
        services.AddHttpClient("twitch-oauth");
        services.AddHttpClient("twitch-helix");
        services.TryAddSingleton<TwitchCommandRegistry>();
        services.TryAddSingleton<TwitchCommandDispatcher>();
        services.TryAddSingleton<TwitchAppAccessTokenProvider>();
        services.TryAddSingleton<TwitchOutboundMessageQueue>();
        services.TryAddSingleton<TwitchOAuthApiClient>();
        services.TryAddSingleton<TwitchHelixApiClient>();
        services.TryAddSingleton<
            ITwitchBotChannelLifecycleNotifier,
            NoOpTwitchBotChannelLifecycleNotifier
        >();
        services.TryAddSingleton<ITwitchChatMessageSender, TwitchChatMessageSender>();
        services.TryAddSingleton<ITwitchTokenStore, JsonTwitchTokenStore>();
        services.TryAddSingleton<ITwitchOAuthStateStore, InMemoryTwitchOAuthStateStore>();
        services.TryAddSingleton<ITwitchOAuthClient, TwitchOAuthClient>();
        services.TryAddSingleton<ITwitchOAuthFlow, TwitchOAuthFlow>();
        services.TryAddSingleton<TwitchAccessTokenProvider>();
        services.TryAddSingleton<ITwitchAccessTokenProvider>(sp =>
            sp.GetRequiredService<TwitchAccessTokenProvider>()
        );
        services.TryAddSingleton<ITwitchAccessTokenCache>(sp =>
            sp.GetRequiredService<TwitchAccessTokenProvider>()
        );
        services.TryAddSingleton<TwitchBotRuntimeStatusStore>();
        services.TryAddSingleton<ITwitchBotRuntimeStatusAccessor>(sp =>
            sp.GetRequiredService<TwitchBotRuntimeStatusStore>()
        );
        services.TryAddSingleton<TwitchEventSubRuntime>();
        services.TryAddSingleton<TwitchHelixChatClient>();
        services.TryAddSingleton<TwitchIrcRuntime>();
        services.AddHostedService<TwitchBotRuntimeHostedService>();

        return new TwitchBotBuilder(services);
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch.Runtime;

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
            .Validate(TwitchBotOptionsValidation.IsValid, "Twitch bot options are invalid.")
            .ValidateOnStart();
        services
            .AddOptions<TwitchBotIdentityOptions>()
            .Bind(configuration.GetSection(nameof(TwitchBotOptions.Identity)))
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
            .Validate(TwitchBotOptionsValidation.IsValid, "Twitch bot options are invalid.")
            .ValidateOnStart();
        services
            .AddOptions<TwitchBotIdentityOptions>()
            .Configure(identity =>
            {
                var options = new TwitchBotOptions();
                configure(options);
                CopyIdentity(options.Identity, identity);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return AddTwitchBotCore(services);
    }

    private static ITwitchBotBuilder AddTwitchBotCore(IServiceCollection services)
    {
        services.AddTwitchAuth();
        services.AddTwitchHelix();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<TwitchOutboundDuplicateCooldown>();
        services.TryAddSingleton<TwitchOutboundQueueBacklogMonitor>();
        services.TryAddSingleton<TwitchOutboundQueueAlertDispatcher>();
        services.TryAddSingleton<TwitchOutboundMessageQueue>();
        services.TryAddSingleton<
            ITwitchBotChannelLifecycleNotifier,
            NoOpTwitchBotChannelLifecycleNotifier
        >();
        services.TryAddSingleton<ITwitchBotAccountProvider, DefaultTwitchBotAccountProvider>();
        services.TryAddSingleton<ITwitchChatMessageSender, TwitchChatMessageSender>();
        services.TryAddSingleton<ITwitchCommandResponseSender, TwitchChatCommandResponseSender>();
        services.AddSingleton<TwitchBotRuntimeStatusStore>();
        services.AddSingleton<ITwitchBotRuntimeStatusAccessor>(sp =>
            sp.GetRequiredService<TwitchBotRuntimeStatusStore>()
        );
        services.TryAddSingleton<TwitchEventSubRuntime>();
        services.TryAddSingleton<TwitchHelixChatClient>();
        services.TryAddSingleton<TwitchIrcRuntime>();
        services.AddHostedService<TwitchBotRuntimeHostedService>();

        return services.AddTwitchCommands();
    }

    private static void CopyIdentity(
        TwitchBotIdentityOptions source,
        TwitchBotIdentityOptions target
    )
    {
        target.BotUsername = source.BotUsername;
        target.ClientId = source.ClientId;
        target.ClientSecret = source.ClientSecret;
        target.RedirectUri = source.RedirectUri;
        target.Scopes = source.Scopes;
        target.TokenCachePath = source.TokenCachePath;
    }
}

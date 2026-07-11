using BlokeBot.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
    /// <param name="selectAccountProvider">Selects exactly one account-provider policy.</param>
    /// <param name="selectResponseSender">Selects exactly one command-response sender policy.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static ITwitchBotBuilder AddTwitchBot(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TwitchBotAccountProviderSelection> selectAccountProvider,
        Action<TwitchCommandResponseSenderSelection> selectResponseSender
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(selectAccountProvider);
        ArgumentNullException.ThrowIfNull(selectResponseSender);

        var accountProvider = SelectAccountProvider(selectAccountProvider);
        var responseSender = SelectResponseSender(selectResponseSender);

        var options = configuration.Get<TwitchBotOptions>()
            ?? throw new OptionsValidationException(
                configuration is IConfigurationSection section ? section.Path : "TwitchBot",
                typeof(TwitchBotOptions),
                ["Twitch bot configuration is required."]
            );
        var boundary = configuration is IConfigurationSection configuredSection
            ? configuredSection.Path
            : "TwitchBot";
        var settings = TwitchBotSettings.FromValidatedOptions(
            options,
            boundary,
            requireConfiguredIdentity: true
        );
        var policies = TwitchBotPolicies.BindRequired(configuration);

        return AddTwitchBotCore(
            services,
            settings,
            policies,
            accountProvider,
            responseSender
        );
    }

    /// <summary>
    /// Registers immutable Twitch settings for host features while the bot runtime is offline.
    /// </summary>
    public static IServiceCollection AddTwitchBotSettings(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.Get<TwitchBotOptions>() ?? new TwitchBotOptions();
        RegisterSettings(services, TwitchBotSettings.FromOptions(options));
        return services;
    }

    /// <summary>
    /// Registers the Twitch bot runtime and configures options with a callback.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The options configuration callback.</param>
    /// <param name="policyOptions">Every required boundary-specific policy.</param>
    /// <param name="selectAccountProvider">Selects exactly one account-provider policy.</param>
    /// <param name="selectResponseSender">Selects exactly one command-response sender policy.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static ITwitchBotBuilder AddTwitchBot(
        this IServiceCollection services,
        Action<TwitchBotOptions> configure,
        TwitchBotPolicyOptions policyOptions,
        Action<TwitchBotAccountProviderSelection> selectAccountProvider,
        Action<TwitchCommandResponseSenderSelection> selectResponseSender
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(policyOptions);
        ArgumentNullException.ThrowIfNull(selectAccountProvider);
        ArgumentNullException.ThrowIfNull(selectResponseSender);

        var accountProvider = SelectAccountProvider(selectAccountProvider);
        var responseSender = SelectResponseSender(selectResponseSender);

        var options = new TwitchBotOptions();
        configure(options);
        var settings = TwitchBotSettings.FromValidatedOptions(
            options,
            "TwitchBot",
            requireConfiguredIdentity: true
        );
        var policies = TwitchBotPolicies.FromOptions(policyOptions);

        return AddTwitchBotCore(
            services,
            settings,
            policies,
            accountProvider,
            responseSender
        );
    }

    private static ITwitchBotBuilder AddTwitchBotCore(
        IServiceCollection services,
        TwitchBotSettings settings,
        TwitchBotPolicies policies,
        TwitchBotAccountProviderRegistration accountProvider,
        TwitchCommandResponseSenderRegistration responseSender
    )
    {
        RegisterSettings(services, settings);
        RegisterPolicies(services, policies);
        RegisterAccountProvider(services, accountProvider);
        RegisterResponseSender(services, responseSender);
        services.AddTwitchAuth();
        services.AddTwitchHelix();
        services.AddContinueAndReportObserverPolicy(TwitchBotObserverPolicyKeys.IrcMessages);
        services.AddContinueAndReportObserverPolicy(TwitchBotObserverPolicyKeys.EventSubMessages);
        services.AddContinueAndReportObserverPolicy(
            TwitchBotObserverPolicyKeys.OutboundQueueAlerts
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<TwitchOutboundDuplicateCooldown>();
        services.TryAddSingleton<TwitchOutboundQueueBacklogMonitor>();
        services.TryAddSingleton<TwitchOutboundQueueAlertDispatcher>();
        services.TryAddSingleton<TwitchOutboundMessageQueue>();
        services.TryAddSingleton<
            ITwitchBotChannelLifecycleNotifier,
            NoOpTwitchBotChannelLifecycleNotifier
        >();
        services.TryAddSingleton<ITwitchChatMessageSender, TwitchChatMessageSender>();
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

    private static TwitchBotAccountProviderRegistration SelectAccountProvider(
        Action<TwitchBotAccountProviderSelection> select
    )
    {
        var selection = new TwitchBotAccountProviderSelection();
        select(selection);
        return selection.RequireSingle();
    }

    private static TwitchCommandResponseSenderRegistration SelectResponseSender(
        Action<TwitchCommandResponseSenderSelection> select
    )
    {
        var selection = new TwitchCommandResponseSenderSelection();
        select(selection);
        return selection.RequireSingle();
    }

    private static void RegisterAccountProvider(
        IServiceCollection services,
        TwitchBotAccountProviderRegistration registration
    )
    {
        switch (registration.Kind)
        {
            case TwitchBotAccountProviderKind.Default:
                services.AddSingleton<
                    ITwitchBotAccountProvider,
                    DefaultTwitchBotAccountProvider
                >();
                return;
            case TwitchBotAccountProviderKind.HostedChannel:
                services.AddSingleton<ITwitchBotAccountProvider>(serviceProvider =>
                    (ITwitchBotAccountProvider)
                        serviceProvider.GetRequiredService(registration.ProviderType)
                );
                return;
            case TwitchBotAccountProviderKind.Custom:
                services.AddSingleton(
                    typeof(ITwitchBotAccountProvider),
                    registration.ProviderType
                );
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(registration),
                    registration.Kind,
                    "Unknown Twitch bot account-provider policy."
                );
        }
    }

    private static void RegisterResponseSender(
        IServiceCollection services,
        TwitchCommandResponseSenderRegistration registration
    )
    {
        switch (registration.Kind)
        {
            case TwitchCommandResponseSenderKind.StandalonePublicChat:
                services.AddSingleton<
                    ITwitchCommandResponseSender,
                    TwitchChatCommandResponseSender
                >();
                return;
            case TwitchCommandResponseSenderKind.HostedWhisper:
                services.AddSingleton<ITwitchCommandResponseSender>(serviceProvider =>
                    (ITwitchCommandResponseSender)
                        serviceProvider.GetRequiredService(registration.SenderType)
                );
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(registration),
                    registration.Kind,
                    "Unknown Twitch command-response sender policy."
                );
        }
    }

    private static void RegisterSettings(
        IServiceCollection services,
        TwitchBotSettings settings
    )
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.Identity);
    }

    private static void RegisterPolicies(
        IServiceCollection services,
        TwitchBotPolicies policies
    )
    {
        services.AddSingleton(policies);
        services.AddKeyedSingleton(
            TwitchBotResiliencePipeline.IrcSession,
            policies.IrcSession
        );
        services.AddKeyedSingleton(
            TwitchBotResiliencePipeline.EventSubSession,
            policies.EventSubSession
        );
        services.AddKeyedSingleton(
            TwitchBotResiliencePipeline.EventSubChannelRecovery,
            policies.EventSubChannelRecovery
        );
        services.AddKeyedSingleton(
            TwitchBotResiliencePipeline.PublicChatDelivery,
            policies.PublicChatRetry
        );
        services.AddSingleton(policies.PublicChatTerminalRetention);
    }
}

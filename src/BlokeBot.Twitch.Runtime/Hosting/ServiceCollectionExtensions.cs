using BlokeBot.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Adds Twitch bot services to an application service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twitch bot runtime and binds options from configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration section that contains bot settings.</param>
    /// <param name="online">Whether to enforce Twitch's public HTTPS webhook boundary.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static IChatBotBuilder AddTwitchBot(
        this IServiceCollection services,
        IConfiguration configuration,
        bool online = true
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options =
            configuration.Get<BotOptions>()
            ?? throw new OptionsValidationException(
                configuration is IConfigurationSection section ? section.Path : "TwitchBot",
                typeof(BotOptions),
                ["Twitch bot configuration is required."]
            );
        var boundary = configuration is IConfigurationSection configuredSection
            ? configuredSection.Path
            : "TwitchBot";
        var settings = BotSettings.FromConfiguredOptions(options, boundary, online);
        var policies = BotPolicies.BindRequired(configuration);

        return AddBotCore(services, settings, policies);
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

        var options = configuration.Get<BotOptions>() ?? new BotOptions();
        RegisterSettings(services, BotSettings.FromOptions(options));
        services.TryAddSingleton<
            IEventSubChannelReconciliationTrigger,
            NoOpEventSubChannelReconciliationTrigger
        >();
        return services;
    }

    /// <summary>
    /// Registers the Twitch bot runtime and configures options with a callback.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The options configuration callback.</param>
    /// <param name="policyOptions">Every required boundary-specific policy.</param>
    /// <param name="online">Whether to enforce Twitch's public HTTPS webhook boundary.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static IChatBotBuilder AddTwitchBot(
        this IServiceCollection services,
        Action<BotOptions> configure,
        BotPolicyOptions policyOptions,
        bool online = true
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(policyOptions);

        var options = new BotOptions();
        configure(options);
        var settings = BotSettings.FromConfiguredOptions(options, "TwitchBot", online);
        var policies = BotPolicies.FromOptions(policyOptions);

        return AddBotCore(services, settings, policies);
    }

    private static IChatBotBuilder AddBotCore(
        IServiceCollection services,
        BotSettings settings,
        BotPolicies policies
    )
    {
        _ =
            settings.EventSubWebhook
            ?? throw new InvalidOperationException(
                "Validated EventSub webhook settings are required by the online runtime."
            );
        RegisterSettings(services, settings);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IRuntimeSessionHealthReporter, RuntimeSessionHealthLogger>();
        services.TryAddSingleton<IRuntimeIdleWait, RuntimeIdleWait>();
        RegisterPolicies(services, policies);
        _ = services.AddSingleton<IBotAccountProvider, DefaultBotAccountProvider>();
        services.TryAddSingleton<
            INativeTwitchFeatureStateProvider,
            EnabledNativeTwitchFeatureStateProvider
        >();
        services.TryAddSingleton<
            IStartupChatMessageProvider,
            ConfiguredStartupChatMessageProvider
        >();
        _ = services.AddSingleton<ICommandResponseSender, PublicChatCommandResponseSender>();
        _ = services.AddSingleton<IBotChannelLifecycleNotifier, NoOpBotChannelLifecycleNotifier>();
        _ = services.AddAuth();
        _ = services.AddHelix();
        _ = services.AddContinueAndReportObserverFanOut<
            IrcMessageObserverBoundary,
            ChatMessage,
            ChatObserverDeadLetter
        >(BotObserverBoundaries.IrcMessages);
        _ = services.AddContinueAndReportObserverFanOut<
            EventSubMessageObserverBoundary,
            ChatMessage,
            ChatObserverDeadLetter
        >(BotObserverBoundaries.EventSubMessages);
        _ = services.AddContinueAndReportObserverFanOut<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >(BotObserverBoundaries.PublicChatQueueAlerts);
        _ = services.AddContinueAndReportObserverFanOut<
            PublicChatTerminalRejectionObserverBoundary,
            PublicChatTerminalRejection,
            PublicChatTerminalRejectionDeadLetter
        >(BotObserverBoundaries.PublicChatTerminalRejections);
        services.TryAddSingleton<PublicChatQueueBacklogMonitor>();
        services.TryAddSingleton<PublicChatQueueAlertDispatcher>();
        services.TryAddSingleton<PublicChatTerminalRejectionDispatcher>();
        services.TryAddSingleton<IPublicChatTransport, HelixPublicChatTransport>();
        services.TryAddSingleton<ChatIdentityResolver>();
        services.TryAddSingleton<PublicChatMessageQueue>();
        _ = services.AddHostedService<PublicChatOutboxWorker>();
        services.TryAddSingleton<IPublicChatPinStore, UnavailablePublicChatPinStore>();
        services.TryAddSingleton<IPublicChatPinProvider, HelixPublicChatPinProvider>();
        _ = services.AddHostedService<PublicChatPinWorker>();
        services.TryAddSingleton<IPublicChatMessageSender, PublicChatMessageSender>();
        _ = services.AddSingleton<BotRuntimeStatusStore>();
        _ = services.AddSingleton<IBotRuntimeStatusAccessor>(static sp =>
            sp.GetRequiredService<BotRuntimeStatusStore>()
        );
        _ = services.AddSingleton<EventSubChannelStatusStore>();
        _ = services.AddSingleton<IEventSubChannelStatusAccessor>(static serviceProvider =>
            serviceProvider.GetRequiredService<EventSubChannelStatusStore>()
        );
        services.TryAddSingleton<EventSubSubscriptionReconciliationStore>();
        services.TryAddSingleton<EventSubChannelReconciliationTrigger>();
        services.TryAddSingleton<IEventSubChannelReconciliationTrigger>(static serviceProvider =>
            serviceProvider.GetRequiredService<EventSubChannelReconciliationTrigger>()
        );
        services.TryAddSingleton<
            IEventSubChannelDiagnosticReporter,
            EventSubChannelDiagnosticLogger
        >();
        services.TryAddSingleton<IEventSubChannelOperations, EventSubChannelOperations>();
        services.TryAddSingleton<EventSubChannelSessionFactory>();
        services.TryAddSingleton<EventSubDeliveryHandler>();
        services.TryAddSingleton<IEventSubDeliveryHandler>(static services =>
            services.GetRequiredService<EventSubDeliveryHandler>()
        );
        services.TryAddSingleton<EventSubSubscriptionVerification>();
        services.TryAddSingleton<IEventSubSubscriptionVerification>(static services =>
            services.GetRequiredService<EventSubSubscriptionVerification>()
        );
        services.TryAddSingleton<EventSubWebhookHandler>();
        services.TryAddSingleton<IEventSubWebhookIngress>(static sp =>
            sp.GetRequiredService<EventSubWebhookHandler>()
        );
        _ = services.AddHostedService(static sp => sp.GetRequiredService<EventSubWebhookHandler>());
        services.TryAddSingleton<EventSubRuntime>();
        services.TryAddSingleton<IIrcConnectionSession, IrcConnectionSession>();
        services.TryAddSingleton<IrcRuntime>();
        _ = services.AddHostedService<BotRuntimeHostedService>();

        return services.AddChatCommands();
    }

    private static void RegisterSettings(IServiceCollection services, BotSettings settings)
    {
        _ = services.AddSingleton(settings);
        _ = services.AddSingleton(settings.Identity);
        if (settings.EventSubWebhook is { } webhook)
        {
            _ = services.AddSingleton(webhook);
        }
    }

    private static void RegisterPolicies(IServiceCollection services, BotPolicies policies)
    {
        _ = services.AddSingleton(policies);
        _ = services.AddKeyedSingleton(BotResiliencePipeline.IrcSession, policies.IrcSession);
        _ = services.AddKeyedSingleton(
            BotResiliencePipeline.EventSubChannelRecovery,
            policies.EventSubChannelRecovery
        );
        _ = services.AddKeyedSingleton(
            BotResiliencePipeline.PublicChatDelivery,
            policies.PublicChatRetry
        );
        _ = services.AddSingleton(policies.PublicChatDeliveryLifetime);
        _ = services.AddSingleton(policies.PublicChatTerminalRetention);
        _ = services.AddResiliencePipeline(
            BotResiliencePipeline.IrcSession,
            (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
                RuntimeSessionResilience.ConfigureIrc(
                    builder,
                    policies.IrcSession,
                    context.ServiceProvider.GetRequiredService<IRuntimeSessionHealthReporter>()
                );
            }
        );
        _ = services.AddResiliencePipeline<
            BotResiliencePipeline,
            EventSubChannelReconciliationOutcome
        >(
            BotResiliencePipeline.EventSubChannelRecovery,
            (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
                EventSubChannelRecoveryResilience.Configure(
                    builder,
                    policies.EventSubChannelRecovery
                );
            }
        );
        _ = services.AddSingleton(serviceProvider => new IrcSessionResiliencePipeline(
            serviceProvider
                .GetRequiredService<ResiliencePipelineProvider<BotResiliencePipeline>>()
                .GetPipeline(BotResiliencePipeline.IrcSession)
        ));
        _ = services.AddSingleton(serviceProvider =>
        {
            var attempt = new ResiliencePipelineBuilder
            {
                TimeProvider = serviceProvider.GetRequiredService<TimeProvider>(),
            };
            EventSubChannelRecoveryResilience.ConfigureAttempt(
                attempt,
                policies.EventSubChannelRecovery
            );
            return new EventSubChannelRecoveryPipeline(
                attempt.Build(),
                serviceProvider
                    .GetRequiredService<ResiliencePipelineProvider<BotResiliencePipeline>>()
                    .GetPipeline<EventSubChannelReconciliationOutcome>(
                        BotResiliencePipeline.EventSubChannelRecovery
                    )
            );
        });
    }
}

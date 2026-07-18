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
    /// <returns>A builder for command and service customization.</returns>
    public static IChatBotBuilder AddTwitchBot(
        this IServiceCollection services,
        IConfiguration configuration
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
        var settings = BotSettings.FromConfiguredOptions(options, boundary);
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
        return services;
    }

    /// <summary>
    /// Registers the Twitch bot runtime and configures options with a callback.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The options configuration callback.</param>
    /// <param name="policyOptions">Every required boundary-specific policy.</param>
    /// <returns>A builder for command and service customization.</returns>
    public static IChatBotBuilder AddTwitchBot(
        this IServiceCollection services,
        Action<BotOptions> configure,
        BotPolicyOptions policyOptions
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(policyOptions);

        var options = new BotOptions();
        configure(options);
        var settings = BotSettings.FromConfiguredOptions(options, "TwitchBot");
        var policies = BotPolicies.FromOptions(policyOptions);

        return AddBotCore(services, settings, policies);
    }

    private static IChatBotBuilder AddBotCore(
        IServiceCollection services,
        BotSettings settings,
        BotPolicies policies
    )
    {
        RegisterSettings(services, settings);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IRuntimeSessionHealthReporter, RuntimeSessionHealthLogger>();
        services.TryAddSingleton<IRuntimeIdleWait, RuntimeIdleWait>();
        RegisterPolicies(services, policies);
        services.AddSingleton<IBotAccountProvider, DefaultBotAccountProvider>();
        services.AddSingleton<ICommandResponseSender, PublicChatCommandResponseSender>();
        services.AddSingleton<IBotChannelLifecycleNotifier, NoOpBotChannelLifecycleNotifier>();
        services.AddAuth();
        services.AddHelix();
        services.AddContinueAndReportObserverFanOut<
            IrcMessageObserverBoundary,
            ChatMessage,
            ChatObserverDeadLetter
        >(BotObserverBoundaries.IrcMessages);
        services.AddContinueAndReportObserverFanOut<
            EventSubMessageObserverBoundary,
            ChatMessage,
            ChatObserverDeadLetter
        >(BotObserverBoundaries.EventSubMessages);
        services.AddContinueAndReportObserverFanOut<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >(BotObserverBoundaries.PublicChatQueueAlerts);
        services.AddContinueAndReportObserverFanOut<
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
        services.AddHostedService<PublicChatOutboxWorker>();
        services.TryAddSingleton<IPublicChatMessageSender, PublicChatMessageSender>();
        services.AddSingleton<BotRuntimeStatusStore>();
        services.AddSingleton<IBotRuntimeStatusAccessor>(sp =>
            sp.GetRequiredService<BotRuntimeStatusStore>()
        );
        services.AddSingleton<EventSubChannelStatusStore>();
        services.AddSingleton<IEventSubChannelStatusAccessor>(serviceProvider =>
            serviceProvider.GetRequiredService<EventSubChannelStatusStore>()
        );
        services.TryAddSingleton<EventSubSubscriptionReconciliationStore>();
        services.TryAddSingleton<
            IEventSubChannelDiagnosticReporter,
            EventSubChannelDiagnosticLogger
        >();
        services.TryAddSingleton<IEventSubChannelOperations, EventSubChannelOperations>();
        services.TryAddSingleton<EventSubChannelSessionFactory>();
        services.TryAddSingleton<IEventSubConnectionSession, EventSubConnectionSession>();
        services.TryAddSingleton<EventSubRuntime>();
        services.TryAddSingleton<IIrcConnectionSession, IrcConnectionSession>();
        services.TryAddSingleton<IrcRuntime>();
        services.AddHostedService<BotRuntimeHostedService>();

        return services.AddChatCommands();
    }

    private static void RegisterSettings(IServiceCollection services, BotSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.Identity);
    }

    private static void RegisterPolicies(IServiceCollection services, BotPolicies policies)
    {
        services.AddSingleton(policies);
        services.AddKeyedSingleton(BotResiliencePipeline.IrcSession, policies.IrcSession);
        services.AddKeyedSingleton(BotResiliencePipeline.EventSubSession, policies.EventSubSession);
        services.AddKeyedSingleton(
            BotResiliencePipeline.EventSubChannelRecovery,
            policies.EventSubChannelRecovery
        );
        services.AddKeyedSingleton(
            BotResiliencePipeline.PublicChatDelivery,
            policies.PublicChatRetry
        );
        services.AddSingleton(policies.PublicChatDeliveryLifetime);
        services.AddSingleton(policies.PublicChatTerminalRetention);
        services.AddResiliencePipeline(
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
        services.AddResiliencePipeline(
            BotResiliencePipeline.EventSubSession,
            (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
                RuntimeSessionResilience.ConfigureEventSub(
                    builder,
                    policies.EventSubSession,
                    context.ServiceProvider.GetRequiredService<IRuntimeSessionHealthReporter>()
                );
            }
        );
        services.AddResiliencePipeline<BotResiliencePipeline, EventSubChannelReconciliationOutcome>(
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
        services.AddSingleton(serviceProvider => new IrcSessionResiliencePipeline(
            serviceProvider
                .GetRequiredService<ResiliencePipelineProvider<BotResiliencePipeline>>()
                .GetPipeline(BotResiliencePipeline.IrcSession)
        ));
        services.AddSingleton(serviceProvider => new EventSubSessionResiliencePipeline(
            serviceProvider
                .GetRequiredService<ResiliencePipelineProvider<BotResiliencePipeline>>()
                .GetPipeline(BotResiliencePipeline.EventSubSession)
        ));
        services.AddSingleton(serviceProvider =>
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

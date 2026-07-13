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

        var options =
            configuration.Get<TwitchBotOptions>()
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

        return AddTwitchBotCore(services, settings, policies);
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
    /// <returns>A builder for command and service customization.</returns>
    public static ITwitchBotBuilder AddTwitchBot(
        this IServiceCollection services,
        Action<TwitchBotOptions> configure,
        TwitchBotPolicyOptions policyOptions
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(policyOptions);

        var options = new TwitchBotOptions();
        configure(options);
        var settings = TwitchBotSettings.FromValidatedOptions(
            options,
            "TwitchBot",
            requireConfiguredIdentity: true
        );
        var policies = TwitchBotPolicies.FromOptions(policyOptions);

        return AddTwitchBotCore(services, settings, policies);
    }

    private static ITwitchBotBuilder AddTwitchBotCore(
        IServiceCollection services,
        TwitchBotSettings settings,
        TwitchBotPolicies policies
    )
    {
        RegisterSettings(services, settings);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<
            ITwitchRuntimeSessionHealthReporter,
            TwitchRuntimeSessionHealthLogger
        >();
        services.TryAddSingleton<ITwitchRuntimeIdleWait, TwitchRuntimeIdleWait>();
        RegisterPolicies(services, policies);
        services.AddSingleton<ITwitchBotAccountProvider, DefaultTwitchBotAccountProvider>();
        services.AddSingleton<ITwitchCommandResponseSender, TwitchChatCommandResponseSender>();
        services.AddSingleton<
            ITwitchBotChannelLifecycleNotifier,
            NoOpTwitchBotChannelLifecycleNotifier
        >();
        services.AddAuth();
        services.AddHelix();
        services.AddContinueAndReportObserverFanOut<
            TwitchIrcMessageObserverBoundary,
            TwitchChatMessage,
            TwitchChatObserverDeadLetter
        >(TwitchBotObserverBoundaries.IrcMessages);
        services.AddContinueAndReportObserverFanOut<
            EventSubMessageObserverBoundary,
            TwitchChatMessage,
            TwitchChatObserverDeadLetter
        >(TwitchBotObserverBoundaries.EventSubMessages);
        services.AddContinueAndReportObserverFanOut<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >(TwitchBotObserverBoundaries.PublicChatQueueAlerts);
        services.TryAddSingleton<PublicChatQueueBacklogMonitor>();
        services.TryAddSingleton<PublicChatQueueAlertDispatcher>();
        services.TryAddSingleton<IPublicChatTransport, TwitchHelixPublicChatTransport>();
        services.TryAddSingleton<ChatIdentityResolver>();
        services.TryAddSingleton<PublicChatMessageQueue>();
        services.AddHostedService<PublicChatOutboxWorker>();
        services.TryAddSingleton<ITwitchChatMessageSender, TwitchChatMessageSender>();
        services.AddSingleton<TwitchBotRuntimeStatusStore>();
        services.AddSingleton<ITwitchBotRuntimeStatusAccessor>(sp =>
            sp.GetRequiredService<TwitchBotRuntimeStatusStore>()
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
        services.TryAddSingleton<ITwitchIrcConnectionSession, TwitchIrcConnectionSession>();
        services.TryAddSingleton<TwitchIrcRuntime>();
        services.AddHostedService<TwitchBotRuntimeHostedService>();

        return services.AddTwitchCommands();
    }

    private static void RegisterSettings(IServiceCollection services, TwitchBotSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.Identity);
    }

    private static void RegisterPolicies(IServiceCollection services, TwitchBotPolicies policies)
    {
        services.AddSingleton(policies);
        services.AddKeyedSingleton(TwitchBotResiliencePipeline.IrcSession, policies.IrcSession);
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
        services.AddSingleton(policies.PublicChatDeliveryLifetime);
        services.AddSingleton(policies.PublicChatTerminalRetention);
        services.AddResiliencePipeline(
            TwitchBotResiliencePipeline.IrcSession,
            (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
                TwitchRuntimeSessionResilience.ConfigureIrc(
                    builder,
                    policies.IrcSession,
                    context.ServiceProvider.GetRequiredService<ITwitchRuntimeSessionHealthReporter>()
                );
            }
        );
        services.AddResiliencePipeline(
            TwitchBotResiliencePipeline.EventSubSession,
            (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
                TwitchRuntimeSessionResilience.ConfigureEventSub(
                    builder,
                    policies.EventSubSession,
                    context.ServiceProvider.GetRequiredService<ITwitchRuntimeSessionHealthReporter>()
                );
            }
        );
        services.AddResiliencePipeline<
            TwitchBotResiliencePipeline,
            EventSubChannelReconciliationOutcome
        >(
            TwitchBotResiliencePipeline.EventSubChannelRecovery,
            (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
                EventSubChannelRecoveryResilience.Configure(
                    builder,
                    policies.EventSubChannelRecovery
                );
            }
        );
        services.AddSingleton(serviceProvider => new TwitchIrcSessionResiliencePipeline(
            serviceProvider
                .GetRequiredService<ResiliencePipelineProvider<TwitchBotResiliencePipeline>>()
                .GetPipeline(TwitchBotResiliencePipeline.IrcSession)
        ));
        services.AddSingleton(serviceProvider => new EventSubSessionResiliencePipeline(
            serviceProvider
                .GetRequiredService<ResiliencePipelineProvider<TwitchBotResiliencePipeline>>()
                .GetPipeline(TwitchBotResiliencePipeline.EventSubSession)
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
                    .GetRequiredService<ResiliencePipelineProvider<TwitchBotResiliencePipeline>>()
                    .GetPipeline<EventSubChannelReconciliationOutcome>(
                        TwitchBotResiliencePipeline.EventSubChannelRecovery
                    )
            );
        });
    }
}

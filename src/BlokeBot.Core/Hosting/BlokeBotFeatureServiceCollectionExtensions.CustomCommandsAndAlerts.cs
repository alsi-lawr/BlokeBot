using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotCustomCommands(
        this IServiceCollection services,
        CustomAnnouncementDeliveryMode announcementDelivery
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<CustomCommandAliasRegistry>();
        _ = services.AddSingleton<CustomCommandCooldownStore>();
        _ = services.AddSingleton<CustomCommandExecutionService>();
        services.TryAddSingleton<
            ICustomCommandAutomationRuntime,
            UnavailableCustomCommandAutomationRuntime
        >();
        services.TryAddSingleton<
            IOverlayCueAdmissionService,
            UnavailableOverlayCueAdmissionService
        >();
        _ = services.AddSingleton<CustomCommandInvocationClaimStore>();
        _ = services.AddSingleton<CustomCommandInvocationResetService>();
        services.TryAddSingleton<
            ICustomCommandViewerResolver,
            UnavailableCustomCommandViewerResolver
        >();
        services.TryAddSingleton<
            IHostStreamLivenessProvider,
            UnavailableCustomCommandStreamLivenessProvider
        >();
        _ = services.AddSingleton<CustomMessageSelector>();
        services.TryAddSingleton<
            IMessageLibraryRandomSource,
            CryptographicMessageLibraryRandomSource
        >();
        services.TryAddSingleton<IMessageLibraryChatterSource, MessageLibraryChatterSource>();
        _ = services.AddSingleton<CustomCommandTemplateRenderer>();
        _ = services.AddSingleton<CustomCommandConfigurationGraphWriter>();
        _ = services.AddSingleton<CustomCommandConfigurationService>();
        _ = services.AddSingleton<HostCustomCommandSettingsService>();
        _ = services.AddSingleton<TwitchAnnouncementAccessService>();
        _ = services.AddSingleton<ITwitchAnnouncementAccessService>(static serviceProvider =>
            serviceProvider.GetRequiredService<TwitchAnnouncementAccessService>()
        );
        _ = services.AddSingleton<ITwitchAnnouncementReadinessProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<TwitchAnnouncementAccessService>()
        );
        services.TryAddSingleton<
            ICustomAnnouncementTickScheduler,
            TimeProviderCustomAnnouncementTickScheduler
        >();
        switch (announcementDelivery)
        {
            case CustomAnnouncementDeliveryMode.Disabled:
                _ = services.AddSingleton<
                    ICustomAnnouncementSender,
                    DisabledCustomAnnouncementSender
                >();
                break;
            case CustomAnnouncementDeliveryMode.PublicChat:
                _ = services.AddSingleton<
                    ICustomAnnouncementSender,
                    TwitchAnnouncementCustomAnnouncementSender
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(announcementDelivery),
                    announcementDelivery,
                    "Unknown custom-announcement delivery mode."
                );
        }
        _ = services.AddSingleton<IChatMessageObserver, CustomAnnouncementChatActivity>();
        _ = services.AddSingleton<CustomAnnouncementScheduler>();
        _ = services.AddHostedService(static sp =>
            sp.GetRequiredService<CustomAnnouncementScheduler>()
        );
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotAlerts(this IServiceCollection services)
    {
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPublicChatQueueAlertObserver,
                DurablePublicChatQueueAlertObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPublicChatTerminalRejectionObserver,
                DurableFollowerOnlyChatAlertObserver
            >()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}

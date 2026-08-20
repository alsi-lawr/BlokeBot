using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.BotStatus;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.BlokeRaid;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Eventing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotApplication
{
    public static WebApplicationBuilder AddBlokeBotCore(
        this WebApplicationBuilder builder,
        BlokeBotRuntimeMode runtime
    )
    {
        BlokeBotLogging.Configure(builder.Logging);
        _ = builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        _ = builder.Services.AddCascadingAuthenticationState();
        _ = builder.Services.AddHttpContextAccessor();
        _ = builder.Services.AddSingleton(BlokeBotBuildIdentity.Current);
        _ = builder.Services.AddSingleton<UiFaultTelemetry>();
        _ = builder.Services.AddScoped<DashboardFragmentState>();

        _ = builder
            .Services.AddOptions<BlokeBotOptions>()
            .BindConfiguration("BlokeBot")
            .Validate(BlokeBotOptionsValidation.IsValid, "BlokeBot options are invalid.")
            .ValidateOnStart();
        _ = builder
            .Services.AddOptions<WebAuthOptions>()
            .BindConfiguration("TwitchWebAuth")
            .ValidateOnStart();
        var privacy = builder
            .Services.AddOptions<PrivacyNoticeOptions>()
            .BindConfiguration("BlokeBotPrivacy")
            .Validate(
                PrivacyNoticeOptionsValidation.HasValidNoticeUrlWhenConfigured,
                PrivacyNoticeOptionsValidation.NoticeUrlFailure
            )
            .ValidateOnStart();
        if (
            PrivacyNoticeOptionsValidation.RequiredFor(
                runtime == BlokeBotRuntimeMode.Online,
                builder.Environment.EnvironmentName
            )
        )
        {
            _ = privacy.Validate(
                PrivacyNoticeOptionsValidation.IsComplete,
                PrivacyNoticeOptionsValidation.RequiredFailure
            );
        }
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        var twitchEndpoints =
            builder
                .Configuration.GetSection(TwitchEndpointPolicy.ConfigurationSectionName)
                .Get<TwitchEndpointPolicy>()
            ?? new TwitchEndpointPolicy();
        twitchEndpoints.Validate();
        _ = builder.Services.AddSingleton(twitchEndpoints);

        var online = runtime == BlokeBotRuntimeMode.Online;
        _ = builder.Services.AddEventBus<AppEventKind>(
            ObserverBoundary.Named("BlokeBot.ApplicationEvents"),
            static eventKind => ObserverEventIdentity.Named($"BlokeBot.{eventKind}")
        );
        _ = builder
            .Services.AddBlokeBotAppCommands()
            .AddBlokeBotPublicChat()
            .AddBlokeBotAlerts()
            .AddBlokeBotCustomCommands(
                online
                    ? CustomAnnouncementDeliveryMode.PublicChat
                    : CustomAnnouncementDeliveryMode.Disabled
            )
            .AddBlokeBotSiteAccess(
                online
                    ? AccessListProfileEnrichmentMode.Twitch
                    : AccessListProfileEnrichmentMode.Disabled
            )
            .AddBlokeBotAdmin(
                online ? BotAccountAuthorizationMode.Twitch : BotAccountAuthorizationMode.Disabled
            )
            .AddBlokeBotHostedChannels(
                online ? HostBotAppAccessTokenMode.Twitch : HostBotAppAccessTokenMode.Unavailable
            )
            .AddBlokeBotConfigurationTransfer()
            .AddBlokeBotAutomations()
            .AddBlokeBotHosts()
            .AddBlokeBotGuessing()
            .AddBlokeBotPoints(
                online
                    ? PointsGiveawayNotificationMode.PublicChat
                    : PointsGiveawayNotificationMode.ReplyOnly
            )
            .AddBlokeBotBounties()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotViewerPassports()
            .AddBlokeBotBingo()
            .AddBlokeBotBlokeRaid()
            .AddBlokeBotCompetitions()
            .AddBlokeBotCollectives()
            .AddBlokeBotRequestBoards()
            .AddBlokeBotPlayWithViewers()
            .AddBlokeBotMoments()
            .AddBlokeBotOverlays()
            .AddBlokeBotToasts()
            .AddBlokeBotTwitchOperations()
            .AddBlokeBotAuth();
        _ = builder.Services.AddOAuthTransport();
        _ = builder.Services.AddHelix();
        _ = builder.Services.AddHttpClient();
        AddAuthentication(builder);

        var botSection = builder.Configuration.GetSection("TwitchBot");
        if (online)
        {
            _ = builder
                .Services.AddTwitchBot(
                    botSection,
                    online: online && !builder.Environment.IsEnvironment("Simulation")
                )
                .UseBlokeBotHostedChannelProvider()
                .UseWhisperCommandResponseSender()
                .UseBlokeBotHostedChannelLifecycleNotifier()
                .AddCommandModule<ViewerCommandCatalogModule>()
                .AddCommandModule<CommandStrategyModule<GuessCommandKind, AppCommandRouteState>>()
                .AddCommandModule<CommandStrategyModule<PointsCommandKind, AppCommandRouteState>>()
                .AddCommandModule<BountyCommandModule>()
                .AddCommandModule<CommunityProgressionCommandModule>()
                .AddCommandModule<ViewerPassportCommandModule>()
                .AddCommandModule<BingoCommandModule>()
                .AddCommandModule<BlokeRaidCommandModule>()
                .AddCommandModule<CompetitionCommandModule>()
                .AddCommandModule<CollectiveCommandModule>()
                .AddCommandModule<RequestBoardCommandModule>()
                .AddCommandModule<PlayQueueCommandModule>()
                .AddCommandModule<MomentCommandModule>()
                .AddCommandModule<CustomCommandModule>();
        }
        else
        {
            _ = builder.Services.AddTwitchBotSettings(botSection);
            _ = builder.Services.AddUnavailableAccessTokenProvider();
            _ = builder.Services.AddOfflineBotRuntimeStatus();
            _ = builder.Services.Replace(
                ServiceDescriptor.Singleton<IPointTargetUserLookup, OfflinePointTargetUserLookup>()
            );
            _ = builder.Services.Replace(
                ServiceDescriptor.Singleton<
                    IPublicChatMessageSender,
                    OfflinePublicChatMessageSender
                >()
            );
        }

        return builder;
    }
}

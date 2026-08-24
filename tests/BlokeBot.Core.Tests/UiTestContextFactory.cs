using System.Security.Claims;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.MomentAttachments;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Plugins.Features;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Tests;

internal static class UiTestContextFactory
{
    public static BunitContext Create(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string hostLogin = "streamer"
    ) => CreateWithAuthorization(dbFactory, hostId, hostLogin).Context;

    public static UiTestContext CreateWithAuthorization(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string hostLogin = "streamer"
    )
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var events = TestEventBus.Create<AppEventKind>();

        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        _ = context.Services.AddSingleton(events);
        _ = context.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero))
        );
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.Services.AddSingleton(BlokeBotBuildIdentity.Current);
        _ = context.Services.AddSingleton<UiFaultTelemetry>();
        var declarations = new PluginFeatureDeclarationRegistry();
        var snapshots = new PluginFeatureSnapshotRegistry();
        _ = context.Services.AddSingleton<IPluginFeatureDeclarationProvider>(declarations);
        _ = context.Services.AddSingleton<IPluginFeatureSnapshotProvider>(snapshots);
        _ = context.Services.AddScoped<DashboardFragmentState>();
        _ = context.Services.AddSingleton<HostedChannelChangeNotifier>();
        _ = context.Services.AddSingleton<HostFeatureService>();
        AddMomentAttachmentServices(context, dbFactory);
        _ = context.Services.AddBlokeBotAlerts();
        _ = context.Services.AddSingleton<IMessageLibraryChatterSource>(
            new UnavailableMessageLibraryChatterSource()
        );
        _ = context.Services.AddBlokeBotCustomCommands(CustomAnnouncementDeliveryMode.Disabled);
        _ = context.Services.AddSingleton<ITwitchAnnouncementReadinessProvider>(
            new UnavailableTwitchAnnouncementReadinessProvider()
        );
        _ = context.Services.AddBlokeBotToasts();

        var host = new BotHostChoice(hostId, hostLogin, "Streamer", AuthRole.Streamer);
        var authorization = context.AddAuthorization();
        _ = authorization.SetAuthorized(hostLogin);
        _ = authorization.SetPolicies("HostSelected", "Operator");
        _ = authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, $"{hostLogin}-id"),
            new Claim(ClaimTypes.Name, hostLogin),
            new Claim(AuthClaims.Login, hostLogin),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Streamer)),
            new Claim(AuthClaims.CanCreateHost, "false"),
            new Claim(AuthClaims.IsBotAdmin, "false"),
            new Claim(AuthClaims.IsBotAccount, "false"),
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host))
        );
        return new UiTestContext(context, authorization);
    }

    public static void AddMomentAttachmentServices(
        BunitContext context,
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        context.Services.TryAddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        context.Services.TryAddSingleton(TestEventBus.Create<AppEventKind>());
        context.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        context.Services.TryAddSingleton<IModeratorAuthorityService>(
            new GrantedModeratorAuthorityService()
        );
        context.Services.TryAddSingleton<MomentAttachmentService>();
    }

    internal sealed record UiTestContext(
        BunitContext Context,
        BunitAuthorizationContext Authorization
    );

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class UnavailableTwitchAnnouncementReadinessProvider
        : ITwitchAnnouncementReadinessProvider
    {
        public Task<TwitchAnnouncementReadiness> GetReadinessAsync(
            string channelLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new TwitchAnnouncementReadiness(TwitchAnnouncementAvailability.Unavailable, "bot")
            );
    }

    private sealed class GrantedModeratorAuthorityService : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }
}

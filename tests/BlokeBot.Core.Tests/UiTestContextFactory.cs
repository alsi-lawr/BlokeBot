using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        _ = context.Services.AddScoped<DashboardFragmentState>();
        _ = context.Services.AddSingleton<HostedChannelChangeNotifier>();
        _ = context.Services.AddSingleton<HostFeatureService>();
        _ = context.Services.AddBlokeBotAlerts();
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
}

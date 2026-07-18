using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
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
    )
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var events = TestEventBus.Create<AppEventKind>();

        context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        context.Services.AddSingleton(events);
        context.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero))
        );
        context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        context.Services.AddSingleton<UiFaultTelemetry>();
        context.Services.AddSingleton<HostedChannelChangeNotifier>();
        context.Services.AddSingleton<HostFeatureService>();
        context.Services.AddBlokeBotAlerts();
        context.Services.AddBlokeBotCustomCommands(CustomAnnouncementDeliveryMode.Disabled);
        context.Services.AddSingleton<ITwitchAnnouncementReadinessProvider>(
            new UnavailableTwitchAnnouncementReadinessProvider()
        );
        context.Services.AddBlokeBotToasts();

        var host = new BotHostChoice(hostId, hostLogin, "Streamer", AuthRole.Streamer);
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized(hostLogin);
        authorization.SetPolicies("HostSelected", "Operator");
        authorization.SetClaims(
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
        return context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class UnavailableTwitchAnnouncementReadinessProvider
        : ITwitchAnnouncementReadinessProvider
    {
        public Task<TwitchAnnouncementReadiness> GetReadinessAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new TwitchAnnouncementReadiness(TwitchAnnouncementAvailability.Unavailable, "bot")
            );
        }
    }
}

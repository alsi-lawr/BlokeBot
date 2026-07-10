using System.Security.Claims;
using BlokeBot.Auth.Sessions;
using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.Toasts;
using BlokeBot.Hosting;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.UI.Tests;

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
        var events = new EventBus<AppEventKind>();

        context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        context.Services.AddSingleton(events);
        context.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero))
        );
        context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        context.Services.AddSingleton<HostedChannelChangeNotifier>();
        context.Services.AddSingleton<HostFeatureService>();
        context.Services.AddBlokeBotAlerts();
        context.Services.AddBlokeBotCustomCommands();
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
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelConnectionStatusUiTests
{
    [Test]
    public void HostedChannelRow_RenderingChannelAuthorization_ShowsTwitchAccessStatus()
    {
        using var context = new BunitContext();

        var saved = context.Render<HostedChannelRow>(static parameters =>
            parameters.Add(
                static component => component.Host,
                new HostedChannelAdminView(
                    1,
                    "streamer",
                    "Streamer",
                    null,
                    true,
                    new HostedChannelRuntimeLifecycle.Stopped(null)
                )
            )
        );
        var needed = context.Render<HostedChannelRow>(static parameters =>
            parameters.Add(
                static component => component.Host,
                new HostedChannelAdminView(
                    2,
                    "otherstreamer",
                    "Other Streamer",
                    null,
                    false,
                    new HostedChannelRuntimeLifecycle.Stopped(null)
                )
            )
        );

        saved.Markup.ShouldContain("Twitch access saved");
        saved.Markup.ShouldNotContain("chat connected");
        needed.Markup.ShouldContain("Twitch access needed");
        needed.Markup.ShouldNotContain("chat not connected");
    }

    [Test]
    public async Task SelectedChannelStatus_WhenReconnectIsNeeded_OnlyShowsReconnectToChannelOwner()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedStaleAuthorizationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        ConfigureHostedChannelServices(context);
        var owner = context.Render<SelectedChannelBotStatus>(parameters =>
            parameters.Add(
                component => component.Session,
                Session(hostId, "streamer", AuthRole.Streamer)
            )
        );
        var moderator = context.Render<SelectedChannelBotStatus>(parameters =>
            parameters.Add(
                component => component.Session,
                Session(hostId, "moderator", AuthRole.Moderator)
            )
        );

        owner.Markup.ShouldContain("Reconnect bot");
        moderator.Markup.ShouldContain("Channel owner needs to reconnect the bot");
        moderator.FindAll("button").ShouldBeEmpty();
    }

    private static void ConfigureHostedChannelServices(BunitContext context)
    {
        _ = context.Services.AddSingleton<IOptions<BlokeBotOptions>>(
            Options.Create(new BlokeBotOptions())
        );
        _ = context.Services.AddSingleton(BotSettings.FromOptions(new BotOptions()));
        _ = context.Services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["TwitchBot:ChannelAuthorization:Scopes:0"] = "chat:read",
                    }
                )
                .Build()
        );
        _ = context.Services.AddOAuthTransport();
        _ = context.Services.AddHelix();
        _ = context.Services.AddBlokeBotSiteAccess(AccessListProfileEnrichmentMode.Disabled);
        _ = context.Services.AddBlokeBotAdmin(BotAccountAuthorizationMode.Disabled);
        _ = context.Services.AddBlokeBotHostedChannels(HostBotAppAccessTokenMode.Unavailable);
        _ = context.Services.AddBlokeBotHosts();
        _ = context.Services.AddTransient<ChannelBotOAuthService>();
    }

    private static AuthenticatedSession Session(int hostId, string login, AuthRole role)
    {
        var channel = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new AuthenticatedSession
        {
            Login = login,
            State = new AuthSessionState.Selected(new BotHostSelection(channel, [channel])),
        };
    }

    private static async Task<int> SeedStaleAuthorizationAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
            ChannelBotAuthorizedAtUtc = DateTime.UtcNow,
            ChannelBotAuthorizedScopes = string.Empty,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}

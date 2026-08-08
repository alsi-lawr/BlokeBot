using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelConnectionStatusUiTests
{
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

using System.Net;
using System.Text;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class AuthorizedHostSelectionServiceTests
{
    [Test]
    public async Task SelfAndModeratedHosts_LoadingAuthorizedSelection_ReturnsSelfAndAllowedModeratedHosts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer", "Streamer");
        await SeedHostAsync(dbFactory, "allowed", "Allowed");
        var blockedHostId = await SeedHostAsync(dbFactory, "blocked", "Blocked");
        var events = TestEventBus.Create<AppEventKind>();
        var modAccess = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(events)
        );
        await modAccess.AddEntryAsync(
            blockedHostId,
            AccessListEntryKind.Blacklist,
            "streamer",
            CancellationToken.None
        );
        var webAuth = new WebAuthConfiguration(
            Options.Create(new WebAuthOptions()),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            )
        );
        var service = new AuthorizedHostSelectionService(
            dbFactory,
            new SiteAccessService(
                dbFactory,
                new BotAdminService(BotAdminSettings.FromOptions(new BlokeBotOptions())),
                new SiteAccessChangeNotifier(events)
            ),
            modAccess,
            new ModeratedChannelLookupService(
                webAuth,
                new HelixClient(
                    new JsonHttpClientFactory(
                        """
                        {
                          "data": [
                            { "broadcaster_login": "allowed" },
                            { "broadcaster_login": "blocked" },
                            { "broadcaster_login": "streamer" }
                          ],
                          "pagination": {}
                        }
                        """
                    )
                ,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
            )
        );

        var result = await service.LoadAuthorizedHostsAsync(
            "token",
            "user-id",
            "streamer",
            CancellationToken.None
        );

        result.CanCreateHost.ShouldBeTrue();
        result.Choices.Select(x => x.Login).ShouldBe(["streamer", "allowed"]);
        result.Choices.Single(x => x.Login == "streamer").Role.ShouldBe(AuthRole.Streamer);
        result.Choices.Single(x => x.Login == "allowed").Role.ShouldBe(AuthRole.Moderator);
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        string displayName
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class JsonHttpClientFactory(string response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new JsonHttpMessageHandler(response));
        }
    }

    private sealed class JsonHttpMessageHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(response, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}

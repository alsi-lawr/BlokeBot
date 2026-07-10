using System.Net;
using System.Text;
using BlokeBot;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class ChannelBotAuthorizationTests
{
    [Test]
    public async Task ReturnedOAuthToken_CompletingChannelAuthorization_ValidatesAndNormalizesGrant()
    {
        var httpClientFactory = new TwitchOAuthHttpClientFactory();
        var service = new ChannelBotOAuthService(
            ConfigurationWithScopes("channel:bot"),
            new TwitchOAuthApiClient(httpClientFactory)
        );

        var grant = await service.CompleteAsync(TwitchRequest(), "code", CancellationToken.None);

        grant.UserId.ShouldBe("123");
        grant.Login.ShouldBe(LoginName.Parse("streamer"));
        grant.Scopes.ShouldBe(["bits:read", "channel:bot"], ignoreOrder: true);
        httpClientFactory.ValidatedToken.ShouldBe("grant-token");
    }

    [Test]
    public async Task GrantForDifferentAccount_AuthorizingChannel_RejectsWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "123", "streamer");
        var service = ChannelAuthorizationService(dbFactory, "channel:bot");

        var result = await service.AuthorizeAsync(
            hostId,
            Grant("999", "other", "channel:bot"),
            CancellationToken.None
        );

        result.Succeeded.ShouldBeFalse();
        var host = await LoadHostAsync(dbFactory, hostId);
        host.ChannelBotAuthorizedAtUtc.ShouldBeNull();
        host.ChannelBotAuthorizedScopes.ShouldBeNull();
    }

    [Test]
    public async Task GrantMissingRequiredScopes_AuthorizingChannel_RejectsAndReportsMissingScopes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "123", "streamer");
        var service = ChannelAuthorizationService(dbFactory, "channel:bot", "bits:read");

        var result = await service.AuthorizeAsync(
            hostId,
            Grant("123", "streamer", "channel:bot"),
            CancellationToken.None
        );

        result.Succeeded.ShouldBeFalse();
        result.MissingScopes.ShouldBe(["bits:read"]);
        var host = await LoadHostAsync(dbFactory, hostId);
        host.ChannelBotAuthorizedAtUtc.ShouldBeNull();
        host.ChannelBotAuthorizedScopes.ShouldBeNull();
    }

    [Test]
    public async Task ValidChannelGrant_AuthorizingChannel_PersistsNormalizedScopes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "123", "streamer");
        var service = ChannelAuthorizationService(dbFactory, "channel:bot");

        var result = await service.AuthorizeAsync(
            hostId,
            Grant("123", "STREAMER", "channel:bot", "bits:read"),
            CancellationToken.None
        );

        result.Succeeded.ShouldBeTrue();
        var host = await LoadHostAsync(dbFactory, hostId);
        host.ChannelBotAuthorizedAtUtc.ShouldNotBeNull();
        host.ChannelBotAuthorizedScopes.ShouldBe("bits:read channel:bot");
        service
            .IsCurrent(host.ChannelBotAuthorizedAtUtc, host.ChannelBotAuthorizedScopes)
            .ShouldBeTrue();
    }

    [Test]
    public async Task StaleChannelScopes_StartingRuntime_RejectsAndKeepsStopped()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            dbFactory,
            "123",
            "streamer",
            authorizedScopes: "bits:read"
        );
        var authorization = ChannelAuthorizationService(dbFactory, "channel:bot");
        var runtime = new HostedChannelRuntimeControlService(
            dbFactory,
            ChangeNotifier(),
            authorization,
            HostBotAccounts(dbFactory),
            Options.Create(new BlokeBotOptions { BotStateChangeCooldownSeconds = 0 })
        );

        var result = await runtime.StartAsync(hostId, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        var host = await LoadHostAsync(dbFactory, hostId);
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
    }

    private static ChannelBotAuthorizationGrant Grant(
        string userId,
        string login,
        params string[] scopes
    ) => new(userId, LoginName.Parse(login), scopes.ToHashSet(StringComparer.Ordinal));

    private static ChannelBotAuthorizationService ChannelAuthorizationService(
        SqliteBlokeBotDbFactory dbFactory,
        params string[] scopes
    ) => new(dbFactory, ChangeNotifier(), ChannelOAuthService(scopes));

    private static ChannelBotOAuthService ChannelOAuthService(params string[] scopes)
    {
        var httpClientFactory = new EmptyHttpClientFactory();
        return new(ConfigurationWithScopes(scopes), new TwitchOAuthApiClient(httpClientFactory));
    }

    private static HostBotAccountAuthorizationService HostBotAccounts(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var httpClientFactory = new EmptyHttpClientFactory();
        var options = Options.Create(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                },
            }
        );
        var oauth = new TwitchOAuthApiClient(httpClientFactory);
        var helix = new TwitchHelixApiClient(httpClientFactory);
        var services = new ServiceCollection().BuildServiceProvider();
        return new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helix),
            oauth,
            helix,
            new TwitchTokenStatusService(services, oauth),
            ChangeNotifier(),
            options
        );
    }

    private static HostedChannelChangeNotifier ChangeNotifier() =>
        new(new EventBus<AppEventKind>());

    private static IConfiguration ConfigurationWithScopes(params string[] scopes)
    {
        var values = new Dictionary<string, string?>
        {
            ["TwitchBot:Identity:ClientId"] = "client",
            ["TwitchBot:Identity:ClientSecret"] = "secret",
        };

        for (var i = 0; i < scopes.Length; i++)
            values[$"TwitchBot:ChannelAuthorization:Scopes:{i}"] = scopes[i];

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpRequest TwitchRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost:7107");
        return context.Request;
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string twitchUserId,
        string login,
        string? authorizedScopes = null
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = twitchUserId,
            Login = login,
            DisplayName = login,
            ChannelBotAuthorizedAtUtc = authorizedScopes is null ? null : DateTime.UtcNow,
            ChannelBotAuthorizedScopes = authorizedScopes,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<BotHost> LoadHostAsync(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Hosts.SingleAsync(x => x.Id == hostId);
    }

    private sealed class TwitchOAuthHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler handler = new();

        public string? ValidatedToken => handler.ValidatedToken;

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

        private sealed class Handler : HttpMessageHandler
        {
            public string? ValidatedToken { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (request.RequestUri?.AbsolutePath == "/oauth2/token")
                {
                    return Task.FromResult(JsonResponse("""{"access_token":"grant-token"}"""));
                }

                if (request.RequestUri?.AbsolutePath == "/oauth2/validate")
                {
                    ValidatedToken = request.Headers.Authorization?.Parameter;
                    return Task.FromResult(
                        JsonResponse(
                            """
                            {"user_id":"123","login":"Streamer","scopes":["channel:bot","bits:read"]}
                            """
                        )
                    );
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

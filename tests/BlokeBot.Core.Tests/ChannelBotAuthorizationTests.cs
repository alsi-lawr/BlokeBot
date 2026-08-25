using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ChannelBotAuthorizationTests
{
    [Test]
    public async Task ReturnedOAuthToken_CompletingChannelAuthorization_ValidatesAndNormalizesGrant()
    {
        var httpClientFactory = new RecordingOAuthHttpClientFactory();
        var service = new ChannelBotOAuthService(
            ConfigurationWithScopes("channel:bot"),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );

        var grant = (await service.CompleteAsync(TwitchRequest(), "code", CancellationToken.None))
            .ShouldBeOfType<OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>.Completed>()
            .Grant;

        grant.UserId.ShouldBe("123");
        grant.Login.ShouldBe(LoginName.Parse("streamer"));
        grant.Scopes.ShouldBe(["bits:read", "channel:bot"], ignoreOrder: true);
        httpClientFactory.ValidatedToken.ShouldBe("grant-token");
    }

    [Test]
    public async Task MissingCredentials_CompletingChannelAuthorization_ReturnsTypedUnavailable()
    {
        var service = new ChannelBotOAuthService(
            new ConfigurationBuilder().Build(),
            new OAuthTransport(
                new EmptyHttpClientFactory(),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );

        var outcome = await service.CompleteAsync(TwitchRequest(), "code", CancellationToken.None);

        _ =
            outcome.ShouldBeOfType<OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>.ConfigurationUnavailable>();
    }

    [Test]
    public async Task ProviderRejectedToken_CompletingChannelAuthorization_ReturnsTypedRejection()
    {
        var service = new ChannelBotOAuthService(
            ConfigurationWithScopes("channel:bot"),
            new OAuthTransport(
                new RecordingOAuthHttpClientFactory(HttpStatusCode.Unauthorized),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );

        var outcome = await service.CompleteAsync(TwitchRequest(), "code", CancellationToken.None);

        _ =
            outcome.ShouldBeOfType<OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>.ProviderNotValidated>();
    }

    [Test]
    public async Task GrantForDifferentAccount_AuthorizingChannel_RejectsWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "123", "streamer");
        var service = ChannelAuthorizationService(dbFactory, "channel:bot");

        var result = await service
            .Authorize(hostId, Grant("999", "other", "channel:bot"))
            .RunAsync(CancellationToken.None);

        _ = result.ShouldBeOfType<ChannelBotAuthorizationOutcome.GrantMismatch>();
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

        var result = await service
            .Authorize(hostId, Grant("123", "streamer", "channel:bot"))
            .RunAsync(CancellationToken.None);

        result
            .ShouldBeOfType<ChannelBotAuthorizationOutcome.MissingScopes>()
            .Scopes.ShouldBe(["bits:read"]);
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

        var result = await service
            .Authorize(hostId, Grant("123", "STREAMER", "channel:bot", "bits:read"))
            .RunAsync(CancellationToken.None);

        _ = result.ShouldBeOfType<ChannelBotAuthorizationOutcome.Authorized>();
        var host = await LoadHostAsync(dbFactory, hostId);
        _ = host.ChannelBotAuthorizedAtUtc.ShouldNotBeNull();
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
        var changes = ChangeNotifier();
        var runtime = new HostedChannelRuntimeControlService(
            dbFactory,
            authorization,
            HostBotAccounts(dbFactory),
            Options.Create(new BlokeBotOptions { BotStateChangeCooldownSeconds = 0 }),
            new HostedChannelRuntimeTransitionService(dbFactory, changes)
        );

        var result = await runtime.Start(hostId).RunAsync(CancellationToken.None);

        _ =
            result.ShouldBeOfType<HostedChannelRuntimeControlOutcome.ChannelAuthorizationRequired>();
        var host = await LoadHostAsync(dbFactory, hostId);
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
    }

    [Test]
    public async Task AuthorizedChannel_StartAndStop_ExposeTransitionsUntilRuntimeConfirmsEffects()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            dbFactory,
            "123",
            "streamer",
            authorizedScopes: "channel:bot"
        );
        var changes = ChangeNotifier();
        var transitions = new HostedChannelRuntimeTransitionService(dbFactory, changes);
        var runtime = new HostedChannelRuntimeControlService(
            dbFactory,
            ChannelAuthorizationService(dbFactory, "channel:bot"),
            HostBotAccounts(dbFactory),
            Options.Create(new BlokeBotOptions { BotStateChangeCooldownSeconds = 0 }),
            transitions
        );
        var lifecycle = new HostedChannelRuntimeLifecycleService(transitions);

        _ = (
            await runtime.Start(hostId).RunAsync(CancellationToken.None)
        ).ShouldBeOfType<HostedChannelRuntimeControlOutcome.Accepted>();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Starting
        );
        await lifecycle.MarkStartedAsync("streamer", CancellationToken.None);
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Started
        );
        _ = (
            await runtime.Stop(hostId).RunAsync(CancellationToken.None)
        ).ShouldBeOfType<HostedChannelRuntimeControlOutcome.Accepted>();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Stopping
        );
        await lifecycle.MarkStoppedAsync("streamer", CancellationToken.None);
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Stopped
        );
    }

    private static ChannelBotAuthorizationGrant Grant(
        string userId,
        string login,
        params string[] scopes
    ) => new(userId, LoginName.Parse(login), OAuthScopeSet.Create(scopes));

    private static ChannelBotAuthorizationService ChannelAuthorizationService(
        SqliteBlokeBotDbFactory dbFactory,
        params string[] scopes
    ) => new(dbFactory, ChangeNotifier(), ChannelOAuthService(scopes));

    private static ChannelBotOAuthService ChannelOAuthService(params string[] scopes)
    {
        var httpClientFactory = new EmptyHttpClientFactory();
        return new(
            ConfigurationWithScopes(scopes),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );
    }

    private static HostBotAccountAuthorizationService HostBotAccounts(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var httpClientFactory = new EmptyHttpClientFactory();
        var options = BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                },
            }
        );
        var oauth = new OAuthTransport(
            httpClientFactory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var helix = new HelixClient(
            httpClientFactory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var changes = ChangeNotifier();
        return new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helix),
            oauth,
            helix,
            HostBotAccountTokenProtectionTestSupport.CreateProtector(),
            new UnavailableTokenStatusSource(),
            changes,
            options,
            new HostedChannelRuntimeTransitionService(dbFactory, changes)
        );
    }

    private static HostedChannelChangeNotifier ChangeNotifier() =>
        new(TestEventBus.Create<AppEventKind>());

    private static IConfiguration ConfigurationWithScopes(params string[] scopes)
    {
        var values = new Dictionary<string, string?>
        {
            ["TwitchBot:Identity:ClientId"] = "client",
            ["TwitchBot:Identity:ClientSecret"] = "secret",
        };

        for (var i = 0; i < scopes.Length; i++)
        {
            values[$"TwitchBot:ChannelAuthorization:Scopes:{i}"] = scopes[i];
        }

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
            EnabledFeatures = HostFeatureFlags.All,
            TwitchUserId = twitchUserId,
            Login = login,
            DisplayName = login,
            ChannelBotAuthorizedAtUtc = authorizedScopes is null ? null : DateTime.UtcNow,
            ChannelBotAuthorizedScopes = authorizedScopes,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<BotHost> LoadHostAsync(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Hosts.SingleAsync(x => x.Id == hostId);
    }

    private sealed class RecordingOAuthHttpClientFactory(
        HttpStatusCode validationStatus = HttpStatusCode.OK
    ) : IHttpClientFactory
    {
        public string? ValidatedToken { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this, validationStatus));

        private sealed class Handler(
            RecordingOAuthHttpClientFactory owner,
            HttpStatusCode validationStatus
        ) : HttpMessageHandler
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
                    owner.ValidatedToken = request.Headers.Authorization?.Parameter;
                    return validationStatus != HttpStatusCode.OK
                        ? Task.FromResult(new HttpResponseMessage(validationStatus))
                        : Task.FromResult(
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

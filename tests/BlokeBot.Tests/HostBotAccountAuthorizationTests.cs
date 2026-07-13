using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostBotAccountAuthorizationTests
{
    [Test]
    public void ConfiguredBotRedirectUri_CreatingAuthorizationUri_UsesConfiguredValue()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            TwitchBotSettings.FromOptions(
                new TwitchBotOptions
                {
                    Identity = new TwitchBotIdentityOptions
                    {
                        ClientId = "client",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                    },
                }
            ),
            new TwitchOAuthApiClient(httpClientFactory),
            new TwitchHelixApiClient(httpClientFactory)
        );

        var uri = oauth.CreateAuthorizationUri("state");

        uri.Query.ShouldContain("redirect_uri=https%3A%2F%2Flocalhost%3A7107%2Foauth%2Fcallback");
    }

    [Test]
    public async Task OverrideDisabled_ResolvingBotAccount_ReturnsGlobalAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var account = await service.GetBotAccountAsync("streamer", CancellationToken.None);

        account.Login.ShouldBe("bot");
        account.AccessToken.ShouldBe("global-token");
    }

    [Test]
    public async Task OverrideDisabled_LoadingStatus_DoesNotReportCustomPermissionGaps()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        status.State.ShouldBe(BotAccountAuthorizationState.Disabled);
        status.RequiredScopes.ShouldNotBeEmpty();
        status.GrantedScopes.ShouldBeEmpty();
        status.MissingScopes.ShouldBeEmpty();
    }

    [Test]
    public async Task UnauthorizedOverrideEnabled_ResolvingBotAccount_DoesNotFallbackToGlobal()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.SetOverrideEnabledAsync(hostId, true, CancellationToken.None);

        await Should.ThrowAsync<TwitchAccessTokenUnavailableException>(async () =>
            await service.GetBotAccountAsync("streamer", CancellationToken.None)
        );
    }

    [Test]
    public async Task AuthorizedOverrideEnabled_ResolvingBotAccount_ReturnsCustomAccountAndReadyStatus()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.SetOverrideEnabledAsync(hostId, true, CancellationToken.None);

        var result = await service.AuthorizeAsync(
            hostId,
            new HostBotAccountAuthorizationGrant(
                new TwitchTokenSet(
                    "override-token",
                    "override-refresh",
                    DateTimeOffset.UtcNow.AddHours(1)
                ),
                "custom-id",
                LoginName.Parse("custombot"),
                "CustomBot",
                "https://static-cdn.jtvnw.net/custombot.png",
                ["chat:read", "chat:edit", TwitchScopes.UserReadModeratedChannels]
            ),
            CancellationToken.None
        );

        var account = await service.GetBotAccountAsync("streamer", CancellationToken.None);
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        account.Login.ShouldBe("custombot");
        account.AccessToken.ShouldBe("override-token");
        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.AuthorizedLogin.ShouldBe("custombot");
        status.AuthorizedProfileImageUrl.ShouldBe("https://static-cdn.jtvnw.net/custombot.png");
    }

    [Test]
    public async Task DifferentChannels_ResolvingConcurrently_DoesNotRetainAccountAcrossChannels()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var customHostId = await SeedHostAsync(dbFactory, "custom-channel");
        await SeedHostAsync(dbFactory, "global-channel");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.SetOverrideEnabledAsync(customHostId, true, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, customHostId);

        var lookups = Enumerable
            .Range(0, 8)
            .SelectMany(_ =>
                new[] { ResolveAsync("custom-channel"), ResolveAsync("global-channel") }
            )
            .ToArray();

        var results = await Task.WhenAll(lookups);

        foreach (var result in results)
        {
            var expected =
                result.Channel == "custom-channel"
                    ? new TwitchBotAccount("custombot", "override-token")
                    : new TwitchBotAccount("bot", "global-token");
            result.Account.ShouldBe(expected);
        }

        async Task<(string Channel, TwitchBotAccount Account)> ResolveAsync(string channel)
        {
            return (channel, await service.GetBotAccountAsync(channel, CancellationToken.None));
        }
    }

    [Test]
    public async Task OverrideDisabled_EnablingWhispers_RejectsWithoutPersisting()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var saved = await service.SetWhisperResponsesEnabledAsync(
            hostId,
            true,
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        saved.ShouldBeFalse();
        (
            await db.HostBotAccountSettings.SingleOrDefaultAsync(
                x => x.HostId == hostId,
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task WhispersEnabled_AuthorizingCustomBot_RequiresWhisperScope()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.SetOverrideEnabledAsync(hostId, true, CancellationToken.None);
        (
            await service.SetWhisperResponsesEnabledAsync(hostId, true, CancellationToken.None)
        ).ShouldBeTrue();

        var missing = await service.AuthorizeAsync(
            hostId,
            CreateCustomBotGrant(
                "override-token",
                ["chat:read", "chat:edit", TwitchScopes.UserReadModeratedChannels]
            ),
            CancellationToken.None
        );
        var authorized = await service.AuthorizeAsync(
            hostId,
            CreateCustomBotGrant(
                "override-whisper-token",
                [
                    "chat:read",
                    "chat:edit",
                    TwitchScopes.UserReadModeratedChannels,
                    TwitchScopes.UserManageWhispers,
                ]
            ),
            CancellationToken.None
        );
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        missing.Succeeded.ShouldBeFalse();
        missing.MissingScopes.ShouldContain(TwitchScopes.UserManageWhispers);
        authorized.Succeeded.ShouldBeTrue();
        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.RequiredScopes.ShouldContain(TwitchScopes.UserManageWhispers);
    }

    [Test]
    public async Task RunningHostWithOverride_DisablingOverride_QueuesRestartWithGlobalAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.SetOverrideEnabledAsync(hostId, true, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);

        await service.SetOverrideEnabledAsync(hostId, false, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.FindAsync([hostId], CancellationToken.None);
        var settings = await db.HostBotAccountSettings.SingleAsync(
            x => x.HostId == hostId,
            CancellationToken.None
        );
        host!.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Starting);
        settings.OverrideEnabled.ShouldBeFalse();
    }

    [Test]
    public async Task RunningHostWithAuthorizedOverride_EnablingOverride_QueuesRestartWithCustomAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.SetOverrideEnabledAsync(hostId, true, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await service.SetOverrideEnabledAsync(hostId, false, CancellationToken.None);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);

        await service.SetOverrideEnabledAsync(hostId, true, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.FindAsync([hostId], CancellationToken.None);
        var settings = await db.HostBotAccountSettings.SingleAsync(
            x => x.HostId == hostId,
            CancellationToken.None
        );
        host!.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Starting);
        settings.OverrideEnabled.ShouldBeTrue();
    }

    private static HostBotAccountAuthorizationService CreateService(
        SqliteBlokeBotDbFactory dbFactory,
        ITwitchAccessTokenProvider? tokenProvider
    )
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var options = TwitchBotSettings.FromOptions(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost:7107/oauth/callback",
                    Scopes = ["chat:read", "chat:edit", TwitchScopes.UserReadModeratedChannels],
                },
            }
        );
        var oauth = new TwitchOAuthApiClient(httpClientFactory);
        var helix = new TwitchHelixApiClient(httpClientFactory);
        return new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helix),
            oauth,
            helix,
            tokenProvider is null
                ? new UnavailableTwitchTokenStatusSource()
                : new TwitchTokenStatusService(
                    tokenProvider,
                    oauth,
                    NullLogger<TwitchTokenStatusService>.Instance
                ),
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            options
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            CreatedAtUtc = DateTime.UtcNow,
            DisplayName = login,
            Login = login,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task AuthorizeCustomBotAsync(
        HostBotAccountAuthorizationService service,
        int hostId
    )
    {
        var result = await service.AuthorizeAsync(
            hostId,
            CreateCustomBotGrant(
                "override-token",
                ["chat:read", "chat:edit", TwitchScopes.UserReadModeratedChannels]
            ),
            CancellationToken.None
        );

        result.Succeeded.ShouldBeTrue();
    }

    private static HostBotAccountAuthorizationGrant CreateCustomBotGrant(
        string accessToken,
        IReadOnlyList<string> scopes
    )
    {
        return new(
            new TwitchTokenSet(accessToken, "override-refresh", DateTimeOffset.UtcNow.AddHours(1)),
            "custom-id",
            LoginName.Parse("custombot"),
            "CustomBot",
            "https://static-cdn.jtvnw.net/custombot.png",
            scopes
        );
    }

    private static async Task SetRuntimeStateAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        BotChannelRuntimeState state
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.FindAsync([hostId], CancellationToken.None);
        host!.BotRuntimeState = state;
        host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private sealed class StaticTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(accessToken);
        }
    }

    private sealed class HostBotAccountHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler _handler = new();

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/validate" => ValidationResponse(request),
                        "/helix/users" => JsonResponse(
                            """
                            {"data":[{"id":"custom-id","login":"custombot","display_name":"CustomBot","profile_image_url":"https://static-cdn.jtvnw.net/custombot.png"}]}
                            """
                        ),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );
            }

            private static HttpResponseMessage ValidationResponse(HttpRequestMessage request)
            {
                return request.Headers.Authorization?.Parameter switch
                {
                    "global-token" => JsonResponse(
                        """
                        {"user_id":"bot-id","login":"bot","scopes":["chat:read","chat:edit","user:read:moderated_channels"]}
                        """
                    ),
                    "override-token" => JsonResponse(
                        """
                        {"user_id":"custom-id","login":"custombot","scopes":["chat:read","chat:edit","user:read:moderated_channels"]}
                        """
                    ),
                    "override-whisper-token" => JsonResponse(
                        """
                        {"user_id":"custom-id","login":"custombot","scopes":["chat:read","chat:edit","user:read:moderated_channels","user:manage:whispers"]}
                        """
                    ),
                    _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                };
            }

            private static HttpResponseMessage JsonResponse(string json)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}

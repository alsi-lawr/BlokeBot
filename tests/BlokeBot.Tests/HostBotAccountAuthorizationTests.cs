using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
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
            BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        ClientId = "client",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                    },
                }
            ),
            new OAuthTransport(httpClientFactory),
            new HelixClient(httpClientFactory)
        );

        var uri = oauth
            .CreateAuthorizationUriForDefaultScopes("state")
            .ShouldBeOfType<OAuthAuthorizationStartOutcome.Ready>()
            .AuthorizationUri;

        uri.Query.ShouldContain("redirect_uri=https%3A%2F%2Flocalhost%3A7107%2Foauth%2Fcallback");
    }

    [Test]
    public void ExplicitScope_CreatingAuthorizationUri_UsesOnlyExplicitSelection()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        ClientId = "client",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                        Scopes = ["chat:read"],
                    },
                }
            ),
            new OAuthTransport(httpClientFactory),
            new HelixClient(httpClientFactory)
        );

        var uri = oauth
            .CreateAuthorizationUriForScopes("state", OAuthScopeSet.Create(["bits:read"]))
            .ShouldBeOfType<OAuthAuthorizationStartOutcome.Ready>()
            .AuthorizationUri;

        uri.Query.ShouldContain("scope=bits%3Aread");
        uri.Query.ShouldNotContain("chat%3Aread");
    }

    [Test]
    public void MissingConfiguration_CreatingAuthorizationUri_ReturnsTypedUnavailable()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(new BotOptions()),
            new OAuthTransport(httpClientFactory),
            new HelixClient(httpClientFactory)
        );

        var outcome = oauth.CreateAuthorizationUriForDefaultScopes("state");

        outcome.ShouldBeOfType<OAuthAuthorizationStartOutcome.ConfigurationUnavailable>();
    }

    [Test]
    public async Task MissingConfiguration_CompletingAuthorization_ReturnsTypedUnavailable()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(new BotOptions()),
            new OAuthTransport(httpClientFactory),
            new HelixClient(httpClientFactory)
        );

        var outcome = await oauth.CompleteAsync("code", CancellationToken.None);

        outcome.ShouldBeOfType<OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.ConfigurationUnavailable>();
    }

    [Test]
    public async Task ProviderRejectedToken_CompletingAuthorization_ReturnsTypedRejection()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        ClientId = "client",
                        ClientSecret = "secret",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                    },
                }
            ),
            new OAuthTransport(httpClientFactory),
            new HelixClient(httpClientFactory)
        );

        var outcome = await oauth.CompleteAsync("code", CancellationToken.None);

        outcome.ShouldBeOfType<OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.ProviderNotValidated>();
    }

    [Test]
    public async Task OverrideDisabled_ResolvingBotAccount_ReturnsGlobalAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var account = Success(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );

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
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var reason = Error(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );

        reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }

    [Test]
    public async Task AuthorizedOverrideEnabled_ResolvingBotAccount_ReturnsCustomAccountAndReadyStatus()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var result = await service.AuthorizeAsync(
            hostId,
            new HostBotAccountAuthorizationGrant(
                new TokenSet(
                    "override-token",
                    "override-refresh",
                    DateTimeOffset.UtcNow.AddHours(1)
                ),
                "custom-id",
                LoginName.Parse("custombot"),
                "CustomBot",
                "https://static-cdn.jtvnw.net/custombot.png",
                OAuthScopeSet.Create(["chat:read", "chat:edit", Scopes.UserReadModeratedChannels])
            ),
            CancellationToken.None
        );

        var account = Success(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        account.Login.ShouldBe("custombot");
        account.AccessToken.ShouldBe("override-token");
        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.AuthorizedLogin.ShouldBe("custombot");
        status.AuthorizedProfileImageUrl.ShouldBe("https://static-cdn.jtvnw.net/custombot.png");
    }

    [Test]
    public async Task RefreshCredentialsRejected_LoadingStatus_ReturnsNotAuthorized()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            new HostBotAccountHttpClientFactory(HttpStatusCode.BadRequest)
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeExpiredCustomBotAsync(service, hostId);

        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        status.State.ShouldBe(BotAccountAuthorizationState.NotAuthorized);
    }

    [Test]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    public async Task RefreshProviderFailure_LoadingStatus_PropagatesTransportFailure(
        HttpStatusCode statusCode
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            new HostBotAccountHttpClientFactory(statusCode)
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeExpiredCustomBotAsync(service, hostId);

        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            service.GetStatusAsync(hostId, CancellationToken.None)
        );

        exception.StatusCode.ShouldBe(statusCode);
    }

    [Test]
    public async Task DifferentChannels_ResolvingConcurrently_DoesNotRetainAccountAcrossChannels()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var customHostId = await SeedHostAsync(dbFactory, "custom-channel");
        await SeedHostAsync(dbFactory, "global-channel");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(customHostId, CancellationToken.None);
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
                    ? new BotAccount("custombot", "override-token")
                    : new BotAccount("bot", "global-token");
            result.Account.ShouldBe(expected);
        }

        async Task<(string Channel, BotAccount Account)> ResolveAsync(string channel)
        {
            return (
                channel,
                Success(await service.GetBotAccount(channel).ExecuteAsync(CancellationToken.None))
            );
        }
    }

    [Test]
    public async Task OverrideDisabled_EnablingWhispers_RejectsWithoutPersisting()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var outcome = await service.EnableWhisperResponsesAsync(hostId, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        outcome.ShouldBeOfType<WhisperResponseConfigurationOutcome.CustomBotRequired>();
        (
            await db.HostBotAccountSettings.SingleOrDefaultAsync(
                x => x.HostId == hostId,
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task MissingHost_ConfiguringWhispers_ReturnsTypedMissingOutcome()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var enabling = await service.EnableWhisperResponsesAsync(42, CancellationToken.None);
        var disabling = await service.DisableWhisperResponsesAsync(42, CancellationToken.None);

        enabling.ShouldBeOfType<WhisperResponseConfigurationOutcome.HostNotFound>();
        disabling.ShouldBeOfType<WhisperResponseConfigurationOutcome.HostNotFound>();
    }

    [Test]
    public async Task WhispersEnabled_DisablingWhispers_PersistsPublicChatConfiguration()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        (
            await service.EnableWhisperResponsesAsync(hostId, CancellationToken.None)
        ).ShouldBeOfType<WhisperResponseConfigurationOutcome.Configured>();

        var outcome = await service.DisableWhisperResponsesAsync(hostId, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        outcome.ShouldBeOfType<WhisperResponseConfigurationOutcome.Configured>();
        (
            await db.HostBotAccountSettings.SingleAsync(
                x => x.HostId == hostId,
                CancellationToken.None
            )
        ).WhisperResponsesEnabled.ShouldBeFalse();
    }

    [Test]
    public async Task WhispersEnabled_AuthorizingCustomBot_RequiresWhisperScope()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        (
            await service.EnableWhisperResponsesAsync(hostId, CancellationToken.None)
        ).ShouldBeOfType<WhisperResponseConfigurationOutcome.Configured>();

        var missing = await service.AuthorizeAsync(
            hostId,
            CreateCustomBotGrant(
                "override-token",
                ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels]
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
                    Scopes.UserReadModeratedChannels,
                    Scopes.UserManageWhispers,
                ]
            ),
            CancellationToken.None
        );
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        missing.Succeeded.ShouldBeFalse();
        missing.MissingScopes.ShouldContain(Scopes.UserManageWhispers);
        authorized.Succeeded.ShouldBeTrue();
        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.RequiredScopes.ShouldContain(Scopes.UserManageWhispers);
    }

    [Test]
    public async Task RunningHostWithOverride_DisablingOverride_QueuesRestartWithGlobalAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);

        await service.UseMainBotAsync(hostId, CancellationToken.None);

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
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await service.UseMainBotAsync(hostId, CancellationToken.None);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);

        await service.UseCustomBotAsync(hostId, CancellationToken.None);

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
        IAccessTokenProvider? tokenProvider,
        HostBotAccountHttpClientFactory? httpClientFactory = null
    )
    {
        httpClientFactory ??= new HostBotAccountHttpClientFactory();
        var options = BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost:7107/oauth/callback",
                    Scopes = ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels],
                },
            }
        );
        var oauth = new OAuthTransport(httpClientFactory);
        var helix = new HelixClient(httpClientFactory);
        return new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helix),
            oauth,
            helix,
            tokenProvider is null
                ? new UnavailableTokenStatusSource()
                : new TokenStatusService(
                    tokenProvider,
                    oauth,
                    NullLogger<TokenStatusService>.Instance
                ),
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            options
        );
    }

    private static BotAccount Success(Result<BotAccount, AccessTokenUnavailableReason> result)
    {
        return result.Match(
            account => account,
            reason =>
                throw new InvalidOperationException(
                    $"Expected an authorized bot account, received {reason}."
                )
        );
    }

    private static AccessTokenUnavailableReason Error(
        Result<BotAccount, AccessTokenUnavailableReason> result
    )
    {
        return result.Match(
            _ => throw new InvalidOperationException("Expected token unavailability."),
            reason => reason
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
                ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels]
            ),
            CancellationToken.None
        );

        result.Succeeded.ShouldBeTrue();
    }

    private static async Task AuthorizeExpiredCustomBotAsync(
        HostBotAccountAuthorizationService service,
        int hostId
    )
    {
        var result = await service.AuthorizeAsync(
            hostId,
            CreateCustomBotGrant(
                "expired-token",
                ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels],
                DateTimeOffset.UtcNow.AddMinutes(-1)
            ),
            CancellationToken.None
        );

        result.Succeeded.ShouldBeTrue();
    }

    private static HostBotAccountAuthorizationGrant CreateCustomBotGrant(
        string accessToken,
        IReadOnlyList<string> scopes,
        DateTimeOffset? expiresAtUtc = null
    )
    {
        return new(
            new TokenSet(
                accessToken,
                "override-refresh",
                expiresAtUtc ?? DateTimeOffset.UtcNow.AddHours(1)
            ),
            "custom-id",
            LoginName.Parse("custombot"),
            "CustomBot",
            "https://static-cdn.jtvnw.net/custombot.png",
            OAuthScopeSet.Create(scopes)
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

    private sealed class StaticTokenProvider(string accessToken) : IAccessTokenProvider
    {
        public IO<string, AccessTokenUnavailableReason> GetAccessToken()
        {
            return IO<string, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<string, AccessTokenUnavailableReason>.Success(accessToken)
                )
            );
        }
    }

    private sealed class HostBotAccountHttpClientFactory(HttpStatusCode? tokenStatusCode = null)
        : IHttpClientFactory
    {
        private readonly Handler _handler = new(tokenStatusCode);

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler(HttpStatusCode? tokenStatusCode) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/token" => tokenStatusCode is { } statusCode
                            ? new HttpResponseMessage(statusCode)
                            : JsonResponse(
                                """
                                {"access_token":"grant-token","refresh_token":"refresh","expires_in":3600}
                                """
                            ),
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

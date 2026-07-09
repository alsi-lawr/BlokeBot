using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostBotAccountAuthorizationTests
{
    [Test]
    public async Task Disabled_override_resolves_global_bot_account()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var account = await service.GetBotAccountAsync("streamer", CancellationToken.None);

        account.Login.ShouldBe("bot");
        account.AccessToken.ShouldBe("global-token");
    }

    [Test]
    public async Task Enabled_override_without_authorization_does_not_fallback_to_global_bot()
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
    public async Task Enabled_override_authorizes_and_resolves_custom_bot_account()
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

    private static HostBotAccountAuthorizationService CreateService(
        SqliteBlokeBotDbFactory dbFactory,
        ITwitchAccessTokenProvider? tokenProvider
    )
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var services = new ServiceCollection();
        if (tokenProvider is not null)
            services.AddSingleton(tokenProvider);

        var serviceProvider = services.BuildServiceProvider();
        var options = Options.Create(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
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
            new TwitchTokenStatusService(serviceProvider, oauth),
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>()),
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

    private sealed class StaticTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(accessToken);
    }

    private sealed class HostBotAccountHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler handler = new();

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

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
                    _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                };
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}

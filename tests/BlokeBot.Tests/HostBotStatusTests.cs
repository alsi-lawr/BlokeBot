using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostBotStatusTests
{
    [Test]
    public async Task MissingTokenProvider_CheckingReadiness_ReportsTokenUnavailable()
    {
        await using var fixture = await CreateFixtureAsync(
            new HostBotStatusHttpClientFactory(),
            includeTokenProvider: false
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.TokenUnavailable);
    }

    [Test]
    public async Task RejectedToken_CheckingReadiness_ReportsInvalidToken()
    {
        await using var fixture = await CreateFixtureAsync(
            new HostBotStatusHttpClientFactory
            {
                ValidationStatusCode = HttpStatusCode.Unauthorized,
            }
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.InvalidToken);
    }

    [Test]
    public async Task MissingModeratorScope_CheckingReadiness_ReportsScopeFailure()
    {
        await using var fixture = await CreateFixtureAsync(
            new HostBotStatusHttpClientFactory
            {
                GrantedScopes = [TwitchScopes.ModeratorReadFollowers],
            }
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.MissingModeratorCheckScope);
    }

    [Test]
    public async Task BotNotModerator_CheckingReadiness_ReportsNotModerator()
    {
        await using var fixture = await CreateFixtureAsync(
            new HostBotStatusHttpClientFactory
            {
                GrantedScopes =
                [
                    TwitchScopes.UserReadModeratedChannels,
                    TwitchScopes.ModeratorReadFollowers,
                ],
                BotIsModerator = false,
            }
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.NotModerator);
    }

    [Test]
    public async Task MissingFollowerScope_CheckingReadiness_ReportsScopeFailure()
    {
        await using var fixture = await CreateFixtureAsync(
            new HostBotStatusHttpClientFactory
            {
                GrantedScopes = [TwitchScopes.UserReadModeratedChannels],
                BotIsModerator = true,
            }
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.MissingFollowerReadScope);
    }

    [Test]
    public async Task FullyAuthorizedBot_CheckingReadiness_ReportsReadyAndFollowerAccess()
    {
        await using var fixture = await CreateFixtureAsync(
            new HostBotStatusHttpClientFactory
            {
                GrantedScopes =
                [
                    TwitchScopes.UserReadModeratedChannels,
                    TwitchScopes.ModeratorReadFollowers,
                ],
                BotIsModerator = true,
            }
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);
        var status = await fixture.Service.GetStatusAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
        status.CanReadFollowers.ShouldBeTrue();
    }

    [Test]
    public async Task CustomBotOverride_CheckingReadiness_UsesCustomBotModeratorIdentity()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory
        {
            GrantedScopes =
            [
                TwitchScopes.UserReadModeratedChannels,
                TwitchScopes.ModeratorReadFollowers,
            ],
            BotIsModerator = true,
        };
        await using var fixture = await CreateFixtureAsync(httpClientFactory);
        var hostId = await SeedHostAsync(fixture.DbFactory, "streamer");
        await SeedHostBotOverrideAsync(fixture.DbFactory, hostId);

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
        httpClientFactory.LastModerationUserId.ShouldBe("custom-id");
    }

    [Test]
    public async Task CustomBotIsBroadcaster_CheckingReadiness_TreatsAccountAsChannelAuthority()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory
        {
            GrantedScopes =
            [
                TwitchScopes.UserReadModeratedChannels,
                TwitchScopes.ModeratorReadFollowers,
            ],
            BotIsModerator = false,
            CustomTokenLogin = "streamer",
            CustomTokenUserId = "channel-id",
        };
        await using var fixture = await CreateFixtureAsync(httpClientFactory);
        var hostId = await SeedHostAsync(fixture.DbFactory, "streamer");
        await SeedHostBotOverrideAsync(
            fixture.DbFactory,
            hostId,
            login: "streamer",
            userId: "channel-id",
            displayName: "Streamer"
        );

        var outcome = await fixture.Service.GetReadinessAsync("streamer", CancellationToken.None);
        var status = await fixture.Service.GetStatusAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
        status.CanReadFollowers.ShouldBeTrue();
        httpClientFactory.LastModerationUserId.ShouldBeNull();
    }

    private static async Task<HostBotStatusFixture> CreateFixtureAsync(
        HostBotStatusHttpClientFactory httpClientFactory,
        bool includeTokenProvider = true
    )
    {
        var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var services = new ServiceCollection();
        if (includeTokenProvider)
            services.AddSingleton<ITwitchAccessTokenProvider>(
                new StaticTokenProvider("user-token")
            );

        var provider = services.BuildServiceProvider();
        var options = Options.Create(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    Scopes =
                    [
                        TwitchScopes.UserReadModeratedChannels,
                        TwitchScopes.ModeratorReadFollowers,
                    ],
                },
            }
        );
        var oauth = new TwitchOAuthApiClient(httpClientFactory);
        var helix = new TwitchHelixApiClient(httpClientFactory);
        var hostBotAccounts = new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helix),
            oauth,
            helix,
            new TwitchTokenStatusService(provider, oauth),
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>()),
            options
        );
        return new HostBotStatusFixture(
            new HostBotStatusService(provider, hostBotAccounts, helix, options),
            dbFactory
        );
    }

    private sealed class HostBotStatusFixture : IAsyncDisposable
    {
        public HostBotStatusFixture(HostBotStatusService service, SqliteBlokeBotDbFactory dbFactory)
        {
            Service = service;
            DbFactory = dbFactory;
        }

        public SqliteBlokeBotDbFactory DbFactory { get; }

        public HostBotStatusService Service { get; }

        public async ValueTask DisposeAsync() => await DbFactory.DisposeAsync();
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

    private static async Task SeedHostBotOverrideAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string login = "custombot",
        string userId = "custom-id",
        string displayName = "CustomBot"
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.HostBotAccountSettings.Add(
            new HostBotAccountSettings
            {
                AccessToken = "custom-token",
                AuthorizedAtUtc = DateTime.UtcNow,
                AuthorizedScopes = string.Join(
                    ' ',
                    TwitchScopes.UserReadModeratedChannels,
                    TwitchScopes.ModeratorReadFollowers
                ),
                DisplayName = displayName,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                HostId = hostId,
                Login = login,
                OverrideEnabled = true,
                RefreshToken = "custom-refresh",
                TwitchUserId = userId,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private sealed class StaticTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(accessToken);
    }

    private sealed class HostBotStatusHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler handler = new();

        public HttpStatusCode ValidationStatusCode
        {
            get => handler.ValidationStatusCode;
            init => handler.ValidationStatusCode = value;
        }

        public IReadOnlyList<string> GrantedScopes
        {
            get => handler.GrantedScopes;
            init => handler.GrantedScopes = value;
        }

        public bool BotIsModerator
        {
            get => handler.BotIsModerator;
            init => handler.BotIsModerator = value;
        }

        public string CustomTokenLogin
        {
            get => handler.CustomTokenLogin;
            init => handler.CustomTokenLogin = value;
        }

        public string CustomTokenUserId
        {
            get => handler.CustomTokenUserId;
            init => handler.CustomTokenUserId = value;
        }

        public string? LastModerationUserId => handler.LastModerationUserId;

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

        private sealed class Handler : HttpMessageHandler
        {
            public HttpStatusCode ValidationStatusCode { get; set; } = HttpStatusCode.OK;

            public IReadOnlyList<string> GrantedScopes { get; set; } =
            [TwitchScopes.UserReadModeratedChannels, TwitchScopes.ModeratorReadFollowers];

            public bool BotIsModerator { get; set; } = true;

            public string CustomTokenLogin { get; set; } = "custombot";

            public string CustomTokenUserId { get; set; } = "custom-id";

            public string? LastModerationUserId { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                var response = request.RequestUri?.AbsolutePath switch
                {
                    "/oauth2/validate" => ValidationResponse(request),
                    "/helix/users" => JsonResponse(
                        """
                        {"data":[{"id":"channel-id","login":"streamer","display_name":"Streamer"},{"id":"bot-id","login":"bot","display_name":"Bot"}]}
                        """
                    ),
                    "/helix/moderation/channels" => ModerationChannelsResponse(request),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                };
                return Task.FromResult(response);
            }

            private HttpResponseMessage ValidationResponse(HttpRequestMessage request)
            {
                if (ValidationStatusCode != HttpStatusCode.OK)
                    return new HttpResponseMessage(ValidationStatusCode);

                return request.Headers.Authorization?.Parameter == "custom-token"
                    ? JsonResponse(
                        $$"""
                        {"user_id":"{{CustomTokenUserId}}","login":"{{CustomTokenLogin}}","scopes":[{{FormatScopes()}}]}
                        """
                    )
                    : JsonResponse(
                        $$"""
                        {"user_id":"bot-id","login":"bot","scopes":[{{FormatScopes()}}]}
                        """
                    );
            }

            private HttpResponseMessage ModerationChannelsResponse(HttpRequestMessage request)
            {
                LastModerationUserId = QueryValue(request.RequestUri, "user_id");
                return JsonResponse(
                    BotIsModerator
                        ? """
                        {"data":[{"broadcaster_id":"channel-id","broadcaster_login":"streamer","broadcaster_name":"Streamer"}],"pagination":{}}
                        """
                        : """{"data":[],"pagination":{}}"""
                );
            }

            private string FormatScopes() =>
                string.Join(',', GrantedScopes.Select(scope => $"\"{scope}\""));

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };

            private static string? QueryValue(Uri? uri, string key)
            {
                if (string.IsNullOrWhiteSpace(uri?.Query))
                    return null;

                foreach (
                    var part in uri
                        .Query.TrimStart('?')
                        .Split('&', StringSplitOptions.RemoveEmptyEntries)
                )
                {
                    var pieces = part.Split('=', 2);
                    if (
                        pieces.Length == 2
                        && string.Equals(
                            Uri.UnescapeDataString(pieces[0]),
                            key,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return Uri.UnescapeDataString(pieces[1]);
                    }
                }

                return null;
            }
        }
    }
}

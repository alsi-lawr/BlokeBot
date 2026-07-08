using System.Net;
using System.Text;
using BlokeBot.Features.HostedChannels.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostBotStatusTests
{
    [Test]
    public async Task Readiness_reports_unavailable_token()
    {
        var service = CreateService(
            new HostBotStatusHttpClientFactory(),
            includeTokenProvider: false
        );

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.TokenUnavailable);
    }

    [Test]
    public async Task Readiness_reports_invalid_token()
    {
        var service = CreateService(
            new HostBotStatusHttpClientFactory
            {
                ValidationStatusCode = HttpStatusCode.Unauthorized,
            }
        );

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.InvalidToken);
    }

    [Test]
    public async Task Readiness_reports_missing_moderator_scope()
    {
        var service = CreateService(
            new HostBotStatusHttpClientFactory
            {
                GrantedScopes = [TwitchScopes.ModeratorReadFollowers],
            }
        );

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.MissingModeratorCheckScope);
    }

    [Test]
    public async Task Readiness_reports_not_moderator()
    {
        var service = CreateService(
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

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.NotModerator);
    }

    [Test]
    public async Task Readiness_reports_missing_follower_scope()
    {
        var service = CreateService(
            new HostBotStatusHttpClientFactory
            {
                GrantedScopes = [TwitchScopes.UserReadModeratedChannels],
                BotIsModerator = true,
            }
        );

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.MissingFollowerReadScope);
    }

    [Test]
    public async Task Readiness_reports_ready()
    {
        var service = CreateService(
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

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);
        var status = await service.GetStatusAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
        status.CanReadFollowers.ShouldBeTrue();
    }

    private static HostBotStatusService CreateService(
        HostBotStatusHttpClientFactory httpClientFactory,
        bool includeTokenProvider = true
    )
    {
        var services = new ServiceCollection();
        if (includeTokenProvider)
            services.AddSingleton<ITwitchAccessTokenProvider>(
                new StaticTokenProvider("user-token")
            );

        var provider = services.BuildServiceProvider();
        return new HostBotStatusService(
            provider,
            new TwitchTokenStatusService(provider, new TwitchOAuthApiClient(httpClientFactory)),
            new TwitchHelixApiClient(httpClientFactory),
            Options.Create(
                new TwitchBotOptions
                {
                    Identity = new TwitchBotIdentityOptions
                    {
                        BotUsername = "bot",
                        ClientId = "client",
                        Scopes =
                        [
                            TwitchScopes.UserReadModeratedChannels,
                            TwitchScopes.ModeratorReadFollowers,
                        ],
                    },
                }
            )
        );
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

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

        private sealed class Handler : HttpMessageHandler
        {
            public HttpStatusCode ValidationStatusCode { get; set; } = HttpStatusCode.OK;

            public IReadOnlyList<string> GrantedScopes { get; set; } =
            [TwitchScopes.UserReadModeratedChannels, TwitchScopes.ModeratorReadFollowers];

            public bool BotIsModerator { get; set; } = true;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/validate" => ValidationResponse(),
                        "/helix/users" => JsonResponse(
                            """
                            {"data":[{"id":"channel-id","login":"streamer","display_name":"Streamer"},{"id":"bot-id","login":"bot","display_name":"Bot"}]}
                            """
                        ),
                        "/helix/moderation/channels" => JsonResponse(
                            BotIsModerator
                                ? """
                                {"data":[{"broadcaster_id":"channel-id","broadcaster_login":"streamer","broadcaster_name":"Streamer"}],"pagination":{}}
                                """
                                : """{"data":[],"pagination":{}}"""
                        ),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );
            }

            private HttpResponseMessage ValidationResponse() =>
                ValidationStatusCode == HttpStatusCode.OK
                    ? JsonResponse(
                        $$"""
                        {"user_id":"bot-id","login":"bot","scopes":[{{FormatScopes()}}]}
                        """
                    )
                    : new HttpResponseMessage(ValidationStatusCode);

            private string FormatScopes() =>
                string.Join(',', GrantedScopes.Select(scope => $"\"{scope}\""));

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}

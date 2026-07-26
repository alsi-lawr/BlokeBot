using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class FollowerOnlyChatReadinessTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FollowerModeOff_CheckingReadiness_DoesNotRequireBotFollowAccess()
    {
        var http = new FollowerOnlyChatHttpClientFactory { FollowerMode = false };
        var tokens = new RecordingTokenStatusProvider(MissingFollowScope());
        var service = CreateService(tokens, http);

        var readiness = await GetReadinessAsync(service);

        readiness.ShouldBeOfType<FollowerOnlyChatReadiness.NotRequired>();
        tokens.RequestCount.ShouldBe(0);
        http.RequestPaths.ShouldBe(["/helix/users", "/helix/chat/settings"]);
        http.ChatSettingsAccessToken.ShouldBe("app-token");
    }

    [Test]
    public async Task BroadcasterBot_CheckingFollowerMode_TreatsBotAsExempt()
    {
        var http = new FollowerOnlyChatHttpClientFactory { FollowerMode = true };
        var service = CreateService(new RecordingTokenStatusProvider(Ready("channel-id")), http);

        var readiness = await GetReadinessAsync(service);

        readiness
            .ShouldBeOfType<FollowerOnlyChatReadiness.Exempt>()
            .Exemption.ShouldBe(FollowerOnlyChatExemption.Broadcaster);
        http.RequestPaths.ShouldNotContain("/helix/moderation/channels");
        http.RequestPaths.ShouldNotContain("/helix/channels/followed");
    }

    [Test]
    public async Task ModeratorBot_CheckingFollowerMode_TreatsBotAsExempt()
    {
        var http = new FollowerOnlyChatHttpClientFactory
        {
            FollowerMode = true,
            BotIsModerator = true,
        };
        var service = CreateService(
            new RecordingTokenStatusProvider(Ready("validated-bot-subject")),
            http
        );

        var readiness = await GetReadinessAsync(service);

        readiness
            .ShouldBeOfType<FollowerOnlyChatReadiness.Exempt>()
            .Exemption.ShouldBe(FollowerOnlyChatExemption.Moderator);
        http.ModeratorRequestAccessToken.ShouldBe("bot-token");
        http.RequestPaths.ShouldNotContain("/helix/channels/followed");
    }

    [Test]
    public async Task FollowedNonModerator_WithMinimumDuration_ReportsWaitingUntil()
    {
        var http = new FollowerOnlyChatHttpClientFactory
        {
            FollowerMode = true,
            FollowerModeDurationMinutes = 30,
            FollowedAtUtc = _now.AddMinutes(-10),
        };
        var service = CreateService(
            new RecordingTokenStatusProvider(Ready("validated-bot-subject")),
            http
        );

        var readiness = await GetReadinessAsync(service);

        readiness
            .ShouldBeOfType<FollowerOnlyChatReadiness.WaitingUntil>()
            .EligibleAtUtc.ShouldBe(_now.AddMinutes(20));
        http.FollowedRequestAccessToken.ShouldBe("bot-token");
        http.FollowedRequestUserId.ShouldBe("validated-bot-subject");
        http.FollowedRequestBroadcasterId.ShouldBe("channel-id");
        http.FollowedRequestModeratorId.ShouldBeNull();
    }

    [Test]
    public async Task NonFollowingBot_CheckingFollowerMode_ReportsFollowStateWithoutClaimingIneligibility()
    {
        var http = new FollowerOnlyChatHttpClientFactory
        {
            FollowerMode = true,
            DoesNotFollow = true,
        };
        var service = CreateService(
            new RecordingTokenStatusProvider(Ready("validated-bot-subject")),
            http
        );

        var readiness = await GetReadinessAsync(service);

        readiness.ShouldBeOfType<FollowerOnlyChatReadiness.NotFollowing>();
    }

    [Test]
    public async Task MissingFollowScope_CheckingFollowerMode_UsesChatSettingsAndRequiresReconnect()
    {
        var http = new FollowerOnlyChatHttpClientFactory { FollowerMode = true };
        var service = CreateService(new RecordingTokenStatusProvider(MissingFollowScope()), http);

        var readiness = await GetReadinessAsync(service);

        readiness
            .ShouldBeOfType<FollowerOnlyChatReadiness.UnableToVerify>()
            .Failure.ShouldBe(FollowerOnlyChatVerificationFailure.MissingFollowReadScope);
        http.ChatSettingsAccessToken.ShouldBe("app-token");
        http.ModeratorRequestAccessToken.ShouldBe("bot-token");
        http.RequestPaths.ShouldNotContain("/helix/channels/followed");
    }

    private static FollowerOnlyChatReadinessService CreateService(
        IHostBotAccountTokenStatusProvider botTokens,
        FollowerOnlyChatHttpClientFactory http
    )
    {
        return new(
            new StaticAppAccessTokenSource(),
            botTokens,
            new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            Settings(),
            new FixedTimeProvider(_now)
        );
    }

    private static ValueTask<FollowerOnlyChatReadiness> GetReadinessAsync(
        FollowerOnlyChatReadinessService service
    )
    {
        return service.GetReadiness("streamer").RunAsync(CancellationToken.None);
    }

    private static BotSettings Settings()
    {
        return BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost/oauth/callback",
                    Scopes = [Scopes.UserReadModeratedChannels, Scopes.UserReadFollows],
                    TokenCachePath = "tokens.json",
                },
            }
        );
    }

    private static ActiveBotAccountTokenStatus Ready(string userId)
    {
        var scopes = ImmutableArray.Create(
            Scopes.UserReadModeratedChannels,
            Scopes.UserReadFollows
        );
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = "bot",
            Status = new TokenStatus.Ready(
                "bot-token",
                new TokenValidation(userId, "bot", OAuthScopeSet.Create(scopes)),
                scopes,
                scopes
            ),
        };
    }

    private static ActiveBotAccountTokenStatus MissingFollowScope()
    {
        var requiredScopes = ImmutableArray.Create(
            Scopes.UserReadModeratedChannels,
            Scopes.UserReadFollows
        );
        var grantedScopes = ImmutableArray.Create(Scopes.UserReadModeratedChannels);
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = "bot",
            Status = new TokenStatus.MissingScopes(
                "bot-token",
                new TokenValidation(
                    "validated-bot-subject",
                    "bot",
                    OAuthScopeSet.Create(grantedScopes)
                ),
                requiredScopes,
                grantedScopes,
                ImmutableArray.Create(Scopes.UserReadFollows)
            ),
        };
    }

    private sealed class StaticAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("app-token");
        }
    }

    private sealed class RecordingTokenStatusProvider(ActiveBotAccountTokenStatus status)
        : IHostBotAccountTokenStatusProvider
    {
        public int RequestCount { get; private set; }

        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(status);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class FollowerOnlyChatHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler _handler;

        public FollowerOnlyChatHttpClientFactory()
        {
            _handler = new Handler(this);
        }

        public bool FollowerMode { get; init; }

        public int? FollowerModeDurationMinutes { get; init; }

        public bool BotIsModerator { get; init; }

        public bool DoesNotFollow { get; init; }

        public DateTimeOffset FollowedAtUtc { get; init; } = _now.AddHours(-1);

        public List<string> RequestPaths { get; } = [];

        public string? ChatSettingsAccessToken { get; private set; }

        public string? ModeratorRequestAccessToken { get; private set; }

        public string? FollowedRequestAccessToken { get; private set; }

        public string? FollowedRequestUserId { get; private set; }

        public string? FollowedRequestBroadcasterId { get; private set; }

        public string? FollowedRequestModeratorId { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler(FollowerOnlyChatHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                var path = request.RequestUri!.AbsolutePath;
                owner.RequestPaths.Add(path);
                return Task.FromResult(
                    path switch
                    {
                        "/helix/users" => JsonResponse(
                            """{"data":[{"id":"channel-id","login":"streamer","display_name":"Streamer"}]}"""
                        ),
                        "/helix/chat/settings" => ChatSettingsResponse(request),
                        "/helix/moderation/channels" => ModerationResponse(request),
                        "/helix/channels/followed" => FollowedResponse(request),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );
            }

            private HttpResponseMessage ChatSettingsResponse(HttpRequestMessage request)
            {
                owner.ChatSettingsAccessToken = request.Headers.Authorization?.Parameter;
                QueryValue(request.RequestUri, "broadcaster_id").ShouldBe("channel-id");
                return JsonResponse(
                    $$"""{"data":[{"follower_mode":{{owner.FollowerMode.ToString().ToLowerInvariant()}},"follower_mode_duration":{{owner.FollowerModeDurationMinutes?.ToString() ?? "null"}}}]}"""
                );
            }

            private HttpResponseMessage ModerationResponse(HttpRequestMessage request)
            {
                owner.ModeratorRequestAccessToken = request.Headers.Authorization?.Parameter;
                QueryValue(request.RequestUri, "user_id").ShouldBe("validated-bot-subject");
                return JsonResponse(
                    owner.BotIsModerator
                        ? """{"data":[{"broadcaster_id":"channel-id","broadcaster_login":"streamer","broadcaster_name":"Streamer"}],"pagination":{}}"""
                        : """{"data":[],"pagination":{}}"""
                );
            }

            private HttpResponseMessage FollowedResponse(HttpRequestMessage request)
            {
                owner.FollowedRequestAccessToken = request.Headers.Authorization?.Parameter;
                owner.FollowedRequestUserId = QueryValue(request.RequestUri, "user_id");
                owner.FollowedRequestBroadcasterId = QueryValue(
                    request.RequestUri,
                    "broadcaster_id"
                );
                owner.FollowedRequestModeratorId = QueryValue(request.RequestUri, "moderator_id");
                return JsonResponse(
                    owner.DoesNotFollow
                        ? """{"data":[]}"""
                        : $$"""{"data":[{"user_id":"validated-bot-subject","user_login":"bot","user_name":"Bot","followed_at":"{{owner.FollowedAtUtc:O}}"}]}"""
                );
            }

            private static HttpResponseMessage JsonResponse(string json)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }

            private static string? QueryValue(Uri? uri, string key)
            {
                if (string.IsNullOrWhiteSpace(uri?.Query))
                {
                    return null;
                }

                return uri
                    .Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .Where(parts =>
                        string.Equals(
                            Uri.UnescapeDataString(parts[0]),
                            key,
                            StringComparison.Ordinal
                        )
                    )
                    .Select(parts => Uri.UnescapeDataString(parts[1]))
                    .SingleOrDefault();
            }
        }
    }
}

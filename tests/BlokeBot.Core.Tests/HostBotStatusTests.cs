using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Functional;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class HostBotStatusTests
{
    [Test]
    public async Task UnavailableToken_CheckingReadiness_ReportsTokenUnavailable()
    {
        var service = CreateService(UnavailableTokenStatus());

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.TokenUnavailable>();
    }

    [Test]
    public async Task RejectedToken_CheckingReadiness_ReportsInvalidToken()
    {
        var service = CreateService(InvalidTokenStatus());

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.InvalidToken>();
    }

    [Test]
    public async Task TokenInspectionUnavailable_CheckingReadiness_ReportsUnknown()
    {
        var service = CreateService(UnknownTokenStatus());

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.Unknown>();
    }

    [Test]
    public async Task MissingModeratorScope_CheckingReadiness_ReportsScopeFailure()
    {
        var service = CreateService(AuthorizedTokenStatus([Scopes.ModeratorReadFollowers]));

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.MissingModeratorCheckScope>();
    }

    [Test]
    public async Task BotNotModerator_CheckingReadiness_ReportsNotModerator()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory { BotIsModerator = false };
        var service = CreateService(AuthorizedTokenStatus(RequiredScopes()), httpClientFactory);

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.NotModerator>();
    }

    [Test]
    public async Task MissingFollowerScope_CheckingReadiness_ReportsScopeFailure()
    {
        var service = CreateService(AuthorizedTokenStatus([Scopes.UserReadModeratedChannels]));

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.MissingFollowerReadScope>();
    }

    [Test]
    public async Task FullyAuthorizedBot_CheckingReadiness_ReportsReadyAndFollowerAccess()
    {
        var service = CreateService(AuthorizedTokenStatus(RequiredScopes()));

        var outcome = await ReadinessAsync(service);
        var status = await service.GetStatus("streamer").RunAsync(CancellationToken.None);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.Ready>();
        status.CanReadFollowers.ShouldBeTrue();
    }

    [Test]
    public async Task CustomBotOverride_CheckingReadiness_UsesCustomBotModeratorIdentity()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory();
        var service = CreateService(
            AuthorizedTokenStatus(
                RequiredScopes(),
                botLogin: "custombot",
                validationLogin: "custombot",
                validationUserId: "custom-id",
                accessToken: "custom-token"
            ),
            httpClientFactory
        );

        var outcome = await ReadinessAsync(service);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.Ready>();
        httpClientFactory.LastModerationUserId.ShouldBe("custom-id");
    }

    [Test]
    public async Task CustomBotIsBroadcaster_CheckingReadiness_TreatsAccountAsChannelAuthority()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory { BotIsModerator = false };
        var service = CreateService(
            AuthorizedTokenStatus(
                RequiredScopes(),
                botLogin: "streamer",
                validationLogin: "streamer",
                validationUserId: "channel-id",
                accessToken: "custom-token"
            ),
            httpClientFactory
        );

        var outcome = await ReadinessAsync(service);
        var status = await service.GetStatus("streamer").RunAsync(CancellationToken.None);

        outcome.ShouldBeOfType<HostBotReadinessOutcome.Ready>();
        status.CanReadFollowers.ShouldBeTrue();
        httpClientFactory.LastModerationUserId.ShouldBeNull();
    }

    [Test]
    public async Task AppTokensUnavailable_CheckingStreamLiveness_RetainsConfigurationCause()
    {
        var service = CreateService(UnavailableTokenStatus());

        var outcome = await service.GetStreamLiveness("streamer").RunAsync(CancellationToken.None);

        var unavailable = outcome.ShouldBeOfType<HostStreamLivenessOutcome.Unavailable>();
        unavailable.Reason.ShouldBe(HostStreamLivenessUnavailableReason.AppAccessTokenUnavailable);
        var error = unavailable.Cause.ShouldBeOfType<HostBotAppAccessTokenUnavailableException>();
        error.Message.ShouldBe("The Twitch bot runner is not set up yet.");
    }

    [Test]
    public async Task ConfiguredAppTokens_CheckingStreamLiveness_ReportsLive()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory { StreamIsLive = true };
        var settings = Settings();
        var service = new HostBotStatusService(
            new OAuthHostBotAppAccessTokenSource(
                new AppAccessTokenProvider(httpClientFactory, settings.Identity)
            ),
            new StaticHostBotAccountTokenStatusProvider(UnavailableTokenStatus()),
            new HelixClient(httpClientFactory),
            settings
        );

        var outcome = await service.GetStreamLiveness("streamer").RunAsync(CancellationToken.None);

        outcome.ShouldBeOfType<HostStreamLivenessOutcome.Live>();
        httpClientFactory.TokenRequestCount.ShouldBe(1);
        httpClientFactory.StreamRequestCount.ShouldBe(1);
        httpClientFactory.StreamRequestClientId.ShouldBe("client");
        httpClientFactory.StreamRequestAccessToken.ShouldBe("app-token");
    }

    [Test]
    public async Task OfflineStream_CheckingStreamLiveness_ReportsOffline()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory();
        var service = CreateStreamService(httpClientFactory);

        var outcome = await service.GetStreamLiveness("streamer").RunAsync(CancellationToken.None);

        outcome.ShouldBeOfType<HostStreamLivenessOutcome.Offline>();
    }

    [Test]
    public async Task ProviderRequestFailure_CheckingStreamLiveness_RetainsUnavailableCause()
    {
        var expected = new HttpRequestException("provider secret");
        var httpClientFactory = new HostBotStatusHttpClientFactory { StreamFailure = expected };
        var service = CreateStreamService(httpClientFactory);

        var outcome = await service.GetStreamLiveness("streamer").RunAsync(CancellationToken.None);

        var unavailable = outcome.ShouldBeOfType<HostStreamLivenessOutcome.Unavailable>();
        unavailable.Reason.ShouldBe(HostStreamLivenessUnavailableReason.ProviderRequestFailed);
        unavailable.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        unavailable.Cause.ShouldBeSameAs(expected);

        unavailable.ToString().ShouldNotContain("provider secret");
        JsonSerializer.Serialize(unavailable).ShouldNotContain("provider secret");
    }

    [Test]
    public async Task CallerCancellation_CheckingStreamLiveness_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var expected = new OperationCanceledException(cancellation.Token);
        var service = CreateStreamService(
            new HostBotStatusHttpClientFactory(),
            new ThrowingHostBotAppAccessTokenSource(expected)
        );

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            service.GetStreamLiveness("streamer").RunAsync(cancellation.Token).AsTask()
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Test]
    public async Task UnexpectedFailure_CheckingGiveawayLiveness_LogsAndEscalates()
    {
        var expected = new NullReferenceException("unexpected secret");
        var logger = new RecordingLogger<PointsGiveawayEligibilityPolicy>();
        var policy = new PointsGiveawayEligibilityPolicy(
            CreateStreamService(
                new HostBotStatusHttpClientFactory(),
                new ThrowingHostBotAppAccessTokenSource(expected)
            ),
            logger
        );

        var thrown = await Should.ThrowAsync<PointsGiveawayStreamLivenessException>(() =>
            policy.GetStreamLiveness("streamer").RunAsync(CancellationToken.None).AsTask()
        );

        thrown.HostLogin.ShouldBe("streamer");
        thrown.InnerException.ShouldBeSameAs(expected);
        var diagnostic = logger.Entries.Single();
        diagnostic.Level.ShouldBe(LogLevel.Critical);
        diagnostic.Exception.ShouldBeNull();
        diagnostic.Message.ShouldContain(typeof(NullReferenceException).FullName!);
        diagnostic.Message.ShouldNotContain("unexpected secret");
    }

    private static HostBotStatusService CreateService(
        ActiveBotAccountTokenStatus tokenStatus,
        HostBotStatusHttpClientFactory? httpClientFactory = null
    )
    {
        var http = httpClientFactory ?? new HostBotStatusHttpClientFactory();
        return new(
            new UnavailableHostBotAppAccessTokenSource(),
            new StaticHostBotAccountTokenStatusProvider(tokenStatus),
            new HelixClient(http),
            Settings()
        );
    }

    private static ValueTask<HostBotReadinessOutcome> ReadinessAsync(HostBotStatusService service)
    {
        return service.GetReadiness("streamer").RunAsync(CancellationToken.None);
    }

    private static HostBotStatusService CreateStreamService(
        HostBotStatusHttpClientFactory httpClientFactory,
        IHostBotAppAccessTokenSource? appTokens = null
    )
    {
        return new(
            appTokens ?? new StaticHostBotAppAccessTokenSource(),
            new StaticHostBotAccountTokenStatusProvider(UnavailableTokenStatus()),
            new HelixClient(httpClientFactory),
            Settings()
        );
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
                    Scopes = RequiredScopes(),
                    TokenCachePath = "tokens.json",
                },
            }
        );
    }

    private static string[] RequiredScopes()
    {
        return [Scopes.UserReadModeratedChannels, Scopes.ModeratorReadFollowers];
    }

    private static ActiveBotAccountTokenStatus UnavailableTokenStatus()
    {
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = "bot",
            Status = new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                ImmutableArray.CreateRange(RequiredScopes())
            ),
        };
    }

    private static ActiveBotAccountTokenStatus InvalidTokenStatus()
    {
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = "bot",
            Status = new TokenStatus.Invalid(ImmutableArray.CreateRange(RequiredScopes())),
        };
    }

    private static ActiveBotAccountTokenStatus UnknownTokenStatus()
    {
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = "bot",
            Status = new TokenStatus.Unknown(
                new TokenStatusError.ValidationUnavailable(
                    TokenStatusTransportFailureReason.RequestFailed,
                    typeof(HttpRequestException).FullName!,
                    ImmutableArray.CreateRange(RequiredScopes())
                )
            ),
        };
    }

    private static ActiveBotAccountTokenStatus AuthorizedTokenStatus(
        IReadOnlyList<string> grantedScopes,
        string botLogin = "bot",
        string validationLogin = "bot",
        string validationUserId = "bot-id",
        string accessToken = "user-token"
    )
    {
        var requiredScopes = ImmutableArray.CreateRange(RequiredScopes());
        var granted = ImmutableArray.CreateRange(grantedScopes);
        var missing = ImmutableArray.CreateRange(
            requiredScopes.Except(granted, StringComparer.Ordinal)
        );
        var validation = new TokenValidation(
            validationUserId,
            validationLogin,
            OAuthScopeSet.Create(granted)
        );
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = botLogin,
            Status = missing.IsEmpty
                ? new TokenStatus.Ready(accessToken, validation, requiredScopes, granted)
                : new TokenStatus.MissingScopes(
                    accessToken,
                    validation,
                    requiredScopes,
                    granted,
                    missing
                ),
        };
    }

    private sealed class StaticHostBotAccountTokenStatusProvider(ActiveBotAccountTokenStatus status)
        : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(status);
        }
    }

    private sealed class StaticHostBotAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("app-token");
        }
    }

    private sealed class ThrowingHostBotAppAccessTokenSource(Exception failure)
        : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            throw failure;
        }
    }

    private sealed class HostBotStatusHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler _handler = new();

        public bool BotIsModerator
        {
            get => _handler.BotIsModerator;
            init => _handler.BotIsModerator = value;
        }

        public bool StreamIsLive
        {
            get => _handler.StreamIsLive;
            init => _handler.StreamIsLive = value;
        }

        public Exception? StreamFailure
        {
            get => _handler.StreamFailure;
            init => _handler.StreamFailure = value;
        }

        public string? LastModerationUserId => _handler.LastModerationUserId;

        public string? StreamRequestAccessToken => _handler.StreamRequestAccessToken;

        public string? StreamRequestClientId => _handler.StreamRequestClientId;

        public int StreamRequestCount => _handler.StreamRequestCount;

        public int TokenRequestCount => _handler.TokenRequestCount;

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler : HttpMessageHandler
        {
            public bool BotIsModerator { get; set; } = true;

            public bool StreamIsLive { get; set; }

            public Exception? StreamFailure { get; set; }

            public string? LastModerationUserId { get; private set; }

            public string? StreamRequestAccessToken { get; private set; }

            public string? StreamRequestClientId { get; private set; }

            public int StreamRequestCount { get; private set; }

            public int TokenRequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (
                    request.RequestUri?.AbsolutePath == "/helix/streams"
                    && StreamFailure is { } failure
                )
                {
                    return Task.FromException<HttpResponseMessage>(failure);
                }

                return Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/token" => TokenResponse(),
                        "/helix/users" => JsonResponse(
                            """
                            {"data":[{"id":"channel-id","login":"streamer","display_name":"Streamer"}]}
                            """
                        ),
                        "/helix/moderation/channels" => ModerationChannelsResponse(request),
                        "/helix/streams" => StreamResponse(request),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
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

            private HttpResponseMessage StreamResponse(HttpRequestMessage request)
            {
                StreamRequestCount++;
                StreamRequestAccessToken = request.Headers.Authorization?.Parameter;
                StreamRequestClientId = request.Headers.GetValues("Client-Id").Single();
                return JsonResponse(
                    StreamIsLive
                        ? """
                        {
                          "data": [
                            {
                              "id": "stream-id",
                              "user_id": "channel-id",
                              "user_login": "streamer",
                              "user_name": "Streamer",
                              "game_id": "game-id",
                              "game_name": "Example Game",
                              "type": "live",
                              "title": "Representative stream",
                              "tags": ["English"],
                              "viewer_count": 42,
                              "started_at": "2026-07-13T12:34:56Z",
                              "language": "en",
                              "thumbnail_url": "https://example.test/{width}x{height}.jpg",
                              "is_mature": false
                            }
                          ]
                        }
                        """
                        : """{"data":[]}"""
                );
            }

            private HttpResponseMessage TokenResponse()
            {
                TokenRequestCount++;
                return JsonResponse("""{"access_token":"app-token","expires_in":3600}""");
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullLoggerScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullLoggerScope : IDisposable
    {
        public static readonly NullLoggerScope Instance = new();

        public void Dispose() { }
    }
}

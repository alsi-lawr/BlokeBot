using System.Net;
using System.Text;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points.Giveaways;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostBotStatusTests
{
    [Test]
    public async Task UnavailableToken_CheckingReadiness_ReportsTokenUnavailable()
    {
        var service = CreateService(UnavailableTokenStatus());

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.TokenUnavailable);
    }

    [Test]
    public async Task RejectedToken_CheckingReadiness_ReportsInvalidToken()
    {
        var service = CreateService(InvalidTokenStatus());

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.InvalidToken);
    }

    [Test]
    public async Task MissingModeratorScope_CheckingReadiness_ReportsScopeFailure()
    {
        var service = CreateService(
            AuthorizedTokenStatus([TwitchScopes.ModeratorReadFollowers])
        );

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.MissingModeratorCheckScope);
    }

    [Test]
    public async Task BotNotModerator_CheckingReadiness_ReportsNotModerator()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory
        {
            BotIsModerator = false,
        };
        var service = CreateService(AuthorizedTokenStatus(RequiredScopes()), httpClientFactory);

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.NotModerator);
    }

    [Test]
    public async Task MissingFollowerScope_CheckingReadiness_ReportsScopeFailure()
    {
        var service = CreateService(
            AuthorizedTokenStatus([TwitchScopes.UserReadModeratedChannels])
        );

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.MissingFollowerReadScope);
    }

    [Test]
    public async Task FullyAuthorizedBot_CheckingReadiness_ReportsReadyAndFollowerAccess()
    {
        var service = CreateService(AuthorizedTokenStatus(RequiredScopes()));

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);
        var status = await service.GetStatusAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
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

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
        httpClientFactory.LastModerationUserId.ShouldBe("custom-id");
    }

    [Test]
    public async Task CustomBotIsBroadcaster_CheckingReadiness_TreatsAccountAsChannelAuthority()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory
        {
            BotIsModerator = false,
        };
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

        var outcome = await service.GetReadinessAsync("streamer", CancellationToken.None);
        var status = await service.GetStatusAsync("streamer", CancellationToken.None);

        outcome.Kind.ShouldBe(HostBotReadinessKind.Ready);
        status.CanReadFollowers.ShouldBeTrue();
        httpClientFactory.LastModerationUserId.ShouldBeNull();
    }

    [Test]
    public async Task AppTokensUnavailable_CheckingStreamLiveness_RetainsConfigurationCause()
    {
        var service = CreateService(UnavailableTokenStatus());

        var outcome = await service.GetStreamLivenessAsync(
            "streamer",
            CancellationToken.None
        );

        var unavailable = outcome.ShouldBeOfType<HostStreamLivenessOutcome.Unavailable>();
        unavailable.Reason.ShouldBe(
            HostStreamLivenessUnavailableReason.AppAccessTokenUnavailable
        );
        var error = unavailable.Cause.ShouldBeOfType<
            HostBotAppAccessTokenUnavailableException
        >();
        error.Message.ShouldBe("The Twitch bot runner is not set up yet.");
    }

    [Test]
    public async Task ConfiguredAppTokens_CheckingStreamLiveness_ReportsLive()
    {
        var httpClientFactory = new HostBotStatusHttpClientFactory { StreamIsLive = true };
        var settings = Settings();
        var service = new HostBotStatusService(
            new TwitchHostBotAppAccessTokenSource(
                new TwitchAppAccessTokenProvider(httpClientFactory, settings.Identity)
            ),
            new StaticHostBotAccountTokenStatusProvider(UnavailableTokenStatus()),
            new TwitchHelixApiClient(httpClientFactory),
            settings
        );

        var outcome = await service.GetStreamLivenessAsync(
            "streamer",
            CancellationToken.None
        );

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

        var outcome = await service.GetStreamLivenessAsync(
            "streamer",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<HostStreamLivenessOutcome.Offline>();
    }

    [Test]
    public async Task ProviderRequestFailure_CheckingStreamLiveness_RetainsUnavailableCause()
    {
        var expected = new HttpRequestException("provider secret");
        var httpClientFactory = new HostBotStatusHttpClientFactory
        {
            StreamFailure = expected,
        };
        var service = CreateStreamService(httpClientFactory);

        var outcome = await service.GetStreamLivenessAsync(
            "streamer",
            CancellationToken.None
        );

        var unavailable = outcome.ShouldBeOfType<HostStreamLivenessOutcome.Unavailable>();
        unavailable.Reason.ShouldBe(
            HostStreamLivenessUnavailableReason.ProviderRequestFailed
        );
        unavailable.Cause.ShouldBeSameAs(expected);
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
            service.GetStreamLivenessAsync("streamer", cancellation.Token)
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
            policy.GetStreamLivenessAsync("streamer", CancellationToken.None)
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
            new TwitchHelixApiClient(http),
            Settings()
        );
    }

    private static HostBotStatusService CreateStreamService(
        HostBotStatusHttpClientFactory httpClientFactory,
        IHostBotAppAccessTokenSource? appTokens = null
    ) =>
        new(
            appTokens ?? new StaticHostBotAppAccessTokenSource(),
            new StaticHostBotAccountTokenStatusProvider(UnavailableTokenStatus()),
            new TwitchHelixApiClient(httpClientFactory),
            Settings()
        );

    private static TwitchBotSettings Settings() =>
        TwitchBotSettings.FromOptions(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
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

    private static string[] RequiredScopes() =>
        [TwitchScopes.UserReadModeratedChannels, TwitchScopes.ModeratorReadFollowers];

    private static ActiveBotAccountTokenStatus UnavailableTokenStatus() =>
        new(
            "bot",
            null,
            TwitchTokenStatusState.Unavailable,
            null,
            null,
            RequiredScopes(),
            [],
            RequiredScopes()
        );

    private static ActiveBotAccountTokenStatus InvalidTokenStatus() =>
        new(
            "bot",
            null,
            TwitchTokenStatusState.Invalid,
            "user-token",
            null,
            RequiredScopes(),
            [],
            RequiredScopes()
        );

    private static ActiveBotAccountTokenStatus AuthorizedTokenStatus(
        IReadOnlyList<string> grantedScopes,
        string botLogin = "bot",
        string validationLogin = "bot",
        string validationUserId = "bot-id",
        string accessToken = "user-token"
    )
    {
        var requiredScopes = RequiredScopes();
        var granted = grantedScopes.ToArray();
        var missing = requiredScopes.Except(granted, StringComparer.Ordinal).ToArray();
        return new(
            botLogin,
            null,
            missing.Length == 0
                ? TwitchTokenStatusState.Ready
                : TwitchTokenStatusState.MissingScopes,
            accessToken,
            new TwitchTokenValidation(
                validationUserId,
                validationLogin,
                granted.ToHashSet(StringComparer.Ordinal)
            ),
            requiredScopes,
            granted,
            missing
        );
    }

    private sealed class StaticHostBotAccountTokenStatusProvider(
        ActiveBotAccountTokenStatus status
    ) : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        ) => Task.FromResult(status);
    }

    private sealed class StaticHostBotAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("app-token");
    }

    private sealed class ThrowingHostBotAppAccessTokenSource(Exception failure)
        : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            throw failure;
    }

    private sealed class HostBotStatusHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler handler = new();

        public bool BotIsModerator
        {
            get => handler.BotIsModerator;
            init => handler.BotIsModerator = value;
        }

        public bool StreamIsLive
        {
            get => handler.StreamIsLive;
            init => handler.StreamIsLive = value;
        }

        public Exception? StreamFailure
        {
            get => handler.StreamFailure;
            init => handler.StreamFailure = value;
        }

        public string? LastModerationUserId => handler.LastModerationUserId;

        public string? StreamRequestAccessToken => handler.StreamRequestAccessToken;

        public string? StreamRequestClientId => handler.StreamRequestClientId;

        public int StreamRequestCount => handler.StreamRequestCount;

        public int TokenRequestCount => handler.TokenRequestCount;

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

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
                return JsonResponse(StreamIsLive ? """{"data":[{}]}""" : """{"data":[]}""");
            }

            private HttpResponseMessage TokenResponse()
            {
                TokenRequestCount++;
                return JsonResponse("""{"access_token":"app-token","expires_in":3600}""");
            }

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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullLoggerScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullLoggerScope : IDisposable
    {
        public static readonly NullLoggerScope Instance = new();

        public void Dispose() { }
    }
}

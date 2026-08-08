using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentProviderOperationsTests
{
    [Test]
    public async Task ProviderRejectedClip_FallsBackToCreatedMarker()
    {
        await using var database = await CreateDatabaseAsync();
        var http = new ProviderHttpClientFactory(
            ClipFailure.ProviderRejected,
            markerAmbiguous: false
        );
        var operations = CreateOperations(database, http);
        var publicId = Guid.NewGuid();

        var outcome = await operations.CaptureAsync(
            1,
            publicId,
            true,
            "Community moment",
            CancellationToken.None
        );

        var marker = outcome.ShouldBeOfType<MomentProviderOutcome.MarkerReady>();
        http.ClipPosts.ShouldBe(1);
        http.MarkerPosts.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
        (await verify.TwitchStreamMarkers.CountAsync()).ShouldBe(1);
        (await verify.TwitchStreamMarkers.SingleAsync()).Id.ShouldBe(marker.MarkerId);
    }

    [Test]
    public async Task ProviderRejectedClip_AmbiguousMarkerRemainsAmbiguous()
    {
        await using var database = await CreateDatabaseAsync();
        var http = new ProviderHttpClientFactory(
            ClipFailure.ProviderRejected,
            markerAmbiguous: true
        );
        var operations = CreateOperations(database, http);

        var outcome = await operations.CaptureAsync(
            1,
            Guid.NewGuid(),
            true,
            "Community moment",
            CancellationToken.None
        );

        var ambiguous = outcome.ShouldBeOfType<MomentProviderOutcome.Ambiguous>();
        _ = ambiguous.ClipId.ShouldNotBeNull();
        _ = ambiguous.MarkerId.ShouldNotBeNull();
        ambiguous.Reason.ShouldContain("fallback marker");
        http.MarkerPosts.ShouldBe(1);
    }

    [Test]
    [Arguments(ClipFailure.Offline)]
    [Arguments(ClipFailure.VodsDisabled)]
    [Arguments(ClipFailure.Unauthorized)]
    public async Task KnownIneligibleClipFailure_DoesNotAttemptMarkerFallback(ClipFailure failure)
    {
        await using var database = await CreateDatabaseAsync();
        var http = new ProviderHttpClientFactory(failure, markerAmbiguous: false);
        var operations = CreateOperations(database, http);

        var outcome = await operations.CaptureAsync(
            1,
            Guid.NewGuid(),
            true,
            "Community moment",
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<MomentProviderOutcome.Failed>();
        http.ClipPosts.ShouldBe(1);
        http.MarkerPosts.ShouldBe(0);
    }

    [Test]
    public async Task DelayedSuccessfulClipClaim_ConvergesWithoutMarkerFallback()
    {
        await using var database = await CreateDatabaseAsync();
        var http = new DelayedSuccessfulClipHttpClientFactory();
        var first = CreateOperations(database, http);
        var second = CreateOperations(database, http);
        var publicId = Guid.NewGuid();

        var firstCapture = first.CaptureAsync(
            1,
            publicId,
            true,
            "Community moment",
            CancellationToken.None
        );
        await http.ClipPostStarted.WaitAsync(TimeSpan.FromSeconds(5));

        MomentProviderOutcome secondOutcome;
        try
        {
            secondOutcome = await second
                .CaptureAsync(1, publicId, true, "Community moment", CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            _ = secondOutcome.ShouldBeOfType<MomentProviderOutcome.Pending>();
            http.ClipPosts.ShouldBe(1);
            http.MarkerPosts.ShouldBe(0);
        }
        finally
        {
            http.ReleaseClipPost();
        }

        var firstOutcome = await firstCapture.WaitAsync(TimeSpan.FromSeconds(5));
        var ready = firstOutcome.ShouldBeOfType<MomentProviderOutcome.ClipReady>();

        http.ClipPosts.ShouldBe(1);
        http.MarkerPosts.ShouldBe(0);
        await using var verify = await database.CreateDbContextAsync();
        var clip = await verify.TwitchClips.SingleAsync();
        clip.Id.ShouldBe(ready.ClipId);
        clip.ProviderClipId.ShouldBe("delayed-clip-id");
        clip.Status.ShouldBe(TwitchClipStatus.Available);
        (await verify.TwitchStreamMarkers.CountAsync()).ShouldBe(0);
    }

    private static async Task<SqliteBlokeBotDbFactory> CreateDatabaseAsync()
    {
        var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await database.CreateDbContextAsync();
        _ = db.Hosts.Add(
            new BotHost
            {
                Login = "one",
                DisplayName = "One",
                TwitchUserId = "one-id",
                EnabledFeatures = HostFeatureFlags.All,
            }
        );
        _ = await db.SaveChangesAsync();
        return database;
    }

    private static MomentProviderOperations CreateOperations(
        SqliteBlokeBotDbFactory database,
        IHttpClientFactory http
    )
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var events = TestEventBus.Create<AppEventKind>();
        var clips = new ClipMarkerService(
            database,
            new BroadcasterOperationAuthorization(
                new ReadyBroadcasterProvider(),
                new DurableAlertService(database, clock, events)
            ),
            new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            events,
            clock,
            new NativeTwitchFeatureGate(database)
        );
        return new MomentProviderOperations(clips, database);
    }

    public enum ClipFailure
    {
        ProviderRejected,
        Offline,
        VodsDisabled,
        Unauthorized,
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        "one-id",
                        "one",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            );

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(static _ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
    }

    private sealed class ProviderHttpClientFactory(ClipFailure clipFailure, bool markerAmbiguous)
        : IHttpClientFactory
    {
        private bool _markerAmbiguous { get; } = markerAmbiguous;

        public int ClipPosts { get; private set; }

        public int MarkerPosts { get; private set; }

        public HttpClient CreateClient(string name) =>
            new(new Handler(this), disposeHandler: false);

        private sealed class Handler(ProviderHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (request.RequestUri!.AbsolutePath == "/helix/clips")
                {
                    owner.ClipPosts++;
                    return Task.FromResult(owner.ClipResponse());
                }
                if (request.RequestUri.AbsolutePath == "/helix/streams/markers")
                {
                    owner.MarkerPosts++;
                    return owner._markerAmbiguous
                        ? throw new HttpRequestException("ambiguous marker response")
                        : Task.FromResult(
                            Json(
                                """
                                {"data":[{"id":"marker-id","description":"Community moment","position_seconds":42,"created_at":"2026-07-30T12:00:00Z","URL":"https://twitch.test/marker"}]}
                                """
                            )
                        );
                }
                throw new InvalidOperationException(
                    $"Unexpected request {request.Method} {request.RequestUri}"
                );
            }
        }

        private HttpResponseMessage ClipResponse() =>
            clipFailure switch
            {
                ClipFailure.ProviderRejected => Error(HttpStatusCode.BadRequest, "not permitted"),
                ClipFailure.Offline => Error(HttpStatusCode.BadRequest, "channel is not live"),
                ClipFailure.VodsDisabled => Error(HttpStatusCode.BadRequest, "VODs disabled"),
                ClipFailure.Unauthorized => Error(HttpStatusCode.Unauthorized, "invalid token"),
                _ => throw new ArgumentOutOfRangeException(),
            };

        private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
            new(status)
            {
                Content = new StringContent(
                    $$"""{"message":"{{message}}"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class DelayedSuccessfulClipHttpClientFactory : IHttpClientFactory
    {
        private readonly TaskCompletionSource _clipPostStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _releaseClipPost = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private AtomicCounter _clipPostCounter { get; } = new();

        private AtomicCounter _markerPostCounter { get; } = new();

        public Task ClipPostStarted => _clipPostStarted.Task;

        public int ClipPosts => _clipPostCounter.Value;

        public int MarkerPosts => _markerPostCounter.Value;

        public HttpClient CreateClient(string name) =>
            new(new Handler(this), disposeHandler: false);

        public void ReleaseClipPost() => _releaseClipPost.TrySetResult();

        private sealed class Handler(DelayedSuccessfulClipHttpClientFactory owner)
            : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                switch (request.RequestUri!.AbsolutePath, request.Method.Method)
                {
                    case ("/helix/clips", "POST"):
                        owner._clipPostCounter.Increment();
                        _ = owner._clipPostStarted.TrySetResult();
                        await owner._releaseClipPost.Task.WaitAsync(cancellationToken);
                        return Json(
                            """{"data":[{"id":"delayed-clip-id","edit_url":"https://twitch.test/edit"}]}"""
                        );
                    case ("/helix/clips", "GET"):
                        request.RequestUri.Query.ShouldContain("id=delayed-clip-id");
                        return Json(
                            """
                            {"data":[{"id":"delayed-clip-id","url":"https://twitch.test/clip","broadcaster_id":"one-id","broadcaster_login":"one","creator_id":"creator-id","creator_name":"Creator","video_id":"video-id"}]}
                            """
                        );
                    case ("/helix/streams/markers", "POST"):
                        owner._markerPostCounter.Increment();
                        return Json(
                            """
                            {"data":[{"id":"unexpected-marker-id","description":"Community moment","position_seconds":42,"created_at":"2026-07-30T12:00:00Z","URL":"https://twitch.test/marker"}]}
                            """
                        );
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected request {request.Method} {request.RequestUri}"
                        );
                }
            }
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        private sealed class AtomicCounter
        {
            private int _value;

            public int Value => Volatile.Read(ref _value);

            public void Increment() => Interlocked.Increment(ref _value);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

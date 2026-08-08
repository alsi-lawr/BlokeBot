using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ClipMarkerServiceTests
{
    [Test]
    public async Task NativeTwitchDisabled_MutationsAndReconciliationStopUntilReenabled()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new ManualTimeProvider(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "one",
                DisplayName = "One",
                TwitchUserId = "one-id",
                EnabledFeatures = HostFeatureFlags.All & ~HostFeatureFlags.ClipsAndMarkers,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            _ = db.TwitchClips.Add(
                new TwitchClip
                {
                    HostId = host.Id,
                    IdempotencyKey = "retained",
                    ProviderClipId = "clip-id",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var http = new ClipMarkerHttpClientFactory();
        var service = CreateService(dbFactory, http, now);

        var state = await service.LoadAsync(1, CancellationToken.None);
        var clip = await service.CreateClipAsync(1, false, CancellationToken.None);
        var marker = await service.CreateMarkerAsync(1, "Disabled marker", CancellationToken.None);
        await service.ReconcileAsync(1, CancellationToken.None);

        _ = state.Authorization.ShouldBeOfType<ClipMarkerAuthorizationReadiness.Disabled>();
        state.PendingClips.ShouldBeEmpty();
        state.Results.ShouldBeEmpty();
        state.Markers.ShouldBeEmpty();
        _ = clip.ShouldBeOfType<ClipMarkerOperationOutcome.NotReady>();
        _ = marker.ShouldBeOfType<ClipMarkerOperationOutcome.NotReady>();
        http.OneClipPosts.ShouldBe(0);
        http.TwoClipPosts.ShouldBe(0);
        http.MarkerPosts.ShouldBe(0);
        http.ClipGets.ShouldBe(0);
        await using (var verifyDisabled = await dbFactory.CreateDbContextAsync())
        {
            (await verifyDisabled.TwitchClips.CountAsync()).ShouldBe(1);
            (await verifyDisabled.TwitchClips.SingleAsync()).Status.ShouldBe(
                TwitchClipStatus.Pending
            );
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.ClipsAndMarkers;
            _ = await verifyDisabled.SaveChangesAsync();
        }

        await service.ReconcileAsync(1, CancellationToken.None);

        http.ClipGets.ShouldBe(1);
        await using var verifyEnabled = await dbFactory.CreateDbContextAsync();
        (await verifyEnabled.TwitchClips.SingleAsync()).Status.ShouldBe(TwitchClipStatus.Available);
    }

    [Test]
    public async Task Reconciliation_UsesTheOwningSwitchAndClipDashboardHidesMomentAttempts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new ManualTimeProvider(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "one",
                DisplayName = "One",
                TwitchUserId = "one-id",
                EnabledFeatures = HostFeatureFlags.Moments,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            db.TwitchClips.AddRange(
                new TwitchClip
                {
                    HostId = host.Id,
                    IdempotencyKey = "clip-attempt",
                    ProviderClipId = "clip-id",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime,
                },
                new TwitchClip
                {
                    HostId = host.Id,
                    IdempotencyKey = "moment:public-id:clip",
                    ProviderClipId = "clip-id",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var http = new ClipMarkerHttpClientFactory();
        var service = CreateService(dbFactory, http, now);

        await service.ReconcileAsync(1, CancellationToken.None);

        http.ClipGets.ShouldBe(1);
        await using (var verifyMoments = await dbFactory.CreateDbContextAsync())
        {
            (
                await verifyMoments.TwitchClips.SingleAsync(static clip =>
                    clip.IdempotencyKey == "clip-attempt"
                )
            ).Status.ShouldBe(TwitchClipStatus.Pending);
            (
                await verifyMoments.TwitchClips.SingleAsync(static clip =>
                    clip.IdempotencyKey == "moment:public-id:clip"
                )
            ).Status.ShouldBe(TwitchClipStatus.Available);
            var host = await verifyMoments.Hosts.SingleAsync();
            host.EnabledFeatures = HostFeatureFlags.ClipsAndMarkers;
            _ = await verifyMoments.SaveChangesAsync();
        }

        await service.ReconcileAsync(1, CancellationToken.None);

        http.ClipGets.ShouldBe(2);
        await using (var enableBoth = await dbFactory.CreateDbContextAsync())
        {
            var host = await enableBoth.Hosts.SingleAsync();
            host.EnabledFeatures = HostFeatureFlags.ClipsAndMarkers | HostFeatureFlags.Moments;
            _ = await enableBoth.SaveChangesAsync();
        }
        var state = await service.LoadAsync(1, CancellationToken.None);
        state.Results.Count.ShouldBe(1);
    }

    [Test]
    public async Task ServiceOwnedAttempts_ReloadTypedRetryKeepsHostIsolationAndDoesNotRepeatMutations()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.AddRange(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                },
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "two",
                    DisplayName = "Two",
                    TwitchUserId = "two-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var now = new ManualTimeProvider(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));
        var http = new ClipMarkerHttpClientFactory();
        var service = CreateService(dbFactory, http, now);

        var ambiguous = await service.CreateClipAsync(1, false, CancellationToken.None);
        var ambiguousReference = ambiguous
            .ShouldBeOfType<ClipMarkerOperationOutcome.ClipAmbiguous>()
            .Attempt;
        string retainedClipKey;
        await using (var verifyAttempt = await dbFactory.CreateDbContextAsync())
        {
            retainedClipKey = (
                await verifyAttempt.TwitchClips.SingleAsync(clip =>
                    clip.HostId == 1 && clip.Id == ambiguousReference.Value
                )
            ).IdempotencyKey;
        }
        var reloadedService = CreateService(dbFactory, http, now);
        var retriedAmbiguous = await reloadedService.RetryClipAsync(
            1,
            ambiguousReference,
            CancellationToken.None
        );
        var wrongHost = await reloadedService.RetryClipAsync(
            2,
            ambiguousReference,
            CancellationToken.None
        );
        var available = await service.CreateClipAsync(2, false, CancellationToken.None);
        var availableReference = available
            .ShouldBeOfType<ClipMarkerOperationOutcome.ClipAvailable>()
            .Clip.Attempt;
        var retriedAvailable = await reloadedService.RetryClipAsync(
            2,
            availableReference,
            CancellationToken.None
        );
        now.Advance(TimeSpan.FromSeconds(61));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.TwitchClips.Add(
                new TwitchClip
                {
                    HostId = 2,
                    IdempotencyKey = "expires",
                    ProviderClipId = "unavailable-clip",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(61),
                }
            );
            _ = db.TwitchClips.Add(
                new TwitchClip
                {
                    HostId = 2,
                    IdempotencyKey = "unknown-provider-mutation",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(61),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        await service.ReconcileAsync(2, CancellationToken.None);
        var ambiguousMarker = await service.CreateMarkerAsync(
            2,
            "Ambiguous marker",
            CancellationToken.None
        );
        var markerReference = ambiguousMarker
            .ShouldBeOfType<ClipMarkerOperationOutcome.MarkerAmbiguous>()
            .Attempt;
        string retainedMarkerKey;
        await using (var verifyAttempt = await dbFactory.CreateDbContextAsync())
        {
            retainedMarkerKey = (
                await verifyAttempt.TwitchStreamMarkers.SingleAsync(marker =>
                    marker.HostId == 2 && marker.Id == markerReference.Value
                )
            ).IdempotencyKey;
        }
        var retriedMarker = await reloadedService.RetryMarkerAsync(
            2,
            markerReference,
            CancellationToken.None
        );
        var wrongMarkerHost = await reloadedService.RetryMarkerAsync(
            1,
            markerReference,
            CancellationToken.None
        );
        var marker = await service.CreateMarkerAsync(2, "Important moment", CancellationToken.None);
        await service.ReconcileAsync(2, CancellationToken.None);

        _ = retriedAmbiguous.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAmbiguous>();
        _ = wrongHost.ShouldBeOfType<ClipMarkerOperationOutcome.InvalidRequest>();
        _ = retriedAvailable.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAvailable>();
        _ = retriedMarker.ShouldBeOfType<ClipMarkerOperationOutcome.MarkerAmbiguous>();
        _ = wrongMarkerHost.ShouldBeOfType<ClipMarkerOperationOutcome.InvalidRequest>();
        _ = marker.ShouldBeOfType<ClipMarkerOperationOutcome.MarkerCreated>();
        http.OneClipPosts.ShouldBe(1);
        http.TwoClipPosts.ShouldBe(1);
        http.MarkerPosts.ShouldBe(2);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var clips = await verify
            .TwitchClips.OrderBy(clip => clip.HostId)
            .ThenBy(clip => clip.Id)
            .ToArrayAsync();
        clips.Length.ShouldBe(4);
        clips.Single(clip => clip.HostId == 1).Status.ShouldBe(TwitchClipStatus.Ambiguous);
        clips
            .Single(clip => clip.HostId == 1 && clip.Id == ambiguousReference.Value)
            .IdempotencyKey.ShouldBe(retainedClipKey);
        _ = clips.Single(clip => clip.HostId == 2 && clip.Status == TwitchClipStatus.Available);
        clips
            .Single(clip => clip.IdempotencyKey == "expires")
            .Status.ShouldBe(TwitchClipStatus.Expired);
        clips
            .Single(clip => clip.IdempotencyKey == "unknown-provider-mutation")
            .Status.ShouldBe(TwitchClipStatus.Ambiguous);
        clips
            .Where(clip => clip.IdempotencyKey is not ("expires" or "unknown-provider-mutation"))
            .ShouldAllBe(clip => !string.IsNullOrWhiteSpace(clip.IdempotencyKey));
        clips
            .Where(clip => clip.IdempotencyKey is not ("expires" or "unknown-provider-mutation"))
            .Select(clip => clip.IdempotencyKey)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(2);
        var markers = await verify.TwitchStreamMarkers.OrderBy(item => item.Id).ToArrayAsync();
        markers.Length.ShouldBe(2);
        markers[0].Status.ShouldBe(TwitchStreamMarkerStatus.Ambiguous);
        markers
            .Single(marker => marker.Id == markerReference.Value)
            .IdempotencyKey.ShouldBe(retainedMarkerKey);
        markers.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.IdempotencyKey));
        markers
            .Select(item => item.IdempotencyKey)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(2);
        var persistedMarker = markers[1];
        persistedMarker.Status.ShouldBe(TwitchStreamMarkerStatus.Succeeded);
        persistedMarker.VideoId.ShouldBe("video-id");
        persistedMarker.MarkerUrl.ShouldBe("https://twitch.test/marker");
        typeof(ClipView).GetProperty("IdempotencyKey").ShouldBeNull();
        typeof(StreamMarkerView).GetProperty("IdempotencyKey").ShouldBeNull();
    }

    [Test]
    public async Task DeterministicAttemptKeys_ReconcileWithoutRepeatingProviderMutations()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var now = new ManualTimeProvider(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));
        var http = new ClipMarkerHttpClientFactory();
        var service = CreateService(dbFactory, http, now);

        var firstClip = await service.CreateClipAsync(
            1,
            false,
            "moment:public-id:clip",
            CancellationToken.None
        );
        var retriedClip = await service.CreateClipAsync(
            1,
            false,
            "moment:public-id:clip",
            CancellationToken.None
        );
        var firstMarker = await service.CreateMarkerAsync(
            1,
            "Ambiguous marker",
            "moment:public-id:marker",
            CancellationToken.None
        );
        var retriedMarker = await service.CreateMarkerAsync(
            1,
            "Ambiguous marker",
            "moment:public-id:marker",
            CancellationToken.None
        );

        _ = firstClip.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAmbiguous>();
        _ = retriedClip.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAmbiguous>();
        _ = firstMarker.ShouldBeOfType<ClipMarkerOperationOutcome.MarkerAmbiguous>();
        _ = retriedMarker.ShouldBeOfType<ClipMarkerOperationOutcome.MarkerAmbiguous>();
        http.OneClipPosts.ShouldBe(1);
        http.MarkerPosts.ShouldBe(1);
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
        (await verify.TwitchStreamMarkers.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentDeterministicClaim_UsesOneProviderMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var now = new ManualTimeProvider(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));
        var http = new ClipMarkerHttpClientFactory();
        var first = CreateService(dbFactory, http, now);
        var second = CreateService(dbFactory, http, now);

        var outcomes = await Task.WhenAll(
            first.CreateClipAsync(1, false, "moment:concurrent:clip", CancellationToken.None),
            second.CreateClipAsync(1, false, "moment:concurrent:clip", CancellationToken.None)
        );

        outcomes
            .All(static value =>
                value
                    is ClipMarkerOperationOutcome.ClipPending
                        or ClipMarkerOperationOutcome.ClipAmbiguous
            )
            .ShouldBeTrue();
        http.OneClipPosts.ShouldBe(1);
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
    }

    private static ClipMarkerService CreateService(
        SqliteBlokeBotDbFactory dbFactory,
        ClipMarkerHttpClientFactory http,
        TimeProvider timeProvider
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new(
            dbFactory,
            new ReadyBroadcasterProvider(),
            new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            events,
            new DurableAlertService(dbFactory, timeProvider, events),
            timeProvider,
            new NativeTwitchFeatureGate(dbFactory)
        );
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
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
                        hostId == 1 ? "one-id" : "two-id",
                        hostId == 1 ? "one" : "two",
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

    private sealed class ClipMarkerHttpClientFactory : IHttpClientFactory
    {
        internal int OneClipPosts { get; private set; }

        internal int TwoClipPosts { get; private set; }

        internal int ClipGets { get; private set; }

        internal int MarkerPosts { get; private set; }

        public HttpClient CreateClient(string name) =>
            new(new Handler(this), disposeHandler: false);

        private sealed class Handler(ClipMarkerHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                switch (request.RequestUri!.AbsolutePath, request.Method.Method)
                {
                    case ("/helix/clips", "POST")
                        when request.RequestUri.Query.Contains(
                            "broadcaster_id=one-id",
                            StringComparison.Ordinal
                        ):
                        lock (owner)
                        {
                            owner.OneClipPosts++;
                        }
                        throw new HttpRequestException("ambiguous clip post");
                    case ("/helix/clips", "POST")
                        when request.RequestUri.Query.Contains(
                            "broadcaster_id=two-id",
                            StringComparison.Ordinal
                        ):
                        lock (owner)
                        {
                            owner.TwoClipPosts++;
                        }
                        return Json(
                            """{"data":[{"id":"clip-id","edit_url":"https://twitch.test/edit"}]}"""
                        );
                    case ("/helix/clips", "GET"):
                        owner.ClipGets++;
                        request.RequestUri.Query.ShouldContain("id=clip-id");
                        return Json(
                            """
                            {"data":[{"id":"clip-id","url":"https://twitch.test/clip","broadcaster_id":"two-id","broadcaster_login":"two","creator_id":"creator-id","creator_name":"Creator","video_id":"video-id"}]}
                            """
                        );
                    case ("/helix/streams/markers", "POST"):
                        owner.MarkerPosts++;
                        var content = await request.Content!.ReadAsStringAsync(cancellationToken);
                        if (content.Contains("Ambiguous marker", StringComparison.Ordinal))
                        {
                            throw new HttpRequestException("ambiguous marker post");
                        }
                        content.ShouldContain("Important moment");
                        return Json(
                            """
                            {"data":[{"id":"marker-id","description":"Important moment","position_seconds":12,"created_at":"2026-07-26T10:01:01Z","URL":"https://twitch.test/marker"}]}
                            """
                        );
                    case ("/helix/streams/markers", "GET"):
                        request.RequestUri.Query.ShouldContain("first=100");
                        return Json(
                            """
                            {"data":[{"videos":[{"video_id":"video-id","markers":[{"id":"marker-id","description":"Important moment","position_seconds":12,"created_at":"2026-07-26T10:01:01Z","URL":"https://twitch.test/marker"}]}]}]}
                            """
                        );
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected request {request.Method} {request.RequestUri}"
                        );
                }
            }

            private static HttpResponseMessage Json(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}

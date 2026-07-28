using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

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
                EnabledFeatures = HostFeatureFlags.All & ~HostFeatureFlags.NativeTwitch,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            db.TwitchClips.Add(
                new TwitchClip
                {
                    HostId = host.Id,
                    IdempotencyKey = "retained",
                    ProviderClipId = "clip-id",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime,
                }
            );
            await db.SaveChangesAsync();
        }
        var http = new ClipMarkerHttpClientFactory();
        var service = CreateService(dbFactory, http, now);

        var state = await service.LoadAsync(1, CancellationToken.None);
        var clip = await service.CreateClipAsync(1, "disabled-clip", false, CancellationToken.None);
        var marker = await service.CreateMarkerAsync(
            1,
            "disabled-marker",
            "Disabled marker",
            CancellationToken.None
        );
        await service.ReconcileAsync(1, CancellationToken.None);

        state.Authorization.ShouldBeOfType<ClipMarkerAuthorizationReadiness.Disabled>();
        state.PendingClips.ShouldBeEmpty();
        state.Results.ShouldBeEmpty();
        state.Markers.ShouldBeEmpty();
        clip.ShouldBeOfType<ClipMarkerOperationOutcome.NotReady>();
        marker.ShouldBeOfType<ClipMarkerOperationOutcome.NotReady>();
        http.ClipPosts.ShouldBe(0);
        http.MarkerPosts.ShouldBe(0);
        http.ClipGets.ShouldBe(0);
        await using (var verifyDisabled = await dbFactory.CreateDbContextAsync())
        {
            (await verifyDisabled.TwitchClips.CountAsync()).ShouldBe(1);
            (await verifyDisabled.TwitchClips.SingleAsync()).Status.ShouldBe(
                TwitchClipStatus.Pending
            );
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.NativeTwitch;
            await verifyDisabled.SaveChangesAsync();
        }

        await service.ReconcileAsync(1, CancellationToken.None);

        http.ClipGets.ShouldBe(1);
        await using var verifyEnabled = await dbFactory.CreateDbContextAsync();
        (await verifyEnabled.TwitchClips.SingleAsync()).Status.ShouldBe(TwitchClipStatus.Available);
    }

    [Test]
    public async Task ClipMarkerOperations_DedupePerHostRecoverBoundedClipsAndEnrichMarkers()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.AddRange(
                new BotHost
                {
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                },
                new BotHost
                {
                    Login = "two",
                    DisplayName = "Two",
                    TwitchUserId = "two-id",
                }
            );
            await db.SaveChangesAsync();
        }

        var now = new ManualTimeProvider(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));
        var http = new ClipMarkerHttpClientFactory();
        var service = CreateService(dbFactory, http, now);

        var ambiguous = await service.CreateClipAsync(1, "same-key", false, CancellationToken.None);
        var repeatedAmbiguous = await service.CreateClipAsync(
            1,
            "same-key",
            false,
            CancellationToken.None
        );
        var otherHost = await service.CreateClipAsync(2, "same-key", false, CancellationToken.None);
        var repeatedAvailable = await service.CreateClipAsync(
            2,
            "same-key",
            false,
            CancellationToken.None
        );
        var concurrent = await Task.WhenAll(
            service.CreateClipAsync(2, "concurrent-key", false, CancellationToken.None),
            service.CreateClipAsync(2, "concurrent-key", false, CancellationToken.None)
        );
        now.Advance(TimeSpan.FromSeconds(61));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.TwitchClips.Add(
                new TwitchClip
                {
                    HostId = 2,
                    IdempotencyKey = "expires",
                    ProviderClipId = "unavailable-clip",
                    Status = TwitchClipStatus.Pending,
                    RequestedAtUtc = now.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(61),
                }
            );
            await db.SaveChangesAsync();
        }
        await service.ReconcileAsync(2, CancellationToken.None);
        var marker = await service.CreateMarkerAsync(
            2,
            "marker-key",
            "Important moment",
            CancellationToken.None
        );
        await service.ReconcileAsync(2, CancellationToken.None);

        ambiguous.ShouldBeOfType<ClipMarkerOperationOutcome.Ambiguous>();
        repeatedAmbiguous.ShouldBeOfType<ClipMarkerOperationOutcome.Ambiguous>();
        otherHost.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAvailable>();
        repeatedAvailable.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAvailable>();
        foreach (var outcome in concurrent)
        {
            outcome.ShouldBeOfType<ClipMarkerOperationOutcome.ClipAvailable>();
        }
        marker.ShouldBeOfType<ClipMarkerOperationOutcome.MarkerCreated>();
        http.ClipPosts.ShouldBe(3);
        http.MarkerPosts.ShouldBe(1);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var clips = await verify
            .TwitchClips.OrderBy(clip => clip.HostId)
            .ThenBy(clip => clip.Id)
            .ToArrayAsync();
        clips.Length.ShouldBe(4);
        clips.Single(clip => clip.HostId == 1).Status.ShouldBe(TwitchClipStatus.Ambiguous);
        clips
            .Single(clip => clip.IdempotencyKey == "same-key" && clip.HostId == 2)
            .Status.ShouldBe(TwitchClipStatus.Available);
        clips
            .Single(clip => clip.IdempotencyKey == "expires")
            .Status.ShouldBe(TwitchClipStatus.Expired);
        var persistedMarker = (
            await verify.TwitchStreamMarkers.ToArrayAsync()
        ).ShouldHaveSingleItem();
        persistedMarker.Status.ShouldBe(TwitchStreamMarkerStatus.Succeeded);
        persistedMarker.VideoId.ShouldBe("video-id");
        persistedMarker.MarkerUrl.ShouldBe("https://twitch.test/marker");
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
            new HelixClient(http),
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

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        internal void Advance(TimeSpan by)
        {
            _now += by;
        }
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        hostId == 1 ? "one-id" : "two-id",
                        hostId == 1 ? "one" : "two",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes)
                )
            );
        }

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        )
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
        }
    }

    private sealed class ClipMarkerHttpClientFactory : IHttpClientFactory
    {
        internal int ClipPosts { get; private set; }

        internal int ClipGets { get; private set; }

        internal int MarkerPosts { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this), disposeHandler: false);
        }

        private sealed class Handler(ClipMarkerHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                switch (request.RequestUri!.AbsolutePath, request.Method.Method)
                {
                    case ("/helix/clips", "POST") when owner.ClipPosts++ == 0:
                        throw new HttpRequestException("ambiguous clip post");
                    case ("/helix/clips", "POST"):
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
                        (await request.Content!.ReadAsStringAsync(cancellationToken)).ShouldContain(
                            "Important moment"
                        );
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

            private static HttpResponseMessage Json(string json)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}

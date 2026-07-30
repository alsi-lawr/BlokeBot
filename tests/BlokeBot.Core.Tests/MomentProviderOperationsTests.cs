using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Moments;
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
        ambiguous.ClipId.ShouldNotBeNull();
        ambiguous.MarkerId.ShouldNotBeNull();
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

        outcome.ShouldBeOfType<MomentProviderOutcome.Failed>();
        http.ClipPosts.ShouldBe(1);
        http.MarkerPosts.ShouldBe(0);
    }

    private static async Task<SqliteBlokeBotDbFactory> CreateDatabaseAsync()
    {
        var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await database.CreateDbContextAsync();
        db.Hosts.Add(
            new BotHost
            {
                Login = "one",
                DisplayName = "One",
                TwitchUserId = "one-id",
                EnabledFeatures = HostFeatureFlags.All,
            }
        );
        await db.SaveChangesAsync();
        return database;
    }

    private static MomentProviderOperations CreateOperations(
        SqliteBlokeBotDbFactory database,
        ProviderHttpClientFactory http
    )
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var events = TestEventBus.Create<AppEventKind>();
        var clips = new ClipMarkerService(
            database,
            new ReadyBroadcasterProvider(),
            new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            events,
            new DurableAlertService(database, clock, events),
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
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        "one-id",
                        "one",
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

    private sealed class ProviderHttpClientFactory(ClipFailure clipFailure, bool markerAmbiguous)
        : IHttpClientFactory
    {
        private bool _markerAmbiguous { get; } = markerAmbiguous;

        public int ClipPosts { get; private set; }

        public int MarkerPosts { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this), disposeHandler: false);
        }

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
                    if (owner._markerAmbiguous)
                    {
                        throw new HttpRequestException("ambiguous marker response");
                    }
                    return Task.FromResult(
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

        private HttpResponseMessage ClipResponse()
        {
            return clipFailure switch
            {
                ClipFailure.ProviderRejected => Error(HttpStatusCode.BadRequest, "not permitted"),
                ClipFailure.Offline => Error(HttpStatusCode.BadRequest, "channel is not live"),
                ClipFailure.VodsDisabled => Error(HttpStatusCode.BadRequest, "VODs disabled"),
                ClipFailure.Unauthorized => Error(HttpStatusCode.Unauthorized, "invalid token"),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        private static HttpResponseMessage Error(HttpStatusCode status, string message)
        {
            return new(status)
            {
                Content = new StringContent(
                    $$"""{"message":"{{message}}"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }

        private static HttpResponseMessage Json(string json)
        {
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}

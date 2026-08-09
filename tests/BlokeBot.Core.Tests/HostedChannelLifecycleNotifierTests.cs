using System.Net;
using System.Text;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelLifecycleNotifierTests
{
    [Test]
    public async Task InterruptedStop_ApplicationRestart_RecoversStoppedAndLeavesStartingResumable()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var changedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.AddRange(
                Host("stopping", BotChannelRuntimeState.Stopping, changedAtUtc),
                Host("starting", BotChannelRuntimeState.Starting, changedAtUtc)
            );
            _ = await db.SaveChangesAsync();
        }

        var events = TestEventBus.Create<AppEventKind>();
        var notifications = 0;
        _ = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostedChannelStopRecovery"),
            (_, _) =>
            {
                notifications++;
                return ValueTask.CompletedTask;
            }
        );
        var lifecycle = new HostedChannelRuntimeLifecycleService(
            dbFactory,
            new HostedChannelChangeNotifier(events)
        );

        await lifecycle.RecoverInterruptedStopsAsync(CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var states = await verify.Hosts.ToDictionaryAsync(host => host.Login);
        states["stopping"].BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
        states["stopping"].BotRuntimeStateChangedAtUtc!.Value.ShouldBeGreaterThan(changedAtUtc);
        states["starting"].BotRuntimeState.ShouldBe(BotChannelRuntimeState.Starting);
        states["starting"].BotRuntimeStateChangedAtUtc.ShouldBe(changedAtUtc);
        notifications.ShouldBe(1);
    }

    [Test]
    public async Task HostedChannel_StartAndReconnect_ReconcilesExternalAndFormerlyLocalPolls()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedStartingHostAsync(dbFactory);
        var events = TestEventBus.Create<AppEventKind>();
        var operationsChanges = 0;
        _ = events.Subscribe(
            AppEventKind.TwitchOperationsChanged,
            ObserverIdentity.Named("Test.HostedChannelPollReconciliation"),
            (_, _) =>
            {
                operationsChanges++;
                return ValueTask.CompletedTask;
            }
        );
        var http = new PollHttpClientFactory();
        http.Enqueue(PollResponse("external-poll", "ACTIVE", votes: 1));
        http.Enqueue(PollResponse("external-poll", "TERMINATED", votes: 2));
        http.Enqueue(EmptyPollResponse());
        http.Enqueue(EmptyPollResponse());
        var notifier = new HostedChannelLifecycleNotifier(
            new HostedChannelRuntimeLifecycleService(
                dbFactory,
                new HostedChannelChangeNotifier(events)
            ),
            CreatePollService(dbFactory, http, events),
            new ClipMarkerService(
                dbFactory,
                new BroadcasterOperationAuthorization(
                    new ReadyBroadcasterProvider(),
                    new DurableAlertService(dbFactory, TimeProvider.System, events)
                ),
                new HelixClient(
                    new PollHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                BotSettings.FromOptions(
                    new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
                ),
                events,
                TimeProvider.System,
                new NativeTwitchFeatureGate(dbFactory)
            )
        );

        await notifier.ChannelStartedAsync("Streamer", CancellationToken.None);
        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.TwitchPolls.Add(
                new TwitchPoll
                {
                    HostId = 1,
                    ProviderPollId = "missing-poll",
                    Title = "Missing",
                    ChoicesJson = "[]",
                    Status = TwitchPollStatus.Active,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                }
            );
            for (var index = 0; index < 99; index++)
            {
                _ = db.TwitchPolls.Add(
                    new TwitchPoll
                    {
                        HostId = 1,
                        ProviderPollId = $"history-{index:D3}",
                        Title = "Retained result",
                        ChoicesJson = "[]",
                        Status = TwitchPollStatus.Completed,
                        StartedAtUtc = DateTime.UtcNow.AddDays(-2),
                        EndedAtUtc = DateTime.UtcNow.AddDays(-1).AddSeconds(index),
                        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1).AddSeconds(index),
                    }
                );
            }
            _ = await db.SaveChangesAsync();
        }

        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);
        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.Hosts.Select(host => host.BotRuntimeState).SingleAsync()).ShouldBe(
            BotChannelRuntimeState.Started
        );
        var polls = await verify.TwitchPolls.OrderBy(poll => poll.ProviderPollId).ToArrayAsync();
        polls.Length.ShouldBe(100);
        var external = polls.Single(poll => poll.ProviderPollId == "external-poll");
        external.Status.ShouldBe(TwitchPollStatus.Terminated);
        _ = external.EndedAtUtc.ShouldNotBeNull();
        var missing = polls.Single(poll => poll.ProviderPollId == "missing-poll");
        missing.Status.ShouldBe(TwitchPollStatus.Archived);
        _ = missing.EndedAtUtc.ShouldNotBeNull();
        polls
            .Count(poll => poll.ProviderPollId.StartsWith("history-", StringComparison.Ordinal))
            .ShouldBe(98);
        operationsChanges.ShouldBe(3);
        http.Requests.ShouldBe(4);
    }

    private static PollService CreatePollService(
        SqliteBlokeBotDbFactory dbFactory,
        PollHttpClientFactory http,
        EventBus<AppEventKind> events
    ) =>
        new PollService(
            dbFactory,
            new BroadcasterOperationAuthorization(
                new ReadyBroadcasterProvider(),
                new DurableAlertService(dbFactory, TimeProvider.System, events)
            ),
            new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            events,
            new NativeTwitchFeatureGate(dbFactory)
        );

    private static async Task SeedStartingHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        _ = db.Hosts.Add(
            new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
                BotRuntimeState = BotChannelRuntimeState.Starting,
                BotRuntimeStateChangedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static BotHost Host(
        string login,
        BotChannelRuntimeState state,
        DateTime changedAtUtc
    ) =>
        new()
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            BotRuntimeState = state,
            BotRuntimeStateChangedAtUtc = changedAtUtc,
            CreatedAtUtc = changedAtUtc,
        };

    private static HttpResponseMessage PollResponse(string id, string status, int votes) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {"data":[{"id":"{{id}}","broadcaster_id":"streamer-id","title":"Question","choices":[{"id":"yes","title":"Yes","votes":{{votes}},"channel_points_votes":0}],"status":"{{status}}","started_at":"2026-07-26T10:00:00Z","ends_at":"2026-07-26T10:01:00Z"}]}
                """,
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static HttpResponseMessage EmptyPollResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json"),
        };

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
                        "streamer-id",
                        "streamer",
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

    private sealed class PollHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<HttpResponseMessage> _responses = [];

        internal int Requests { get; private set; }

        internal void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        public HttpClient CreateClient(string name) =>
            new(new Handler(this), disposeHandler: false);

        private sealed class Handler(PollHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                request.Method.ShouldBe(HttpMethod.Get);
                request.RequestUri!.AbsolutePath.ShouldBe("/helix/polls");
                owner.Requests++;
                return Task.FromResult(owner._responses.Dequeue());
            }
        }
    }
}

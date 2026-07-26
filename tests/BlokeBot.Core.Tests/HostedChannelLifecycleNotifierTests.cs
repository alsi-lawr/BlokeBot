using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelLifecycleNotifierTests
{
    [Test]
    public async Task HostedChannel_StartAndReconnect_ReconcilesExternalAndFormerlyLocalPolls()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedStartingHostAsync(dbFactory);
        var events = TestEventBus.Create<AppEventKind>();
        var operationsChanges = 0;
        events.Subscribe(
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
            CreatePollService(dbFactory, http, events)
        );

        await notifier.ChannelStartedAsync("Streamer", CancellationToken.None);
        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.TwitchPolls.Add(
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
            await db.SaveChangesAsync();
        }

        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);
        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.Hosts.Select(host => host.BotRuntimeState).SingleAsync()).ShouldBe(
            BotChannelRuntimeState.Started
        );
        var polls = await verify.TwitchPolls.OrderBy(poll => poll.ProviderPollId).ToArrayAsync();
        polls.Length.ShouldBe(2);
        polls[0].ProviderPollId.ShouldBe("external-poll");
        polls[0].Status.ShouldBe(TwitchPollStatus.Terminated);
        polls[0].EndedAtUtc.ShouldNotBeNull();
        polls[1].ProviderPollId.ShouldBe("missing-poll");
        polls[1].Status.ShouldBe(TwitchPollStatus.Archived);
        polls[1].EndedAtUtc.ShouldNotBeNull();
        operationsChanges.ShouldBe(3);
        http.Requests.ShouldBe(4);
    }

    private static PollService CreatePollService(
        SqliteBlokeBotDbFactory dbFactory,
        PollHttpClientFactory http,
        EventBus<AppEventKind> events
    )
    {
        return new PollService(
            dbFactory,
            new ReadyBroadcasterProvider(),
            new HelixClient(http),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            events,
            new DurableAlertService(dbFactory, TimeProvider.System, events)
        );
    }

    private static async Task SeedStartingHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Hosts.Add(
            new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
                BotRuntimeState = BotChannelRuntimeState.Starting,
                BotRuntimeStateChangedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private static HttpResponseMessage PollResponse(string id, string status, int votes)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {"data":[{"id":"{{id}}","broadcaster_id":"streamer-id","title":"Question","choices":[{"id":"yes","title":"Yes","votes":{{votes}},"channel_points_votes":0}],"status":"{{status}}","started_at":"2026-07-26T10:00:00Z","ends_at":"2026-07-26T10:01:00Z"}]}
                """,
                Encoding.UTF8,
                "application/json"
            ),
        };
    }

    private static HttpResponseMessage EmptyPollResponse()
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json"),
        };
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
                        "streamer-id",
                        "streamer",
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

    private sealed class PollHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<HttpResponseMessage> _responses = [];

        internal int Requests { get; private set; }

        internal void Enqueue(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this), disposeHandler: false);
        }

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

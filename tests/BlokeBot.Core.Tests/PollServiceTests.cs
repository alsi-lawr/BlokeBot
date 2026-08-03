using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PollServiceTests
{
    [Test]
    public async Task DisabledSwitch_RetainsTemplatesAndSuppressesMutationsAndInboundEvents()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All & ~HostFeatureFlags.Polls,
                Login = "host",
                DisplayName = "Host",
                TwitchUserId = "host-id",
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            _ = db.TwitchPollTemplates.Add(Template(host.Id, "Retained template"));
            _ = await db.SaveChangesAsync();
        }
        var http = new PollHttpClientFactory();
        var service = CreateService(dbFactory, http);

        var state = await service.LoadAsync(1, CancellationToken.None);
        var save = await service.SaveTemplateAsync(
            1,
            new PollTemplateDraft("Suppressed", ["Yes", "No"], 60, false, 0),
            CancellationToken.None
        );
        await service.PollReceivedAsync(
            ProviderPollEvent("ACTIVE", 1, "suppressed-event"),
            CancellationToken.None
        );

        _ = state.Authorization.ShouldBeOfType<PollAuthorizationReadiness.Disabled>();
        state.Templates.ShouldBeEmpty();
        _ = save.ShouldBeOfType<PollOperationOutcome.NotReady>();
        http.CreateRequests.ShouldBe(0);
        await using (var verifyDisabled = await dbFactory.CreateDbContextAsync())
        {
            (await verifyDisabled.TwitchPollTemplates.CountAsync()).ShouldBe(1);
            (await verifyDisabled.TwitchPolls.CountAsync()).ShouldBe(0);
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.Polls;
            _ = await verifyDisabled.SaveChangesAsync();
        }

        var restored = await service.LoadAsync(1, CancellationToken.None);
        restored.Templates.ShouldHaveSingleItem().Title.ShouldBe("Retained template");
        restored.ActivePoll.ShouldBeNull();
        http.CreateRequests.ShouldBe(0);
    }

    [Test]
    public async Task PollCreation_MapsActiveConflictAndRejectsVotingCostsAboveOneMillionBeforePersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var http = new PollHttpClientFactory();
        http.Enqueue(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"Bad Request","status":400,"message":"A poll is already active"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        var service = CreateService(dbFactory, http);

        var accepted = await service.SaveTemplateAsync(
            1,
            new PollTemplateDraft("Question", ["Yes", "No"], 60, true, 1_000_000),
            CancellationToken.None
        );
        var rejected = await service.SaveTemplateAsync(
            1,
            new PollTemplateDraft("Too expensive", ["Yes", "No"], 60, true, 1_000_001),
            CancellationToken.None
        );
        var started = await service.StartAsync(
            1,
            accepted.ShouldBeOfType<PollOperationOutcome.TemplateSaved>().Template.Id,
            CancellationToken.None
        );

        _ = rejected.ShouldBeOfType<PollOperationOutcome.InvalidTemplate>();
        _ = started.ShouldBeOfType<PollOperationOutcome.ActivePollExists>();
        await using var verify = await dbFactory.CreateDbContextAsync();
        var template = (await verify.TwitchPollTemplates.ToArrayAsync()).ShouldHaveSingleItem();
        template.ChannelPointsPerVote.ShouldBe(1_000_000);
        http.CreateRequests.ShouldBe(1);
    }

    [Test]
    public async Task PollEvents_DuplicateAndDelayedProgressAfterEnd_LeaveTheTerminalResultIntact()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var service = CreateService(dbFactory, new PollHttpClientFactory());
        await service.PollReceivedAsync(
            ProviderPollEvent("ACTIVE", 1, "progress-1"),
            CancellationToken.None
        );
        await service.PollReceivedAsync(
            ProviderPollEvent("ACTIVE", 1, "progress-1"),
            CancellationToken.None
        );
        await service.PollReceivedAsync(
            ProviderPollEvent("COMPLETED", 2, "end"),
            CancellationToken.None
        );
        await service.PollReceivedAsync(
            ProviderPollEvent("ACTIVE", 99, "delayed-progress"),
            CancellationToken.None
        );

        var state = await service.LoadAsync(1, CancellationToken.None);

        state.ActivePoll.ShouldBeNull();
        var result = state.Results.ShouldHaveSingleItem();
        result.Status.ShouldBe("Completed");
        result.Choices.ShouldHaveSingleItem().Votes.ShouldBe(2);
        _ = result.EndedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task MissingBroadcasterAuthority_LoadingPolls_ProvidesReauthorizationAndDurableAlert()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var events = TestEventBus.Create<AppEventKind>();
        var service = new PollService(
            dbFactory,
            new StaticBroadcasterProvider(
                new TokenStatus.Unavailable(
                    AccessTokenUnavailableReason.MissingRefreshToken,
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            ),
            new HelixClient(
                new PollHttpClientFactory(),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            events,
            new DurableAlertService(dbFactory, TimeProvider.System, events),
            new NativeTwitchFeatureGate(dbFactory)
        );

        var state = await service.LoadAsync(1, CancellationToken.None);

        state
            .Authorization.ShouldBeOfType<PollAuthorizationReadiness.NeedsBroadcasterAuthorization>()
            .ReauthorizationUrl.ShouldBe("/oauth/broadcaster/start");
        await using var verify = await dbFactory.CreateDbContextAsync();
        var alert = await verify.DurableAlerts.SingleAsync();
        alert.Source.ShouldBe("twitch-broadcaster-authorization");
        alert.LinkPath.ShouldBe("/twitch-operations");
    }

    [Test]
    public async Task ReconciledExternalPoll_EndingRequiresConfirmationBeforeProviderMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var http = new PollHttpClientFactory();
        http.Enqueue(CreateResponse("external-poll", "host-id", "External poll", "ACTIVE"));
        http.Enqueue(CreateResponse("external-poll", "host-id", "External poll", "TERMINATED"));
        var service = CreateService(dbFactory, http);

        await service.ReconcileAsync(1, CancellationToken.None);
        var guarded = await service.EndAsync(1, false, CancellationToken.None);
        var ended = await service.EndAsync(1, true, CancellationToken.None);

        _ = guarded.ShouldBeOfType<PollOperationOutcome.ConfirmationRequired>();
        ended.ShouldBeOfType<PollOperationOutcome.Ended>().Poll.Status.ShouldBe("Terminated");
        http.EndRequests.ShouldBe(1);
        (await service.LoadAsync(1, CancellationToken.None)).ActivePoll.ShouldBeNull();
    }

    private static PollService CreateService(
        SqliteBlokeBotDbFactory dbFactory,
        PollHttpClientFactory http
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
            new DurableAlertService(dbFactory, TimeProvider.System, events),
            new NativeTwitchFeatureGate(dbFactory)
        );
    }

    private static TwitchPollTemplate Template(int hostId, string title) =>
        new()
        {
            HostId = hostId,
            Title = title,
            DurationSeconds = 60,
            CreatedAtUtc = DateTime.UtcNow,
            Choices =
            [
                new TwitchPollTemplateChoice { Position = 0, Title = "Yes" },
                new TwitchPollTemplateChoice { Position = 1, Title = "No" },
            ],
        };

    private static EventSubPollEvent ProviderPollEvent(
        string status,
        int votes,
        string messageId
    ) =>
        new(
            "host-id",
            "host",
            "poll-id",
            "Question",
            [new EventSubPollChoice("yes", "Yes", votes, 0)],
            status,
            new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 26, 10, 1, 0, TimeSpan.Zero),
            messageId
        );

    private static HttpResponseMessage CreateResponse(
        string id,
        string broadcasterId,
        string title,
        string status
    ) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {"data":[{"id":"{{id}}","broadcaster_id":"{{broadcasterId}}","title":"{{title}}","choices":[{"id":"yes","title":"Yes","votes":1,"channel_points_votes":0},{"id":"no","title":"No","votes":0,"channel_points_votes":0}],"status":"{{status}}","started_at":"2026-07-26T10:00:00Z","ends_at":"2026-07-26T10:01:00Z"}]}
                """,
                Encoding.UTF8,
                "application/json"
            ),
        };

    private sealed class ReadyBroadcasterProvider : StaticBroadcasterProvider
    {
        public ReadyBroadcasterProvider()
            : base(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        "host-id",
                        "host",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            ) { }
    }

    private class StaticBroadcasterProvider(TokenStatus status)
        : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) => Task.FromResult(status);

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

        internal int CreateRequests { get; private set; }

        internal int ActivePollRequests { get; private set; }

        internal int EndRequests { get; private set; }

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
                request.RequestUri!.AbsolutePath.ShouldBe("/helix/polls");
                switch (request.Method)
                {
                    case var method when method == HttpMethod.Post:
                        owner.CreateRequests++;
                        break;
                    case var method when method == HttpMethod.Get:
                        owner.ActivePollRequests++;
                        break;
                    case var method when method == HttpMethod.Patch:
                        owner.EndRequests++;
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected poll request.");
                }
                return Task.FromResult(owner._responses.Dequeue());
            }
        }
    }
}

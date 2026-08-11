using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class RaidCollaborationServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DuplicateAndWindowedIncomingRaids_RecordEachRaidOnceAndDeliverWelcomeOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.RaidCollaboration);
        var fixture = Fixture(database);
        fixture.Provider.SetLive("raider", "Raid Game", "en", 42);
        var first = Incoming("event-1", _now, "raider-id", "raider");

        await fixture.Service.IncomingRaidReceivedAsync(first, default);
        await fixture.Service.IncomingRaidReceivedAsync(first, default);
        await fixture.Service.IncomingRaidReceivedAsync(
            Incoming("event-2", _now.AddMinutes(10), "raider-id", "raider"),
            default
        );

        _ = fixture.Welcome.Messages.ShouldHaveSingleItem();
        fixture.Shoutouts.Targets.ShouldHaveSingleItem().ShouldBe("raider");
        fixture.Provider.LoadedLogins.Count(value => value == "raider").ShouldBe(2);
        await using var verify = await database.CreateDbContextAsync();
        var history = await verify
            .RaidCollaborationHistory.OrderBy(value => value.OccurredAtUtc)
            .ToArrayAsync();
        history.Length.ShouldBe(2);
        history[0].WelcomeOutcome.ShouldBe(RaidWelcomeOutcome.Delivered);
        history[0].ShoutoutOutcome.ShouldBe(RaidShoutoutOutcome.Sent);
        history[1].WelcomeOutcome.ShouldBe(RaidWelcomeOutcome.Deduplicated);
        history[1].ShoutoutOutcome.ShouldBe(RaidShoutoutOutcome.Deduplicated);
        fixture
            .Events.Events.Count(value =>
                value.Kind == RaidCollaborationDomainEventKind.IncomingRaidRecorded
            )
            .ShouldBe(2);
        fixture
            .Events.Events.Count(value =>
                value.Kind == RaidCollaborationDomainEventKind.WelcomeDelivered
            )
            .ShouldBe(1);
    }

    [Test]
    public async Task DisabledPreEnableAndIdDisagreement_CauseNoEffectsMutationOrReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.None);
        var fixture = Fixture(database);
        fixture.Provider.SetLive("raider", "Raid Game", "en", 42);

        await fixture.Service.IncomingRaidReceivedAsync(
            Incoming("disabled", _now, "raider-id", "raider"),
            default
        );
        _ = (
            await fixture.Service.LoadAsync(hostId, default)
        ).ShouldBeOfType<RaidCollaborationLoadOutcome.FeatureDisabled>();
        await using (var enable = await database.CreateDbContextAsync())
        {
            var host = await enable.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.RaidCollaboration;
            host.RaidCollaborationAcceptEventsAfterUtc = _now.AddMinutes(1).UtcDateTime;
            _ = await enable.SaveChangesAsync();
        }
        await fixture.Service.IncomingRaidReceivedAsync(
            Incoming("disabled", _now, "raider-id", "raider"),
            default
        );
        await fixture.Service.IncomingRaidReceivedAsync(
            Incoming(
                "id-disagreement",
                _now.AddMinutes(2),
                "raider-id",
                "raider",
                targetId: "unrelated-id",
                targetLogin: "alpha"
            ),
            default
        );

        fixture.Provider.LoadedLogins.ShouldBeEmpty();
        fixture.Welcome.Messages.ShouldBeEmpty();
        fixture.Shoutouts.Targets.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.RaidCollaborationHistory.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Shortlist_UsesOnlyHostApprovedLiveMatchesAndExplainsEveryExclusion()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.RaidCollaboration);
        var otherHostId = await SeedHostAsync(database, "beta", HostFeatureFlags.RaidCollaboration);
        await SeedConfigurationAsync(
            database,
            hostId,
            ["eligible", "offline", "wronglanguage", "recent"],
            language: "en",
            categories: ["Celeste"]
        );
        await SeedConfigurationAsync(database, otherHostId, ["private-other"]);
        await using (var history = await database.CreateDbContextAsync())
        {
            _ = history.RaidCollaborationHistory.Add(
                History(hostId, "recent-event", RaidDirection.Outgoing, "recent", _now.AddDays(-2))
            );
            _ = await history.SaveChangesAsync();
        }
        var fixture = Fixture(database);
        fixture.Provider.SetLive("eligible", "Celeste", "en", 70);
        fixture.Provider.SetOffline("offline");
        fixture.Provider.SetLive("wronglanguage", "Celeste", "fr", 80);
        fixture.Provider.SetLive("recent", "Celeste", "en", 90);

        var loaded = (await fixture.Service.LoadAsync(hostId, default))
            .ShouldBeOfType<RaidCollaborationLoadOutcome.Loaded>()
            .Dashboard;

        loaded.EligibleChannels.ShouldHaveSingleItem().Login.ShouldBe("eligible");
        loaded
            .ExcludedChannels.Select(value => value.Login)
            .ShouldBe(["offline", "recent", "wronglanguage"], ignoreOrder: true);
        loaded
            .ExcludedChannels.Single(value => value.Login == "offline")
            .Reasons.ShouldContain("Channel is offline.");
        loaded
            .ExcludedChannels.Single(value => value.Login == "wronglanguage")
            .Reasons.ShouldContain(value => value.Contains("Language", StringComparison.Ordinal));
        loaded
            .ExcludedChannels.Single(value => value.Login == "recent")
            .Reasons.ShouldContain(value =>
                value.Contains("relationship gap", StringComparison.OrdinalIgnoreCase)
            );
        fixture.Provider.LoadedLogins.ShouldNotContain("private-other");
        _ = (
            await fixture.Service.SendShoutoutAsync(hostId, "eligible", default)
        ).ShouldBeOfType<ShoutoutOperationOutcome.Sent>();
        fixture.Shoutouts.Targets.ShouldBe(["eligible"]);
    }

    [Test]
    public async Task ConfirmedRaid_RechecksApprovalEligibilityAndFeatureBeforeProviderStart()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.RaidCollaboration);
        await SeedConfigurationAsync(database, hostId, ["eligible"]);
        var fixture = Fixture(database);
        fixture.Provider.SetLive("eligible", "Celeste", "en", 70);

        _ = (
            await fixture.Service.StartConfirmedRaidAsync(hostId, "unapproved", default)
        ).ShouldBeOfType<ConfirmedRaidStartOutcome.TargetNotApproved>();
        fixture.Provider.StartedLogins.ShouldBeEmpty();
        await using (var disable = await database.CreateDbContextAsync())
        {
            var host = await disable.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await disable.SaveChangesAsync();
        }
        _ = (
            await fixture.Service.StartConfirmedRaidAsync(hostId, "eligible", default)
        ).ShouldBeOfType<ConfirmedRaidStartOutcome.FeatureDisabled>();
        fixture.Provider.LoadedLogins.ShouldBeEmpty();
        fixture.Provider.StartedLogins.ShouldBeEmpty();

        var broadcasterTokens = new RecordingBroadcasterTokens();
        var twitch = new RecordingHttpClientFactory();
        var provider = new TwitchRaidCollaborationProvider(
            database,
            broadcasterTokens,
            new HelixClient(twitch, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            Settings(),
            new FixedTimeProvider(_now)
        );

        _ = (
            await provider.StartConfirmedRaidAsync(hostId, "eligible-id", "eligible", default)
        ).ShouldBeOfType<ConfirmedRaidStartOutcome.FeatureDisabled>();
        broadcasterTokens.StatusRequests.ShouldBe(0);
        twitch.Requests.ShouldBe(0);
    }

    private static FixtureState Fixture(SqliteBlokeBotDbFactory database)
    {
        var provider = new RecordingProvider();
        var welcome = new RecordingWelcomeSender();
        var shoutouts = new RecordingShoutouts();
        var domainEvents = new RecordingDomainEvents();
        return new(
            new RaidCollaborationService(
                database,
                provider,
                welcome,
                shoutouts,
                [domainEvents],
                TestEventBus.Create<AppEventKind>(),
                new FixedTimeProvider(_now)
            ),
            provider,
            welcome,
            shoutouts,
            domainEvents
        );
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            EnabledFeatures = features,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedConfigurationAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        IReadOnlyList<string> channels,
        string language = "",
        IReadOnlyList<string>? categories = null
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.RaidCollaborationSettings.Add(
            new RaidCollaborationSettings
            {
                HostId = hostId,
                WelcomeEnabled = true,
                NativeShoutoutEnabled = true,
                DeduplicationWindowMinutes = 60,
                Language = language,
                EligibleCategories = string.Join('\n', categories ?? []),
                RelationshipCooldownHours = 336,
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        foreach (var login in channels)
        {
            _ = db.ApprovedRaidChannels.Add(
                new ApprovedRaidChannel
                {
                    HostId = hostId,
                    Login = login,
                    DisplayName = login,
                    ApprovedAtUtc = _now.UtcDateTime,
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
        }
        _ = await db.SaveChangesAsync();
    }

    private static EventSubIncomingRaidEvent Incoming(
        string messageId,
        DateTimeOffset timestamp,
        string raiderId,
        string raiderLogin,
        string targetId = "alpha-id",
        string targetLogin = "alpha"
    ) =>
        new(
            messageId,
            timestamp,
            raiderId,
            raiderLogin,
            raiderLogin,
            targetId,
            targetLogin,
            targetLogin,
            42
        );

    private static BotSettings Settings() =>
        BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );

    private static RaidCollaborationHistoryEntry History(
        int hostId,
        string messageId,
        RaidDirection direction,
        string login,
        DateTimeOffset occurredAt
    ) =>
        new()
        {
            HostId = hostId,
            ProviderMessageId = messageId,
            Direction = direction,
            OtherTwitchUserId = $"{login}-id",
            OtherLogin = login,
            OtherDisplayName = login,
            ViewerCount = 40,
            OccurredAtUtc = occurredAt.UtcDateTime,
            RecordedAtUtc = occurredAt.UtcDateTime,
        };

    private sealed record FixtureState(
        RaidCollaborationService Service,
        RecordingProvider Provider,
        RecordingWelcomeSender Welcome,
        RecordingShoutouts Shoutouts,
        RecordingDomainEvents Events
    );

    private sealed class RecordingProvider : IRaidCollaborationProvider
    {
        private readonly Dictionary<string, RaidChannelSnapshotOutcome> _channels = [];
        internal List<string> LoadedLogins { get; } = [];
        internal List<string> StartedLogins { get; } = [];

        internal void SetLive(string login, string category, string language, int viewers) =>
            _channels[login] = new RaidChannelSnapshotOutcome.Available(
                new(
                    $"{login}-id",
                    login,
                    login,
                    $"{login}-stream",
                    category,
                    language,
                    $"{login} title",
                    viewers,
                    null
                )
            );

        internal void SetOffline(string login) =>
            _channels[login] = new RaidChannelSnapshotOutcome.Offline(login);

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        )
        {
            LoadedLogins.Add(login);
            return Task.FromResult(
                _channels.GetValueOrDefault(login, new RaidChannelSnapshotOutcome.Offline(login))
            );
        }

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            StartedLogins.Add(targetLogin);
            return Task.FromResult<ConfirmedRaidStartOutcome>(
                new ConfirmedRaidStartOutcome.Started(targetLogin)
            );
        }

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(true);
    }

    private sealed class RecordingWelcomeSender : IRaidWelcomeSender
    {
        internal List<string> Messages { get; } = [];

        public Task<bool> SendAsync(
            int hostId,
            string hostLogin,
            string providerMessageId,
            string message,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingShoutouts : IRaidCollaborationShoutoutProvider
    {
        internal List<string> Targets { get; } = [];

        public Task<ShoutoutDashboardState> LoadAsync(
            int hostId,
            string? targetLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new ShoutoutDashboardState(null, new ShoutoutTargetCooldownReadiness.Unknown(), [])
            );

        public Task<ShoutoutOperationOutcome> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            Targets.Add(targetLogin);
            return Task.FromResult<ShoutoutOperationOutcome>(
                new ShoutoutOperationOutcome.Sent(targetLogin)
            );
        }
    }

    private sealed class RecordingDomainEvents : IRaidCollaborationDomainEventObserver
    {
        internal List<RaidCollaborationDomainEvent> Events { get; } = [];

        public ValueTask CollaborationEventAsync(
            RaidCollaborationDomainEvent domainEvent,
            CancellationToken cancellationToken
        )
        {
            Events.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingBroadcasterTokens : IHostBroadcasterTokenStatusProvider
    {
        internal int StatusRequests { get; private set; }

        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        )
        {
            StatusRequests++;
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Unavailable(AccessTokenUnavailableReason.MissingRefreshToken, [])
            );
        }

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        internal int Requests { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(RecordingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.Requests++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}

using System.Net;
using BlokeBot.Core;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Auth;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class HostBroadcasterAuthorizationClearTests
{
    [Test]
    public async Task Clear_DeletesOnlyRequestedAuthorization_NotifiesOnceAndUsesNoTwitchTransport()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, otherHostId) = await SeedTwoAuthorizationsAsync(factory);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.BroadcasterClear.Success"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var http = new RecordingHttpClientFactory();

        var outcome = await Service(factory, events, http)
            .ClearAsync(hostId, CancellationToken.None);

        outcome.ShouldBeOfType<HostBroadcasterAuthorizationClearOutcome.Cleared>();
        notificationCount.ShouldBe(1);
        http.RequestCount.ShouldBe(0);
        await using var verify = await factory.CreateDbContextAsync();
        (
            await verify.HostBroadcasterAuthorizations.AnyAsync(value => value.HostId == hostId)
        ).ShouldBeFalse();
        (
            await verify.HostBroadcasterAuthorizations.AnyAsync(value =>
                value.HostId == otherHostId
            )
        ).ShouldBeTrue();
        (await verify.Hosts.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task Clear_MissingAuthorization_IsIdempotentWithoutNotificationOrTransport()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory, "host", includeAuthorization: false);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.BroadcasterClear.Missing"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var http = new RecordingHttpClientFactory();

        var outcome = await Service(factory, events, http)
            .ClearAsync(hostId, CancellationToken.None);

        outcome.ShouldBeOfType<HostBroadcasterAuthorizationClearOutcome.AlreadyDisconnected>();
        notificationCount.ShouldBe(0);
        http.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Clear_ObserverFailure_KeepsDeletionAndReturnsIncompleteNotification()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory, "host", includeAuthorization: true);
        var recording = TestEventBus.CreateContinueAndRecord<AppEventKind>();
        var invocationCount = 0;
        using var subscription = recording.Events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.BroadcasterClear.Failure"),
            (_, _) =>
            {
                invocationCount++;
                return ValueTask.FromException(
                    new InvalidOperationException("runtime unavailable")
                );
            }
        );
        var http = new RecordingHttpClientFactory();

        var outcome = await Service(factory, recording.Events, http)
            .ClearAsync(hostId, CancellationToken.None);

        var failed =
            outcome.ShouldBeOfType<HostBroadcasterAuthorizationClearOutcome.ClearedWithNotificationFailures>();
        failed.FailureCount.ShouldBe(1);
        invocationCount.ShouldBe(1);
        recording.Reports.ShouldHaveSingleItem();
        http.RequestCount.ShouldBe(0);
        await AssertAuthorizationMissingAsync(factory, hostId);
    }

    [Test]
    public async Task Clear_FailureHandlingEscalation_KeepsDeletionAndReturnsEscalatedNotification()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory, "host", includeAuthorization: true);
        var events = TestEventBus.Create<AppEventKind>();
        var invocationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.BroadcasterClear.Escalation"),
            (_, _) =>
            {
                invocationCount++;
                return ValueTask.FromException(
                    new InvalidOperationException("runtime unavailable")
                );
            }
        );
        var http = new RecordingHttpClientFactory();

        var outcome = await Service(factory, events, http)
            .ClearAsync(hostId, CancellationToken.None);

        var escalated =
            outcome.ShouldBeOfType<HostBroadcasterAuthorizationClearOutcome.ClearedWithNotificationEscalation>();
        escalated.ObserverFailureCount.ShouldBe(1);
        escalated.HandlingFailureCount.ShouldBe(1);
        invocationCount.ShouldBe(1);
        http.RequestCount.ShouldBe(0);
        await AssertAuthorizationMissingAsync(factory, hostId);
    }

    [Test]
    public async Task Clear_PersistenceFailure_RetainsAuthorizationAndSkipsNotificationAndTransport()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(factory, "host", includeAuthorization: true);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_broadcaster_authorization_delete
                BEFORE DELETE ON host_broadcaster_authorizations
                BEGIN
                    SELECT RAISE(ABORT, 'intentional broadcaster authorization delete failure');
                END;
                """
            );
        }
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.BroadcasterClear.PersistenceFailure"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var http = new RecordingHttpClientFactory();

        await Should.ThrowAsync<DbUpdateException>(() =>
            Service(factory, events, http).ClearAsync(hostId, CancellationToken.None)
        );

        notificationCount.ShouldBe(0);
        http.RequestCount.ShouldBe(0);
        await using var verify = await factory.CreateDbContextAsync();
        (
            await verify.HostBroadcasterAuthorizations.AnyAsync(value => value.HostId == hostId)
        ).ShouldBeTrue();
    }

    private static HostBroadcasterAuthorizationService Service(
        SqliteBlokeBotDbFactory factory,
        EventBus<AppEventKind> events,
        RecordingHttpClientFactory http
    )
    {
        return new(
            factory,
            HostBotAccountTokenProtectionTestSupport.CreateProtector(),
            new OAuthTransport(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(new BotOptions()),
            new HostedChannelChangeNotifier(events)
        );
    }

    private static async Task<(int HostId, int OtherHostId)> SeedTwoAuthorizationsAsync(
        SqliteBlokeBotDbFactory factory
    )
    {
        var hostId = await SeedHostAsync(factory, "host", includeAuthorization: true);
        var otherHostId = await SeedHostAsync(factory, "other", includeAuthorization: true);
        return (hostId, otherHostId);
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory factory,
        string login,
        bool includeAuthorization
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            EnabledFeatures = HostFeatureFlags.All,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        if (includeAuthorization)
        {
            db.HostBroadcasterAuthorizations.Add(
                new HostBroadcasterAuthorization
                {
                    HostId = host.Id,
                    TwitchUserId = host.TwitchUserId,
                    Login = host.Login,
                    ProtectedTokenPayload = [1, 2, 3],
                    AuthorizedScopes = string.Join(
                        ' ',
                        HostBroadcasterAuthorizationService.MilestoneScopes
                    ),
                    AuthorizedAtUtc = DateTime.UtcNow.AddDays(-30),
                    UpdatedAtUtc = DateTime.UtcNow.AddDays(-30),
                }
            );
            await db.SaveChangesAsync();
        }

        return host.Id;
    }

    private static async Task AssertAuthorizationMissingAsync(
        SqliteBlokeBotDbFactory factory,
        int hostId
    )
    {
        await using var verify = await factory.CreateDbContextAsync();
        (
            await verify.HostBroadcasterAuthorizations.AnyAsync(value => value.HostId == hostId)
        ).ShouldBeFalse();
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public int RequestCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this));
        }

        private sealed class Handler(RecordingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
        }
    }
}

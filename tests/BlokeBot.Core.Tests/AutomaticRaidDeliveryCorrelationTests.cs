using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidDeliveryCorrelationTests : PublicChatOutboxIntegrationTestBase
{
    [Test]
    public async Task TerminalDelivery_AlertSaveFails_RollsBackOutcomeAndPublishesNothing()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            new FailDurableAlertSaveInterceptor()
        );
        await SeedTwoHostsAndOutcomesAsync(database);
        var now = Utc(12, 0, 0);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.AutomaticRaidDelivery.AlertRollback"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var outbox = new EfPublicChatOutbox(
            database,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            Alerts(database, events)
        );
        await EnqueueCorrelatedAsync(
            outbox,
            now,
            new PublicChatDeliveryDeadline.ConfiguredMaximum()
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        _ = (
            await outbox.BeginSendAsync(claimed, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
            outbox
                .RecordDeliveryOutcomeAsync(
                    claimed,
                    new PublicChatDeliveryOutcome.Rejection
                    {
                        Reason = new PublicChatRejectionReason.ProviderCode(
                            new PublicChatProviderRejectionCode("msg_rejected")
                        ),
                    },
                    now.AddSeconds(1),
                    CancellationToken.None
                )
                .AsTask()
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.PublicChatOutboxMessages.SingleAsync()).Status.ShouldBe(
            PublicChatOutboxStatus.Sending
        );
        var outcome = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(value =>
            value.HostId == 1
        );
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Delivered);
        (await verify.DurableAlerts.CountAsync()).ShouldBe(0);
        notificationCount.ShouldBe(0);
    }

    [Test]
    public async Task CorrelatedRateLimit_DirectRetryExhaustionRecordsRateLimitedAndOneAlert()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedTwoHostsAndOutcomesAsync(database);
        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(
            database,
            CreateRetryPolicy(
                1,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                Polly.DelayBackoffType.Constant
            ),
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            Alerts(database)
        );
        await EnqueueCorrelatedAsync(
            outbox,
            now,
            new PublicChatDeliveryDeadline.ConfiguredMaximum()
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);

        _ = (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                RateLimitedPreparationOutcome(),
                now,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        await AssertRateLimitedOutcomeAsync(database);
    }

    [Test]
    public async Task CorrelatedRateLimit_MaintenanceRetryExhaustionRecordsRateLimitedAndOneAlert()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedTwoHostsAndOutcomesAsync(database);
        var now = Utc(12, 0, 0);
        var schedulingOutbox = new EfPublicChatOutbox(
            database,
            CreateRetryPolicy(
                2,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                Polly.DelayBackoffType.Constant
            ),
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            Alerts(database)
        );
        await EnqueueCorrelatedAsync(
            schedulingOutbox,
            now,
            new PublicChatDeliveryDeadline.ConfiguredMaximum()
        );
        var claimed = await ClaimAsync(schedulingOutbox, now, TimeSpan.Zero);
        _ = (
            await schedulingOutbox.RecordDeliveryOutcomeAsync(
                claimed,
                RateLimitedPreparationOutcome(),
                now,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        var retryAt = now.AddSeconds(1);
        var maintenanceOutbox = new EfPublicChatOutbox(
            database,
            CreateRetryPolicy(
                1,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                Polly.DelayBackoffType.Constant
            ),
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            Alerts(database)
        );

        _ = (
            await maintenanceOutbox.TryClaimNextAsync(
                retryAt,
                retryAt.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();

        await AssertRateLimitedOutcomeAsync(database);
    }

    [Test]
    public async Task CorrelatedRegularRejection_UpdatesOnlyOwningHostAndCreatesOneAlert()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedTwoHostsAndOutcomesAsync(database);
        var now = Utc(12, 0, 0);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.AutomaticRaidDelivery.AlertCommit"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var outbox = new EfPublicChatOutbox(
            database,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            Alerts(database, events)
        );
        var correlation = new PublicChatDeliveryCorrelation(1, "same-provider-message");
        var accepted = await outbox.EnqueueAsync(
            new PublicChatOutboxBatch
            {
                Channel = "first",
                EnqueuedAt = now,
                Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                Items =
                [
                    new PublicChatOutboxItem
                    {
                        Message = "one automatic shoutout",
                        DeduplicationKey = PublicChatMessageDeduplication.CorrelatedKey(
                            correlation
                        ),
                    },
                ],
            },
            CancellationToken.None
        );
        _ = accepted.ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>();
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        _ = (
            await outbox.BeginSendAsync(claimed, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        _ = (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Rejection
                {
                    Reason = new PublicChatRejectionReason.ProviderCode(
                        new PublicChatProviderRejectionCode("msg_rejected")
                    ),
                },
                now.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        await using var verify = await database.CreateDbContextAsync();
        var first = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(static outcome =>
            outcome.HostId == 1
        );
        first.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        first.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
        var second = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(static outcome =>
            outcome.HostId == 2
        );
        second.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Delivered);
        second.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Delivered);
        var alert = await verify.DurableAlerts.SingleAsync();
        alert.HostId.ShouldBe(1);
        alert.SourceKey.ShouldBe("same-provider-message");
        notificationCount.ShouldBe(1);
    }

    [Test]
    public async Task PinTerminalAfterMessageDelivery_RecordsPartialFailureOnceWithoutRetry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAutomaticPinAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.AutomaticRaidPin.AlertCommit"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var store = new EfPublicChatPinStore(
            database,
            new ManualTestTimeProvider(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero)),
            Alerts(database, events)
        );
        var item = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();

        await store.CompleteAsync(
            item,
            new PublicChatPinExecutionOutcome.Terminal("permission-denied"),
            CancellationToken.None
        );
        await store.CompleteAsync(
            item,
            new PublicChatPinExecutionOutcome.Terminal("permission-denied"),
            CancellationToken.None
        );

        await using var verify = await database.CreateDbContextAsync();
        var outcome = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.PartialFailure);
        var operation = await verify.PublicChatPinOperations.SingleAsync();
        operation.Status.ShouldBe(PublicChatPinOperationStatus.Terminal);
        operation.Outcome.ShouldBe("permission-denied");
        (await verify.DurableAlerts.CountAsync()).ShouldBe(1);
        (await verify.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
        notificationCount.ShouldBe(1);
    }

    [Test]
    public async Task CorrelatedPendingExpiry_RecordsNotReadyWithoutSending()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedTwoHostsAndOutcomesAsync(database);
        var now = Utc(12, 0, 0);
        var outbox = Outbox(database);
        await EnqueueCorrelatedAsync(
            outbox,
            now,
            new PublicChatDeliveryDeadline.ProducerAbsolute(now.AddSeconds(1))
        );

        _ = await outbox.TryClaimNextAsync(
            now.AddSeconds(2),
            now.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        await using var verify = await database.CreateDbContextAsync();
        var outcome = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(static value =>
            value.HostId == 1
        );
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.NotReady);
    }

    [Test]
    public async Task CorrelatedSendingLeaseRecovery_RecordsAmbiguousWithoutResend()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedTwoHostsAndOutcomesAsync(database);
        var now = Utc(12, 0, 0);
        var outbox = Outbox(database);
        await EnqueueCorrelatedAsync(
            outbox,
            now,
            new PublicChatDeliveryDeadline.ConfiguredMaximum()
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        _ = (
            await outbox.BeginSendAsync(claimed, now, now.AddSeconds(1), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        _ = await outbox.TryClaimNextAsync(
            now.AddSeconds(2),
            now.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        await using var verify = await database.CreateDbContextAsync();
        var outcome = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(static value =>
            value.HostId == 1
        );
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Ambiguous);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Ambiguous);
        (await verify.PublicChatOutboxMessages.SingleAsync()).Status.ShouldBe(
            PublicChatOutboxStatus.Ambiguous
        );
    }

    private static EfPublicChatOutbox Outbox(SqliteBlokeBotDbFactory database) =>
        new(
            database,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            Alerts(database)
        );

    private static DurableAlertService Alerts(
        SqliteBlokeBotDbFactory database,
        EventBus<AppEventKind>? events = null
    ) => new(database, TimeProvider.System, events ?? TestEventBus.Create<AppEventKind>());

    private static PublicChatDeliveryOutcome RateLimitedPreparationOutcome() =>
        PublicChatDeliveryClassifier.MapPreparationFailure(
            PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                new HttpRequestException(
                    "rate limited",
                    null,
                    System.Net.HttpStatusCode.TooManyRequests
                ),
                CancellationToken.None
            )
        );

    private static async Task AssertRateLimitedOutcomeAsync(SqliteBlokeBotDbFactory database)
    {
        await using var verify = await database.CreateDbContextAsync();
        var first = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(static outcome =>
            outcome.HostId == 1
        );
        first.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        first.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.RateLimited);
        var second = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(static outcome =>
            outcome.HostId == 2
        );
        second.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Delivered);
        second.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Delivered);
        var message = await verify.PublicChatOutboxMessages.SingleAsync();
        message.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendExhausted);
        message.HttpStatusCode.ShouldBe(429);
        var alert = await verify.DurableAlerts.SingleAsync();
        alert.HostId.ShouldBe(1);
        alert.SourceKey.ShouldBe("same-provider-message");
        alert.Message.ShouldContain(nameof(AutomaticRaidShoutoutResultCode.RateLimited));
    }

    private static async Task EnqueueCorrelatedAsync(
        EfPublicChatOutbox outbox,
        DateTimeOffset now,
        PublicChatDeliveryDeadline deadline
    ) =>
        (
            await outbox.EnqueueAsync(
                new PublicChatOutboxBatch
                {
                    Channel = "first",
                    EnqueuedAt = now,
                    Deadline = deadline,
                    Items =
                    [
                        new PublicChatOutboxItem
                        {
                            Message = "one automatic shoutout",
                            DeduplicationKey = PublicChatMessageDeduplication.CorrelatedKey(
                                new PublicChatDeliveryCorrelation(1, "same-provider-message")
                            ),
                        },
                    ],
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>();

    private static async Task SeedTwoHostsAndOutcomesAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        db.Hosts.AddRange(
            new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Id = 1,
                Login = "first",
                DisplayName = "First",
                TwitchUserId = "first-id",
            },
            new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Id = 2,
                Login = "second",
                DisplayName = "Second",
                TwitchUserId = "second-id",
            }
        );
        db.AutomaticRaidShoutoutOutcomes.AddRange(
            Outcome(1, "same-provider-message"),
            Outcome(2, "same-provider-message")
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task SeedAutomaticPinAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.Hosts.Add(
            new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Id = 1,
                Login = "first",
                DisplayName = "First",
                TwitchUserId = "first-id",
            }
        );
        _ = db.AutomaticRaidShoutoutOutcomes.Add(Outcome(1, "raid-message"));
        _ = db.PublicChatPinOperations.Add(
            new PublicChatPinOperation
            {
                Id = 1,
                Kind = PublicChatPinOperationKind.Pin,
                Status = PublicChatPinOperationStatus.Attempting,
                HostId = 1,
                Channel = "first",
                Feature = AutomaticRaidDeliveryCorrelation.Feature,
                ReplyKey = "raid-message",
                OwnerId = 1,
                TwitchMessageId = "twitch-message",
                DurationSeconds = 120,
                UnpinOnOwnerCompletion = false,
                CreatedAtUtc = DateTime.UtcNow,
                AttemptStartedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static AutomaticRaidShoutoutOutcome Outcome(int hostId, string providerMessageId) =>
        new()
        {
            Id = hostId,
            HostId = hostId,
            ProviderMessageId = providerMessageId,
            SourceTwitchUserId = $"raider-{hostId}",
            SourceLogin = "raider",
            SourceDisplayName = "Raider",
            ViewerCount = 10,
            Status = AutomaticRaidShoutoutOutcomeStatus.Delivered,
            ResultCode = AutomaticRaidShoutoutResultCode.Delivered,
            MessageTimestampUtc = DateTime.UtcNow,
            ClaimedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
        };
}

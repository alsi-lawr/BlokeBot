using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class DurableAlertReportSerializationTests : PublicChatOutboxIntegrationTestBase
{
    [Test]
    public async Task OutboxAndPin_FirstReportsInterleave_BothSourceTransactionsCommit()
    {
        var pause = new PauseFirstDurableAlertSaveInterceptor();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(pause);
        await SeedSourcesAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var outbox = new EfPublicChatOutbox(
            database,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            alerts
        );
        var enqueuedAt = Utc(12, 0, 0);
        _ = (
            await outbox.EnqueueAsync(
                new PublicChatOutboxBatch
                {
                    Channel = "streamer",
                    EnqueuedAt = enqueuedAt,
                    Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                    Items =
                    [
                        new PublicChatOutboxItem
                        {
                            Message = "automatic raid shoutout",
                            DeduplicationKey = PublicChatMessageDeduplication.CorrelatedKey(
                                new PublicChatDeliveryCorrelation(1, "outbox-raid")
                            ),
                        },
                    ],
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>();
        var claimed = await ClaimAsync(outbox, enqueuedAt, TimeSpan.Zero);
        _ = (
            await outbox.BeginSendAsync(
                claimed,
                enqueuedAt,
                enqueuedAt.AddMinutes(5),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        var pinStore = new EfPublicChatPinStore(
            database,
            new ManualTestTimeProvider(enqueuedAt.AddMinutes(2)),
            alerts
        );
        var pin = (await pinStore.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();

        var outboxCompletion = Task.Run(async () =>
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Rejection
                {
                    Reason = new PublicChatRejectionReason.ProviderCode(
                        new PublicChatProviderRejectionCode("msg_rejected")
                    ),
                },
                enqueuedAt.AddMinutes(1),
                CancellationToken.None
            )
        );
        await pause.WaitUntilPausedAsync();
        var pinCompletion = pinStore
            .CompleteAsync(
                pin,
                new PublicChatPinExecutionOutcome.Terminal("permission-denied"),
                CancellationToken.None
            )
            .AsTask();
        var pinWaitedForOutboxTransaction = !pinCompletion.IsCompleted;
        pause.Release();

        _ = (await outboxCompletion).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        await pinCompletion;
        pinWaitedForOutboxTransaction.ShouldBeTrue();

        await using var verify = await database.CreateDbContextAsync();
        (await verify.PublicChatOutboxMessages.SingleAsync()).Status.ShouldBe(
            PublicChatOutboxStatus.Rejected
        );
        (await verify.PublicChatPinOperations.SingleAsync()).Status.ShouldBe(
            PublicChatPinOperationStatus.Terminal
        );
        var outcomes = await verify
            .AutomaticRaidShoutoutOutcomes.OrderBy(outcome => outcome.ProviderMessageId)
            .ToArrayAsync();
        outcomes[0].ProviderMessageId.ShouldBe("outbox-raid");
        outcomes[0].Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcomes[0].ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
        outcomes[1].ProviderMessageId.ShouldBe("pin-raid");
        outcomes[1].Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcomes[1].ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.PartialFailure);
        var alertsByKey = (await verify.DurableAlerts.ToArrayAsync()).ToDictionary(alert =>
            alert.SourceKey
        );
        alertsByKey.Count.ShouldBe(2);
        alertsByKey["outbox-raid"].OccurrenceCount.ShouldBe(1);
        alertsByKey["outbox-raid"].Title.ShouldBe("Automatic raid shoutout was not delivered");
        alertsByKey["pin-raid"].OccurrenceCount.ShouldBe(1);
        alertsByKey["pin-raid"].Title.ShouldBe("Automatic raid shoutout pin failed");
        alertsByKey["pin-raid"].Message.ShouldContain("permission-denied");
    }

    private static async Task SeedSourcesAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.Hosts.Add(
            new BotHost
            {
                Id = 1,
                EnabledFeatures = HostFeatureFlags.All,
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
            }
        );
        db.AutomaticRaidShoutoutOutcomes.AddRange(
            new AutomaticRaidShoutoutOutcome
            {
                Id = 1,
                HostId = 1,
                ProviderMessageId = "outbox-raid",
                SourceTwitchUserId = "raider-id",
                SourceLogin = "raider",
                SourceDisplayName = "Raider",
                ViewerCount = 10,
                Status = AutomaticRaidShoutoutOutcomeStatus.Queued,
                ResultCode = AutomaticRaidShoutoutResultCode.Queued,
                MessageTimestampUtc = Utc(11, 59, 0).UtcDateTime,
                ClaimedAtUtc = Utc(11, 59, 0).UtcDateTime,
            },
            new AutomaticRaidShoutoutOutcome
            {
                Id = 2,
                HostId = 1,
                ProviderMessageId = "pin-raid",
                SourceTwitchUserId = "pin-raider-id",
                SourceLogin = "pin-raider",
                SourceDisplayName = "Pin raider",
                ViewerCount = 10,
                Status = AutomaticRaidShoutoutOutcomeStatus.Delivered,
                ResultCode = AutomaticRaidShoutoutResultCode.Delivered,
                MessageTimestampUtc = Utc(11, 59, 0).UtcDateTime,
                ClaimedAtUtc = Utc(11, 59, 0).UtcDateTime,
                CompletedAtUtc = Utc(11, 59, 30).UtcDateTime,
            }
        );
        _ = db.PublicChatPinOperations.Add(
            new PublicChatPinOperation
            {
                Id = 1,
                Kind = PublicChatPinOperationKind.Pin,
                Status = PublicChatPinOperationStatus.Attempting,
                HostId = 1,
                Channel = "streamer",
                Feature = AutomaticRaidDeliveryCorrelation.Feature,
                ReplyKey = "pin-raid",
                OwnerId = 2,
                TwitchMessageId = "twitch-message",
                DurationSeconds = 120,
                UnpinOnOwnerCompletion = false,
                CreatedAtUtc = Utc(11, 59, 0).UtcDateTime,
                AttemptStartedAtUtc = Utc(11, 59, 30).UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private sealed class PauseFirstDurableAlertSaveInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _pauseClaimed;

        internal Task WaitUntilPausedAsync() => _paused.Task;

        internal void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                eventData
                    .Context?.ChangeTracker.Entries<DurableAlert>()
                    .Any(entry => entry.State == EntityState.Added) == true
                && Interlocked.Exchange(ref _pauseClaimed, 1) == 0
            )
            {
                _ = _paused.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}

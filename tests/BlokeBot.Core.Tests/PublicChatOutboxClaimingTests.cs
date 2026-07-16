using System.Collections.Immutable;
using System.Diagnostics;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Shouldly;
using TUnit.Core;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class PublicChatOutboxClaimingTests : PublicChatOutboxIntegrationTestBase
{
    [Test]
    public async Task SuccessfulSend_IdleWorkerPurgesReceiptExactlyAtOperationalExpiry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var outbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var queue = CreateQueue(
            outbox,
            new RecordingPublicChatTransport(),
            clock,
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 5,
                DuplicateChatMessageCooldownSeconds = 3,
            }
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        _ = (await outbox.ReadClaimOutcomeAsync()).ShouldBeOfType<PublicChatClaimOutcome.Empty>();

        _ = await queue.EnqueueAsync(
            Command("streamer", "receipt expires while idle"),
            CancellationToken.None
        );
        _ = (await outbox.ReadClaimOutcomeAsync()).ShouldBeOfType<PublicChatClaimOutcome.Claimed>();
        _ = await outbox.ReadOutcomeAsync();
        var awaitingExpiry = (
            await outbox.ReadClaimOutcomeAsync()
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        awaitingExpiry.AvailableAt.ShouldBe(clock.GetUtcNow().AddSeconds(5));
        _ = await clock.WaitForTimerRegistrationAsync();

        clock.Advance(TimeSpan.FromSeconds(4));
        await using (var beforeExpiry = await dbFactory.CreateDbContextAsync())
        {
            (await beforeExpiry.PublicChatSendReceipts.CountAsync()).ShouldBe(1);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        _ = (await outbox.ReadClaimOutcomeAsync()).ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using (var atExpiry = await dbFactory.CreateDbContextAsync())
        {
            (await atExpiry.PublicChatSendReceipts.CountAsync()).ShouldBe(0);
        }

        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task CompletedSendReceipts_MoreThanOneBatch_PurgeInBoundedPasses()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            seed.PublicChatSendReceipts.AddRange(
                Enumerable
                    .Range(1, 101)
                    .Select(index => new PublicChatSendReceipt
                    {
                        OutboxMessageId = index,
                        AttemptedAtUtc = now.AddSeconds(-6).UtcDateTime,
                        CompletedAtUtc = now.AddSeconds(-5).UtcDateTime,
                    })
            );
            await seed.SaveChangesAsync();
        }
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.Zero,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>()
            .AvailableAt.ShouldBe(now);
        await using (var afterFirstBatch = await dbFactory.CreateDbContextAsync())
        {
            (await afterFirstBatch.PublicChatSendReceipts.CountAsync()).ShouldBe(1);
        }

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var afterSecondBatch = await dbFactory.CreateDbContextAsync();
        (await afterSecondBatch.PublicChatSendReceipts.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task MessagesWithSameCreationTime_Processing_PreservesIdentityOrder()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var outbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(
            outbox,
            transport,
            clock,
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
            }
        );
        _ = await queue.EnqueueAsync(Command("streamer", "first"), CancellationToken.None);
        _ = await queue.EnqueueAsync(Command("streamer", "second"), CancellationToken.None);
        _ = await queue.EnqueueAsync(Command("streamer", "third"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        var delivered = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            delivered.Add((await transport.ReadAsync()).Message);
            _ = await outbox.ReadDeliveryAsync();
        }

        await StopAsync(stopping, worker);
        delivered.ShouldBe(["first", "second", "third"]);
    }

    [Test]
    public async Task PreviousCompletion_ClaimingNext_AppliesGlobalSendInterval()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "first", "second"),
            CancellationToken.None
        );
        var first = await ClaimAsync(outbox, now, TimeSpan.FromSeconds(10));
        (
            await outbox.BeginSendAsync(first, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (
            await outbox.RecordDeliveryOutcomeAsync(
                first,
                new PublicChatDeliveryOutcome.Sent(),
                now.AddSeconds(2),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        var waiting = (
            await outbox.TryClaimNextAsync(
                now.AddSeconds(11),
                now.AddMinutes(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        waiting.AvailableAt.ShouldBe(now.AddSeconds(12));

        var second = await ClaimAsync(outbox, now.AddSeconds(12), TimeSpan.FromSeconds(10));
        second.Message.ShouldBe("second");
    }

    [Test]
    public async Task DuplicateAndDistinctMessages_Claiming_DelaysOnlyDuplicateFromCompletion()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "same", "same", "different"),
            CancellationToken.None
        );
        var first = await ClaimAsync(outbox, now, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        first.Message.ShouldBe("same");
        await BeginAndDeliverAsync(outbox, first, now, now.AddSeconds(2));

        var distinct = await ClaimAsync(
            outbox,
            now.AddSeconds(2),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10)
        );
        distinct.Message.ShouldBe("different");
        await BeginAndDeliverAsync(outbox, distinct, now.AddSeconds(2), now.AddSeconds(3));

        var waiting = (
            await outbox.TryClaimNextAsync(
                now.AddSeconds(11),
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(10),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        waiting.AvailableAt.ShouldBe(now.AddSeconds(12));
        var duplicate = await ClaimAsync(
            outbox,
            now.AddSeconds(12),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10)
        );
        duplicate.Message.ShouldBe("same");
    }

    [Test]
    public async Task ConcurrentStores_ClaimingPendingMessage_GrantOneGlobalClaim()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstStore = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var secondStore = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var now = Utc(12, 0, 0);
        _ = await firstStore.EnqueueAsync(
            Batch("streamer", now, "only once"),
            CancellationToken.None
        );
        await using var firstContext = await dbFactory.CreateDbContextAsync();
        await using var secondContext = await dbFactory.CreateDbContextAsync();
        firstContext.ShouldNotBeSameAs(secondContext);
        firstContext
            .Database.GetDbConnection()
            .ShouldNotBeSameAs(secondContext.Database.GetDbConnection());

        var claims = await Task.WhenAll(
            firstStore
                .TryClaimNextAsync(
                    now,
                    now.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            secondStore
                .TryClaimNextAsync(
                    now,
                    now.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask()
        );

        claims.OfType<PublicChatClaimOutcome.Claimed>().ShouldHaveSingleItem();
        claims
            .Count(outcome =>
                outcome
                    is PublicChatClaimOutcome.AwaitingAvailability
                        or PublicChatClaimOutcome.Contended
            )
            .ShouldBe(1);
        await using var verification = await dbFactory.CreateDbContextAsync();
        var claimed = await verification.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        claimed.Status.ShouldBe(PublicChatOutboxStatus.Claimed);
        claimed.ClaimSlot.ShouldBe(1);
        claimed.ClaimToken.ShouldNotBeNull();
    }

    [Test]
    public async Task WorkerCanceledBeforeSend_Restarting_ReleasesAndDeliversPendingMessage()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var persistedOutbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var blockingOutbox = new BlockingBeginSendPublicChatOutbox(persistedOutbox);
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(blockingOutbox, transport, clock);
        _ = await queue.EnqueueAsync(Command("streamer", "recover me"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await blockingOutbox.ReadBeginAttemptAsync();
        await StopAsync(stopping, worker);

        transport.DeliveryCount.ShouldBe(0);
        await using (var interruptedDb = await dbFactory.CreateDbContextAsync())
        {
            var interrupted = await interruptedDb
                .PublicChatOutboxMessages.AsNoTracking()
                .SingleAsync();
            interrupted.Status.ShouldBe(PublicChatOutboxStatus.Pending);
            interrupted.AttemptCount.ShouldBe(0);
            interrupted.ClaimToken.ShouldBeNull();
            interrupted.ClaimSlot.ShouldBeNull();
        }

        var restartedOutbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var restartedTransport = new RecordingPublicChatTransport();
        var restartedQueue = CreateQueue(restartedOutbox, restartedTransport, clock);
        using var restartedStopping = new CancellationTokenSource();
        var restartedWorker = restartedQueue.RunAsync(restartedStopping.Token);
        var delivery = await restartedTransport.ReadAsync();
        _ = await restartedOutbox.ReadDeliveryAsync();
        await StopAsync(restartedStopping, restartedWorker);

        delivery.Message.ShouldBe("recover me");
        delivery.Attempt.ShouldBe(1);
        await using var completedDb = await dbFactory.CreateDbContextAsync();
        (await completedDb.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
        (
            await completedDb.PublicChatSendReceipts.AsNoTracking().ToArrayAsync()
        ).ShouldHaveSingleItem();
    }

    [Test]
    public async Task ClaimedLeaseExpired_AfterRestart_IsReclaimedWithoutStartingAttempt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "safe to reclaim"),
            CancellationToken.None
        );
        var original = (
            await outbox.TryClaimNextAsync(
                now,
                now.AddSeconds(1),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PublicChatClaimOutcome.Claimed>()
            .Message;

        var reclaimed = await ClaimAsync(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            now.AddSeconds(2),
            TimeSpan.Zero
        );

        reclaimed.Id.ShouldBe(original.Id);
        reclaimed.Message.ShouldBe("safe to reclaim");
        reclaimed.Attempt.ShouldBe(1);
        reclaimed.ClaimToken.ShouldNotBe(original.ClaimToken);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Claimed);
        row.AttemptCount.ShouldBe(0);
        row.SendStartedAtUtc.ShouldBeNull();
    }

    [Test]
    public async Task SendingClaimExpired_AfterRestart_BecomesRedactedAmbiguousWithoutRetry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "may have sent"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        (
            await outbox.BeginSendAsync(claimed, now, now.AddSeconds(1), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        var afterRestart = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            now.AddSeconds(2),
            now.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        afterRestart.ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Ambiguous);
        row.Message.ShouldBeNull();
        row.DeduplicationKey.ShouldBeNull();
        row.NextAttemptAtUtc.ShouldBeNull();
        row.AttemptCount.ShouldBe(1);
        row.SendStartedAtUtc.ShouldBe(now.UtcDateTime);
        row.CompletedAtUtc.ShouldBe(now.AddSeconds(2).UtcDateTime);
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();
        row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Send);
        row.FailureType.ShouldBe(typeof(PublicChatSendLeaseExpired).FullName);
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
    }
}

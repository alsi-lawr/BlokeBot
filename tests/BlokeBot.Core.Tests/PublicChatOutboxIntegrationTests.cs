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

public sealed class PublicChatOutboxIntegrationTests
{
    [Test]
    public async Task SplitMessage_Enqueueing_PersistsWholeBatchBeforeAcknowledgement()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var queue = CreateQueue(
            outbox,
            new RecordingPublicChatTransport(),
            new ManualTestTimeProvider(now),
            new BotOptions { MaxChatMessageLength = 10 }
        );

        var receipt = await queue.EnqueueAsync(
            Command("streamer", "alpha beta gamma"),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync();
        rows.Select(row => row.Id)
            .ShouldBe(
                receipt.ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>().Receipt.MessageIds
            );
        rows.Select(row => row.Message).ShouldBe(["alpha", "beta gamma"]);
        rows.Select(row => row.Status)
            .ShouldAllBe(status => status == PublicChatOutboxStatus.Pending);
        rows.Select(row => row.CreatedAtUtc).ShouldAllBe(value => value == now.UtcDateTime);
        rows.Select(row => row.NextAttemptAtUtc).ShouldAllBe(value => value == now.UtcDateTime);
        rows.Select(row => row.AttemptCount).ShouldAllBe(attempts => attempts == 0);
        rows.Select(row => row.ClaimToken).ShouldAllBe(token => token == null);
    }

    [Test]
    public async Task PendingMessage_RestartingQueue_DeliversOnceAndDeletesClaimedRow()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var originalQueue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            new RecordingPublicChatTransport(),
            clock
        );
        var receipt = await originalQueue.EnqueueAsync(
            Command("streamer", "survives restart"),
            CancellationToken.None
        );

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
        using var stopping = new CancellationTokenSource();
        var worker = restartedQueue.RunAsync(stopping.Token);

        var delivery = await restartedTransport.ReadAsync();
        var completion = await restartedOutbox.ReadDeliveryAsync();
        await StopAsync(stopping, worker);

        delivery.Id.ShouldBe(
            receipt
                .ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>()
                .Receipt.MessageIds.ShouldHaveSingleItem()
        );
        delivery.Message.ShouldBe("survives restart");
        delivery.Attempt.ShouldBe(1);
        completion.Id.ShouldBe(delivery.Id);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
        var sendReceipt = await db.PublicChatSendReceipts.AsNoTracking().SingleAsync();
        sendReceipt.OutboxMessageId.ShouldBe(delivery.Id);
        sendReceipt.DeliveredAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
        sendReceipt.DeliveredDeduplicationKey.ShouldBe(
            PublicChatMessageDeduplication.Key("streamer", "survives restart").Value
        );

        var next = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        next.ShouldBeOfType<PublicChatClaimOutcome.Empty>();
    }

    [Test]
    public async Task TokenUnavailable_PreparingMessage_PersistsTerminalRedactedOutcomeWithoutRetry()
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
        var transport = new ScriptedPublicChatTransport(
            (_, _) =>
                ValueTask.FromResult<PublicChatPreparationOutcome>(
                    new PublicChatPreparationOutcome.TokenUnavailable(
                        AccessTokenUnavailableReason.MissingRefreshToken
                    )
                ),
            (_, _) =>
                ValueTask.FromException<PublicChatTransportSendResult>(
                    new InvalidOperationException("A token-unavailable message must not be sent.")
                )
        );
        var logger = new RecordingPublicChatLogger<PublicChatMessageQueue>();
        var queue = CreateQueue(outbox, transport, clock, logger: logger);
        _ = await queue.EnqueueAsync(
            Command("streamer", "private message payload"),
            CancellationToken.None
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = (await outbox.ReadClaimOutcomeAsync()).ShouldBeOfType<PublicChatClaimOutcome.Claimed>();
        var outcome = (
            await outbox.ReadOutcomeAsync()
        ).ShouldBeOfType<PublicChatDeliveryOutcome.TokenUnavailable>();
        var nextClaim = await outbox.ReadClaimOutcomeAsync();
        await StopAsync(stopping, worker);

        outcome.Reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        nextClaim.ShouldNotBeOfType<PublicChatClaimOutcome.Claimed>();
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(0);
        var diagnostic = logger.Entries.ShouldHaveSingleItem();
        diagnostic.Level.ShouldBe(LogLevel.Warning);
        diagnostic.Exception.ShouldBeNull();
        diagnostic
            .Properties["UnavailableReason"]
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        diagnostic.Properties.ContainsKey("FailureType").ShouldBeFalse();
        diagnostic.Message.ShouldNotContain("private message payload");
        await using var db = await dbFactory.CreateDbContextAsync();
        var persisted = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        persisted.Status.ShouldBe(PublicChatOutboxStatus.Unexpected);
        persisted.Message.ShouldBeNull();
        persisted.DeduplicationKey.ShouldBeNull();
        persisted.AttemptCount.ShouldBe(0);
        persisted.SafePreSendFailureCount.ShouldBe(0);
        persisted.NextAttemptAtUtc.ShouldBeNull();
        persisted.ClaimToken.ShouldBeNull();
        persisted.CompletedAtUtc.ShouldNotBeNull();
        persisted.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Preparation);
        persisted.FailureType.ShouldBe(nameof(AccessTokenUnavailableReason.MissingRefreshToken));
        persisted.HttpStatusCode.ShouldBeNull();
        (await db.PublicChatSendReceipts.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task EnqueuedMessage_Persisting_UsesCreatedTimeAndRequiredLifetimeOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(17)),
            StandardRetentionPolicy
        );

        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "short lived"),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.CreatedAtUtc.ShouldBe(now.UtcDateTime);
        row.ExpiresAtUtc.ShouldBe(now.AddSeconds(17).UtcDateTime);
    }

    [Test]
    public async Task ProducerDeadline_Persisting_UsesEarlierAbsoluteBoundaryWithoutRestartingAge()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 25);
        var occurrenceDueAt = Utc(12, 0, 0);
        var producerExpiry = occurrenceDueAt.AddSeconds(30);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );

        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "aged occurrence") with
            {
                Deadline = new PublicChatDeliveryDeadline.ProducerAbsolute(producerExpiry),
            },
            CancellationToken.None
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "configured cap") with
            {
                Deadline = new PublicChatDeliveryDeadline.ProducerAbsolute(now.AddMinutes(1)),
            },
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToArrayAsync();
        rows[0].CreatedAtUtc.ShouldBe(now.UtcDateTime);
        rows[0].ExpiresAtUtc.ShouldBe(producerExpiry.UtcDateTime);
        rows[1].ExpiresAtUtc.ShouldBe(now.AddSeconds(30).UtcDateTime);
    }

    [Test]
    public async Task ClaimedMessage_BeginningAtExactExpiry_RedactsWithoutSendReceipt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var expiry = now.AddSeconds(5);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "secret stale payload"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);

        (
            await outbox.BeginSendAsync(
                claimed,
                expiry,
                expiry.AddMinutes(5),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Expired>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        AssertExpired(row, expiry);
        (await db.PublicChatSendReceipts.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task PendingMessage_ClaimingAfterExpiry_RedactsAtObservedTime()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var observedAt = now.AddSeconds(5).AddTicks(1);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "already stale"),
            CancellationToken.None
        );

        (
            await outbox.TryClaimNextAsync(
                observedAt,
                observedAt.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldNotBeOfType<PublicChatClaimOutcome.Claimed>();

        await using var db = await dbFactory.CreateDbContextAsync();
        AssertExpired(await db.PublicChatOutboxMessages.SingleAsync(), observedAt);
    }

    [Test]
    public async Task ClaimedMessage_BeginningImmediatelyBeforeExpiry_PreservesSendOutcome()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var expiry = now.AddSeconds(5);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "begins in time"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);

        (
            await outbox.BeginSendAsync(
                claimed,
                expiry.AddTicks(-1),
                expiry.AddMinutes(5),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        _ = await outbox.TryClaimNextAsync(
            expiry,
            expiry.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Sent(),
                expiry.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
        (await db.PublicChatSendReceipts.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task SafeRetryBeyondExpiry_Scheduling_UsesExpiryAndThenRedacts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var expiry = now.AddSeconds(5);
        var retryPolicy = CreateRetryPolicy(
            3,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            DelayBackoffType.Constant
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            retryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "retry expires"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);

        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                SafePreSendTransientOutcome(),
                now,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        await using (var scheduledDb = await dbFactory.CreateDbContextAsync())
        {
            var scheduled = await scheduledDb.PublicChatOutboxMessages.SingleAsync();
            scheduled.NextAttemptAtUtc.ShouldBe(expiry.UtcDateTime);
        }

        (
            await outbox.TryClaimNextAsync(
                expiry.AddTicks(-1),
                expiry.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>()
            .AvailableAt.ShouldBe(expiry);
        _ = await outbox.TryClaimNextAsync(
            expiry,
            expiry.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        AssertExpired(await db.PublicChatOutboxMessages.SingleAsync(), expiry);
    }

    [Test]
    public async Task CanceledClaim_ReleasedAtExpiry_BecomesTerminalInsteadOfPending()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var expiry = now.AddSeconds(5);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "cancel expires"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);

        (
            await outbox.ReleaseClaimAsync(claimed, expiry, CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Expired>();

        await using var db = await dbFactory.CreateDbContextAsync();
        AssertExpired(await db.PublicChatOutboxMessages.SingleAsync(), expiry);
    }

    [Test]
    public async Task PendingMessage_ConcurrentClaimsAtExpiry_ExpireWithoutClaim()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var expiry = now.AddSeconds(5);
        var first = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        var second = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await first.EnqueueAsync(
            Batch("streamer", now, "concurrent stale"),
            CancellationToken.None
        );

        var outcomes = await Task.WhenAll(
            first
                .TryClaimNextAsync(
                    expiry,
                    expiry.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            second
                .TryClaimNextAsync(
                    expiry,
                    expiry.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask()
        );

        foreach (var outcome in outcomes)
        {
            outcome.ShouldNotBeOfType<PublicChatClaimOutcome.Claimed>();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        AssertExpired(await db.PublicChatOutboxMessages.SingleAsync(), expiry);
    }

    [Test]
    public async Task PendingMessage_IdleWorker_WakesAtDurableExpiryWithoutTransport()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            seed.PublicChatSendReceipts.Add(
                new PublicChatSendReceipt
                {
                    OutboxMessageId = 999,
                    AttemptedAtUtc = now.UtcDateTime,
                    CompletedAtUtc = now.UtcDateTime,
                }
            );
            await seed.SaveChangesAsync();
        }
        var clock = new ManualTestTimeProvider(now);
        var persisted = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            Lifetime(TimeSpan.FromSeconds(5)),
            StandardRetentionPolicy
        );
        _ = await persisted.EnqueueAsync(
            Batch("streamer", now, "idle stale"),
            CancellationToken.None
        );
        var observed = new CompletionObservingPublicChatOutbox(persisted);
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(
            observed,
            transport,
            clock,
            new BotOptions { ChatMessageSendIntervalSeconds = 60 }
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await observed.ReadClaimOutcomeAsync())
            .ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>()
            .AvailableAt.ShouldBe(now.AddSeconds(5));
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        _ = await observed.ReadClaimOutcomeAsync();

        transport.DeliveryCount.ShouldBe(0);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            AssertExpired(await db.PublicChatOutboxMessages.SingleAsync(), now.AddSeconds(5));
        }

        await StopAsync(stopping, worker);
    }

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

    [Test]
    public async Task ClassifiedTerminalOutcomes_RestartingOutbox_AreRedactedAndNeverReclaimed()
    {
        TerminalScenario[] scenarios =
        [
            new()
            {
                ExpectedStatus = PublicChatOutboxStatus.MissingChannel,
                ExpectedPhase = PublicChatOutboxFailurePhase.Preparation,
                ExpectedFailureType = null,
                ExpectedRejectionCode = null,
                ExpectedInitialSendCount = 0,
                CreateTransport = () =>
                    new ScriptedPublicChatTransport(
                        static (_, _) =>
                            ValueTask.FromResult<PublicChatPreparationOutcome>(
                                new PublicChatPreparationOutcome.MissingChannel()
                            ),
                        static (_, _) =>
                            throw new InvalidOperationException(
                                "Missing channel preparation cannot send."
                            )
                    ),
            },
            new()
            {
                ExpectedStatus = PublicChatOutboxStatus.MissingBot,
                ExpectedPhase = PublicChatOutboxFailurePhase.Preparation,
                ExpectedFailureType = null,
                ExpectedRejectionCode = null,
                ExpectedInitialSendCount = 0,
                CreateTransport = () =>
                    new ScriptedPublicChatTransport(
                        static (_, _) =>
                            ValueTask.FromResult<PublicChatPreparationOutcome>(
                                new PublicChatPreparationOutcome.MissingBot()
                            ),
                        static (_, _) =>
                            throw new InvalidOperationException(
                                "Missing bot preparation cannot send."
                            )
                    ),
            },
            new()
            {
                ExpectedStatus = PublicChatOutboxStatus.Rejected,
                ExpectedPhase = PublicChatOutboxFailurePhase.Send,
                ExpectedFailureType = null,
                ExpectedRejectionCode = "followers_only",
                ExpectedInitialSendCount = 1,
                CreateTransport = () =>
                    new ScriptedPublicChatTransport(
                        Ready,
                        static (_, _) =>
                            ValueTask.FromResult<PublicChatTransportSendResult>(
                                new PublicChatTransportSendResult.Rejected
                                {
                                    Reason = new PublicChatRejectionReason.ProviderCode(
                                        new PublicChatProviderRejectionCode("followers_only")
                                    ),
                                }
                            )
                    ),
            },
            new()
            {
                ExpectedStatus = PublicChatOutboxStatus.Rejected,
                ExpectedPhase = PublicChatOutboxFailurePhase.Send,
                ExpectedFailureType = null,
                ExpectedRejectionCode = null,
                ExpectedInitialSendCount = 1,
                CreateTransport = () =>
                    new ScriptedPublicChatTransport(
                        Ready,
                        static (_, _) =>
                            ValueTask.FromResult<PublicChatTransportSendResult>(
                                new PublicChatTransportSendResult.Rejected
                                {
                                    Reason = new PublicChatRejectionReason.Unspecified(),
                                }
                            )
                    ),
            },
            new()
            {
                ExpectedStatus = PublicChatOutboxStatus.Ambiguous,
                ExpectedPhase = PublicChatOutboxFailurePhase.Send,
                ExpectedFailureType = typeof(IOException).FullName,
                ExpectedRejectionCode = null,
                ExpectedInitialSendCount = 1,
                CreateTransport = () =>
                    new ScriptedPublicChatTransport(
                        Ready,
                        static (_, _) =>
                            ValueTask.FromException<PublicChatTransportSendResult>(
                                new IOException("secret provider response")
                            )
                    ),
            },
            new()
            {
                ExpectedStatus = PublicChatOutboxStatus.Unexpected,
                ExpectedPhase = PublicChatOutboxFailurePhase.Preparation,
                ExpectedFailureType = typeof(InvalidOperationException).FullName,
                ExpectedRejectionCode = null,
                ExpectedInitialSendCount = 0,
                CreateTransport = () =>
                    new ScriptedPublicChatTransport(
                        static (_, cancellationToken) =>
                            ValueTask.FromResult(
                                PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                                    new InvalidOperationException("secret provider response"),
                                    cancellationToken
                                )
                            ),
                        static (_, _) =>
                            throw new InvalidOperationException(
                                "Unexpected preparation cannot send."
                            )
                    ),
            },
        ];

        foreach (var scenario in scenarios)
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
            var transport = scenario.CreateTransport();
            var queue = CreateQueue(outbox, transport, clock);
            _ = await queue.EnqueueAsync(
                Command("streamer", "secret chat payload"),
                CancellationToken.None
            );
            using var stopping = new CancellationTokenSource();
            var worker = queue.RunAsync(stopping.Token);

            _ = await outbox.ReadOutcomeAsync();
            await StopAsync(stopping, worker);
            transport.PrepareCount.ShouldBe(1);
            transport.SendCount.ShouldBe(scenario.ExpectedInitialSendCount);

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
                row.Status.ShouldBe(scenario.ExpectedStatus);
                row.Message.ShouldBeNull();
                row.DeduplicationKey.ShouldBeNull();
                row.NextAttemptAtUtc.ShouldBeNull();
                row.FailurePhase.ShouldBe(scenario.ExpectedPhase);
                row.FailureType.ShouldBe(scenario.ExpectedFailureType);
                row.RejectionCode.ShouldBe(scenario.ExpectedRejectionCode);
                row.HttpStatusCode.ShouldBeNull();
                row.ClaimToken.ShouldBeNull();
                row.ClaimSlot.ShouldBeNull();
                row.CompletedAtUtc.ShouldNotBeNull();
                string.Join("|", row.FailureType, row.RejectionCode, row.Status, row.FailurePhase)
                    .ShouldNotContain("secret");
            }

            var afterRestart = await new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ).TryClaimNextAsync(
                clock.GetUtcNow(),
                clock.GetUtcNow().AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            );
            afterRestart.ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();

            var restartedTransport = new RecordingPublicChatTransport();
            var restartedQueue = CreateQueue(
                new EfPublicChatOutbox(
                    dbFactory,
                    StandardRetryPolicy,
                    StandardLifetimePolicy,
                    StandardRetentionPolicy
                ),
                restartedTransport,
                clock,
                new BotOptions
                {
                    ChatMessageSendIntervalSeconds = 0,
                    DuplicateChatMessageCooldownSeconds = 0,
                }
            );
            using var restartedStopping = new CancellationTokenSource();
            var restartedWorker = restartedQueue.RunAsync(restartedStopping.Token);
            _ = await restartedQueue.EnqueueAsync(
                Command("streamer", "new delivery after terminal"),
                CancellationToken.None
            );
            var replacement = await restartedTransport.ReadAsync();
            await StopAsync(restartedStopping, restartedWorker);

            replacement.Message.ShouldBe("new delivery after terminal");
            restartedTransport.DeliveryCount.ShouldBe(1);
        }
    }

    [Test]
    public async Task SafePreparationFailure_PersistingClassification_SchedulesRetryFromPersistedFailure()
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
        var transport = new ScriptedPublicChatTransport(
            static (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        new HttpRequestException(
                            "secret provider response",
                            null,
                            System.Net.HttpStatusCode.ServiceUnavailable
                        ),
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("A safe preparation failure cannot send.")
        );
        var queue = CreateQueue(outbox, transport, clock);
        _ = await queue.EnqueueAsync(
            Command("streamer", "retained payload"),
            CancellationToken.None
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await outbox.ReadOutcomeAsync();
        await StopAsync(stopping, worker);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendTransient);
            row.Message.ShouldBe("retained payload");
            row.AttemptCount.ShouldBe(0);
            row.SafePreSendFailureCount.ShouldBe(1);
            row.NextAttemptAtUtc.ShouldBe(clock.GetUtcNow().AddSeconds(1).UtcDateTime);
            row.SendStartedAtUtc.ShouldBeNull();
            row.CompletedAtUtc.ShouldBeNull();
            row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Preparation);
            row.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
            row.HttpStatusCode.ShouldBe(503);
            row.RejectionCode.ShouldBeNull();
        }

        var beforeRetry = (
            await new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ).TryClaimNextAsync(
                clock.GetUtcNow(),
                clock.GetUtcNow().AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        beforeRetry.AvailableAt.ShouldBe(clock.GetUtcNow().AddSeconds(1));

        var retry = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            beforeRetry.AvailableAt,
            beforeRetry.AvailableAt.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        retry
            .ShouldBeOfType<PublicChatClaimOutcome.Claimed>()
            .Message.Message.ShouldBe("retained payload");
    }

    [Test]
    public async Task PersistedSafePreSendRetry_RestartingWithAttemptLimitOne_ExhaustsWithoutClaim()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var schedulingPolicy = CreateRetryPolicy(
            2,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            DelayBackoffType.Constant
        );
        var boundedPolicy = CreateRetryPolicy(
            1,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            DelayBackoffType.Constant
        );
        var now = Utc(12, 0, 0);
        var schedulingStore = new EfPublicChatOutbox(
            dbFactory,
            schedulingPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        _ = await schedulingStore.EnqueueAsync(
            Batch("streamer", now, "must not prepare again"),
            CancellationToken.None
        );
        var initial = await ClaimAsync(schedulingStore, now, TimeSpan.Zero);
        (
            await schedulingStore.RecordDeliveryOutcomeAsync(
                initial,
                SafePreSendTransientOutcome(),
                now,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        var retryAt = now.AddSeconds(1);
        var firstRestartStore = new EfPublicChatOutbox(
            dbFactory,
            boundedPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var secondRestartStore = new EfPublicChatOutbox(
            dbFactory,
            boundedPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var concurrentClaims = await Task.WhenAll(
            firstRestartStore
                .TryClaimNextAsync(
                    retryAt,
                    retryAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            secondRestartStore
                .TryClaimNextAsync(
                    retryAt,
                    retryAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask()
        );
        concurrentClaims.OfType<PublicChatClaimOutcome.Claimed>().ShouldBeEmpty();
        foreach (var outcome in concurrentClaims)
        {
            (
                outcome
                is PublicChatClaimOutcome.AwaitingAvailability
                    or PublicChatClaimOutcome.Contended
            ).ShouldBeTrue();
        }

        (
            await firstRestartStore.TryClaimNextAsync(
                retryAt,
                retryAt.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendExhausted);
        row.Message.ShouldBeNull();
        row.DeduplicationKey.ShouldBeNull();
        row.NextAttemptAtUtc.ShouldBeNull();
        row.AttemptCount.ShouldBe(0);
        row.SafePreSendFailureCount.ShouldBe(1);
        row.CompletedAtUtc.ShouldBe(retryAt.UtcDateTime);
        row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Preparation);
        row.FailureType.ShouldBe(typeof(IOException).FullName);
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();
    }

    [Test]
    public async Task SafePreparationFailure_RestartingQueue_DeliversOnceAfterConfiguredRetryTime()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var retryPolicy = CreateRetryPolicy(
            3,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            DelayBackoffType.Exponential
        );
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var initialOutbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                retryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var initialQueue = CreateQueue(
            initialOutbox,
            new ScriptedPublicChatTransport(
                static (_, cancellationToken) =>
                    ValueTask.FromResult(
                        PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                            new IOException("secret preparation detail"),
                            cancellationToken
                        )
                    ),
                static (_, _) =>
                    throw new InvalidOperationException("A safe preparation failure cannot send.")
            ),
            clock
        );
        _ = await initialQueue.EnqueueAsync(
            Command("streamer", "survives safe retry restart"),
            CancellationToken.None
        );
        using (var initialStopping = new CancellationTokenSource())
        {
            var initialWorker = initialQueue.RunAsync(initialStopping.Token);
            _ = await initialOutbox.ReadOutcomeAsync();
            await StopAsync(initialStopping, initialWorker);
        }

        var restartedOutbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                retryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var restartedTransport = new RecordingPublicChatTransport();
        var restartedQueue = CreateQueue(restartedOutbox, restartedTransport, clock);
        using var restartedStopping = new CancellationTokenSource();
        var restartedWorker = restartedQueue.RunAsync(restartedStopping.Token);

        await clock.WaitForTimerRegistrationAsync();
        restartedTransport.DeliveryCount.ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(2));
        var delivery = await restartedTransport.ReadAsync();
        _ = await restartedOutbox.ReadDeliveryAsync();
        await StopAsync(restartedStopping, restartedWorker);

        delivery.Message.ShouldBe("survives safe retry restart");
        delivery.Attempt.ShouldBe(1);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
        (await db.PublicChatSendReceipts.AsNoTracking().ToArrayAsync()).ShouldHaveSingleItem();
    }

    [Test]
    public async Task SafePreparationFailures_ExhaustingPolicy_RedactsTerminalAndCannotBeClaimed()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var retryPolicy = CreateRetryPolicy(
            2,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            DelayBackoffType.Constant
        );
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var outbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                retryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var transport = new ScriptedPublicChatTransport(
            static (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        new HttpRequestException(
                            "secret provider response",
                            null,
                            System.Net.HttpStatusCode.ServiceUnavailable
                        ),
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("A safe preparation failure cannot send.")
        );
        var queue = CreateQueue(outbox, transport, clock);
        _ = await queue.EnqueueAsync(
            Command("streamer", "redact when exhausted"),
            CancellationToken.None
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await outbox.ReadOutcomeAsync();
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        _ = await outbox.ReadOutcomeAsync();
        await StopAsync(stopping, worker);

        transport.PrepareCount.ShouldBe(2);
        transport.SendCount.ShouldBe(0);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendExhausted);
            row.Message.ShouldBeNull();
            row.DeduplicationKey.ShouldBeNull();
            row.NextAttemptAtUtc.ShouldBeNull();
            row.AttemptCount.ShouldBe(0);
            row.SafePreSendFailureCount.ShouldBe(2);
            row.CompletedAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
            row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Preparation);
            row.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
            row.HttpStatusCode.ShouldBe(503);
        }

        var afterExhaustion = await new EfPublicChatOutbox(
            dbFactory,
            retryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        afterExhaustion.ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
    }

    [Test]
    public async Task SafePreSendRetry_ConcurrentStores_GrantOneClaimWithoutResettingFailureCount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var retryPolicy = CreateRetryPolicy(
            3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            DelayBackoffType.Exponential
        );
        var firstStore = new EfPublicChatOutbox(
            dbFactory,
            retryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var secondStore = new EfPublicChatOutbox(
            dbFactory,
            retryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var now = Utc(12, 0, 0);
        _ = await firstStore.EnqueueAsync(
            Batch("streamer", now, "safe concurrent retry"),
            CancellationToken.None
        );
        var initial = await ClaimAsync(firstStore, now, TimeSpan.Zero);
        (
            await firstStore.RecordDeliveryOutcomeAsync(
                initial,
                SafePreSendTransientOutcome(),
                now,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        await using var firstContext = await dbFactory.CreateDbContextAsync();
        await using var secondContext = await dbFactory.CreateDbContextAsync();
        firstContext.ShouldNotBeSameAs(secondContext);
        firstContext
            .Database.GetDbConnection()
            .ShouldNotBeSameAs(secondContext.Database.GetDbConnection());

        var retryAt = now.AddSeconds(1);
        var claims = await Task.WhenAll(
            firstStore
                .TryClaimNextAsync(
                    retryAt,
                    retryAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            secondStore
                .TryClaimNextAsync(
                    retryAt,
                    retryAt.AddMinutes(5),
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
        var row = await verification.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Claimed);
        row.Message.ShouldBe("safe concurrent retry");
        row.AttemptCount.ShouldBe(0);
        row.SafePreSendFailureCount.ShouldBe(1);
    }

    [Test]
    public async Task SafePreSendRetry_CallerCanceledDuringPreparation_RetainsDurableRetryState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var retryPolicy = CreateRetryPolicy(
            3,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            DelayBackoffType.Exponential
        );
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var persistedOutbox = new EfPublicChatOutbox(
            dbFactory,
            retryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        _ = await persistedOutbox.EnqueueAsync(
            Batch("streamer", clock.GetUtcNow(), "retain safe retry"),
            CancellationToken.None
        );
        var initial = await ClaimAsync(persistedOutbox, clock.GetUtcNow(), TimeSpan.Zero);
        (
            await persistedOutbox.RecordDeliveryOutcomeAsync(
                initial,
                SafePreSendTransientOutcome(),
                clock.GetUtcNow(),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        clock.Advance(TimeSpan.FromSeconds(2));

        using var stopping = new CancellationTokenSource();
        var queue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                retryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            new ScriptedPublicChatTransport(
                (_, cancellationToken) =>
                {
                    stopping.Cancel();
                    return ValueTask.FromException<PublicChatPreparationOutcome>(
                        new OperationCanceledException(cancellationToken)
                    );
                },
                static (_, _) =>
                    throw new InvalidOperationException("Canceled preparation cannot send.")
            ),
            clock
        );

        await queue.RunAsync(stopping.Token);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendTransient);
            row.Message.ShouldBe("retain safe retry");
            row.AttemptCount.ShouldBe(0);
            row.SafePreSendFailureCount.ShouldBe(1);
            row.NextAttemptAtUtc.ShouldBe(Utc(12, 0, 2).UtcDateTime);
            row.ClaimToken.ShouldBeNull();
            row.ClaimSlot.ShouldBeNull();
        }

        var retry = await new EfPublicChatOutbox(
            dbFactory,
            retryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        retry.ShouldBeOfType<PublicChatClaimOutcome.Claimed>().Message.Attempt.ShouldBe(1);
    }

    [Test]
    public async Task CallerCanceledDuringPreparation_PersistingState_ReleasesPendingWithoutAttempt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var transport = new ScriptedPublicChatTransport(
            (_, cancellationToken) =>
            {
                stopping.Cancel();
                return ValueTask.FromException<PublicChatPreparationOutcome>(
                    new OperationCanceledException(cancellationToken)
                );
            },
            static (_, _) =>
                throw new InvalidOperationException("Canceled preparation cannot send.")
        );
        var queue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            transport,
            new ManualTestTimeProvider(Utc(12, 0, 0))
        );
        _ = await queue.EnqueueAsync(Command("streamer", "still pending"), CancellationToken.None);

        await queue.RunAsync(stopping.Token);

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Pending);
        row.Message.ShouldBe("still pending");
        row.AttemptCount.ShouldBe(0);
        row.SendStartedAtUtc.ShouldBeNull();
        row.CompletedAtUtc.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task CallerCanceledAfterSendBoundary_PersistingState_IsAmbiguousAndNeverReclaimed()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var transport = new ScriptedPublicChatTransport(
            Ready,
            (_, cancellationToken) =>
            {
                stopping.Cancel();
                return ValueTask.FromException<PublicChatTransportSendResult>(
                    new OperationCanceledException(cancellationToken)
                );
            }
        );
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var queue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            transport,
            clock
        );
        _ = await queue.EnqueueAsync(
            Command("streamer", "redact after boundary"),
            CancellationToken.None
        );

        await queue.RunAsync(stopping.Token);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.Ambiguous);
            row.Message.ShouldBeNull();
            row.AttemptCount.ShouldBe(1);
            row.SendStartedAtUtc.ShouldNotBeNull();
            row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Send);
            row.FailureType.ShouldBe(typeof(OperationCanceledException).FullName);
            row.RejectionCode.ShouldBeNull();
        }

        var afterRestart = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        afterRestart.ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
    }

    [Test]
    public async Task ConfirmedSend_ReceiptPersistenceFails_RollsBackClaimedRowDeletion()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "atomic success"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        (
            await outbox.BeginSendAsync(claimed, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed with
                {
                    ClaimToken = new PublicChatClaimToken(Guid.NewGuid()),
                },
                new PublicChatDeliveryOutcome.Sent(),
                now.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.OwnershipLost>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_public_chat_receipt_update
                BEFORE UPDATE ON public_chat_send_receipts
                BEGIN
                    SELECT RAISE(ABORT, 'receipt update failed');
                END;
                """
            );
        }

        await Should.ThrowAsync<SqliteException>(() =>
            outbox
                .RecordDeliveryOutcomeAsync(
                    claimed,
                    new PublicChatDeliveryOutcome.Sent(),
                    now.AddSeconds(1),
                    CancellationToken.None
                )
                .AsTask()
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.Sending);
            row.Message.ShouldBe("atomic success");
            var sendReceipt = await db.PublicChatSendReceipts.AsNoTracking().SingleAsync();
            sendReceipt.DeliveredDeduplicationKey.ShouldBeNull();
            sendReceipt.DeliveredAtUtc.ShouldBeNull();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_public_chat_receipt_update;");
        }

        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Sent(),
                now.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        await using var verification = await dbFactory.CreateDbContextAsync();
        (await verification.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task TerminalRetention_CleanupAtExactCutoff_PreservesOnlyNewerRowUntilDue()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var duration = TimeSpan.FromMinutes(10);
        await SeedTerminalRowsAsync(
            dbFactory,
            TerminalRow(PublicChatOutboxStatus.Unexpected, now - duration - TimeSpan.FromTicks(1)),
            TerminalRow(PublicChatOutboxStatus.Rejected, now - duration),
            TerminalRow(PublicChatOutboxStatus.Ambiguous, now - duration + TimeSpan.FromTicks(1))
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(duration)
        );

        var beforeFinalCutoff = await outbox.TryClaimNextAsync(
            now,
            now.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        beforeFinalCutoff
            .ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>()
            .AvailableAt.ShouldBe(now.AddTicks(1));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var retained = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            retained.Status.ShouldBe(PublicChatOutboxStatus.Ambiguous);
        }

        (
            await new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                Retention(duration)
            ).TryClaimNextAsync(
                now.AddTicks(1),
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var verification = await dbFactory.CreateDbContextAsync();
        (await verification.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ExpiredTerminal_RetentionAtExactCutoff_PurgesWithOtherTerminalRows()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var duration = TimeSpan.FromMinutes(10);
        await SeedTerminalRowsAsync(
            dbFactory,
            TerminalRow(PublicChatOutboxStatus.Expired, now - duration)
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(duration)
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task MissingIdentityTerminals_RetentionAtExactCutoff_PurgesBothCases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var duration = TimeSpan.FromMinutes(10);
        await SeedTerminalRowsAsync(
            dbFactory,
            TerminalRow(PublicChatOutboxStatus.MissingChannel, now - duration),
            TerminalRow(PublicChatOutboxStatus.MissingBot, now - duration)
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(duration)
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task TerminalRetention_MoreThanOneBatch_CleansInBoundedPasses()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        await SeedTerminalRowsAsync(
            dbFactory,
            [
                .. Enumerable
                    .Range(0, 101)
                    .Select(index =>
                        TerminalRow(
                            PublicChatOutboxStatus.Unexpected,
                            now.AddMinutes(-20).AddTicks(index)
                        )
                    ),
            ]
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(TimeSpan.FromMinutes(10))
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(1);
        }

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var verification = await dbFactory.CreateDbContextAsync();
        (await verification.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task TerminalRetention_CleanupNeverDeletesPendingRetryOrInFlightStates()
    {
        PublicChatOutboxStatus[] outstandingStatuses =
        [
            PublicChatOutboxStatus.Pending,
            PublicChatOutboxStatus.Claimed,
            PublicChatOutboxStatus.Sending,
            PublicChatOutboxStatus.SafePreSendTransient,
        ];
        var now = Utc(12, 0, 0);
        foreach (var status in outstandingStatuses)
        {
            await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var outstanding = OutstandingRow(status, now);
                db.PublicChatOutboxMessages.Add(outstanding);
                db.PublicChatOutboxMessages.Add(
                    TerminalRow(PublicChatOutboxStatus.Unexpected, now.AddMinutes(-20))
                );
                await db.SaveChangesAsync();
                if (status == PublicChatOutboxStatus.Sending)
                {
                    db.PublicChatSendReceipts.Add(
                        new PublicChatSendReceipt
                        {
                            OutboxMessageId = outstanding.Id,
                            AttemptedAtUtc = outstanding.SendStartedAtUtc!.Value,
                        }
                    );
                    await db.SaveChangesAsync();
                }
            }
            var outbox = new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                Retention(TimeSpan.FromMinutes(10))
            );

            _ = await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            );

            await using var verification = await dbFactory.CreateDbContextAsync();
            var retained = await verification.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            retained.Message.ShouldBe("must survive terminal cleanup");
            retained.Status.ShouldNotBe(PublicChatOutboxStatus.Unexpected);
        }
    }

    [Test]
    public async Task TerminalRetention_ConcurrentCleanupAndClaim_UsesDistinctConnectionsSafely()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        await SeedTerminalRowsAsync(
            dbFactory,
            [
                .. Enumerable
                    .Range(0, 101)
                    .Select(index =>
                        TerminalRow(
                            PublicChatOutboxStatus.Unexpected,
                            now.AddMinutes(-20).AddTicks(index)
                        )
                    ),
            ]
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var pending = OutstandingRow(PublicChatOutboxStatus.Pending, now);
            pending.NextAttemptAtUtc = now.UtcDateTime;
            db.PublicChatOutboxMessages.Add(pending);
            await db.SaveChangesAsync();
        }
        await using var firstContext = await dbFactory.CreateDbContextAsync();
        await using var secondContext = await dbFactory.CreateDbContextAsync();
        firstContext.ShouldNotBeSameAs(secondContext);
        firstContext
            .Database.GetDbConnection()
            .ShouldNotBeSameAs(secondContext.Database.GetDbConnection());
        var firstStore = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(TimeSpan.FromMinutes(10))
        );
        var secondStore = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(TimeSpan.FromMinutes(10))
        );

        var outcomes = await Task.WhenAll(
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
        if (outcomes.OfType<PublicChatClaimOutcome.Claimed>().Count() == 0)
        {
            _ = await firstStore.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            );
        }

        await using var verification = await dbFactory.CreateDbContextAsync();
        var liveRows = await verification
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row => row.Status == PublicChatOutboxStatus.Claimed)
            .ToArrayAsync();
        liveRows.ShouldHaveSingleItem().Message.ShouldBe("must survive terminal cleanup");
        (
            await verification
                .PublicChatOutboxMessages.AsNoTracking()
                .CountAsync(row => row.Status == PublicChatOutboxStatus.Unexpected)
        ).ShouldBeLessThanOrEqualTo(1);
    }

    [Test]
    public async Task DatabaseUnavailable_Enqueueing_ReportsFailureWithoutDelivery()
    {
        var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await dbFactory.DisposeAsync();
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            transport,
            new ManualTestTimeProvider(Utc(12, 0, 0))
        );

        var outcome = await queue.EnqueueAsync(
            Command("streamer", "not accepted"),
            CancellationToken.None
        );
        outcome
            .ShouldBeOfType<PublicChatEnqueueOutcome.Ambiguous>()
            .Cause.ShouldBeOfType<DbUpdateException>();
        transport.DeliveryCount.ShouldBe(0);
    }

    private static PublicChatOutboxBatch Batch(
        string channel,
        DateTimeOffset enqueuedAt,
        params string[] messages
    )
    {
        return new()
        {
            Channel = channel,
            EnqueuedAt = enqueuedAt,
            Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            Items = messages
                .Select(message => new PublicChatOutboxItem
                {
                    Message = message,
                    DeduplicationKey = PublicChatMessageDeduplication.Key(channel, message),
                })
                .ToImmutableArray(),
        };
    }

    private static async Task<PublicChatClaimedMessage> ClaimAsync(
        IPublicChatOutbox outbox,
        DateTimeOffset now,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown = default
    )
    {
        return (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                sendInterval,
                duplicateCooldown,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PublicChatClaimOutcome.Claimed>()
            .Message;
    }

    private static async Task BeginAndDeliverAsync(
        IPublicChatOutbox outbox,
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset deliveredAt
    )
    {
        (
            await outbox.BeginSendAsync(
                message,
                sendStartedAt,
                sendStartedAt.AddMinutes(5),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (
            await outbox.RecordDeliveryOutcomeAsync(
                message,
                new PublicChatDeliveryOutcome.Sent(),
                deliveredAt,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
    }

    private static ValueTask<PublicChatPreparationOutcome> Ready(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<PublicChatPreparationOutcome>(
            new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
        );
    }

    private static PublicChatDeliveryOutcome SafePreSendTransientOutcome()
    {
        return PublicChatDeliveryClassifier.MapPreparationFailure(
            PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                new IOException("secret preparation detail"),
                CancellationToken.None
            )
        );
    }

    private static PublicChatTerminalRetentionPolicy Retention(TimeSpan duration)
    {
        return new() { Duration = duration };
    }

    private static PublicChatDeliveryLifetimePolicy Lifetime(TimeSpan maximumAge)
    {
        return new() { MaximumAge = maximumAge };
    }

    private static void AssertExpired(PublicChatOutboxMessage row, DateTimeOffset completedAt)
    {
        row.Status.ShouldBe(PublicChatOutboxStatus.Expired);
        row.Message.ShouldBeNull();
        row.DeduplicationKey.ShouldBeNull();
        row.NextAttemptAtUtc.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();
        row.ClaimExpiresAtUtc.ShouldBeNull();
        row.SendStartedAtUtc.ShouldBeNull();
        row.CompletedAtUtc.ShouldBe(completedAt.UtcDateTime);
        row.FailurePhase.ShouldBeNull();
        row.FailureType.ShouldBeNull();
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
    }

    private static async Task SeedTerminalRowsAsync(
        SqliteBlokeBotDbFactory dbFactory,
        params PublicChatOutboxMessage[] rows
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.PublicChatOutboxMessages.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static PublicChatOutboxMessage TerminalRow(
        PublicChatOutboxStatus status,
        DateTimeOffset completedAt
    )
    {
        var row = new PublicChatOutboxMessage
        {
            Channel = "streamer",
            CreatedAtUtc = completedAt.AddHours(-1).UtcDateTime,
            ExpiresAtUtc = completedAt.AddMinutes(-59).UtcDateTime,
            CompletedAtUtc = completedAt.UtcDateTime,
            Status = status,
        };
        switch (status)
        {
            case PublicChatOutboxStatus.SafePreSendExhausted:
                row.SafePreSendFailureCount = 1;
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                row.FailureType = typeof(IOException).FullName;
                break;
            case PublicChatOutboxStatus.MissingChannel:
            case PublicChatOutboxStatus.MissingBot:
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                break;
            case PublicChatOutboxStatus.Rejected:
                row.AttemptCount = 1;
                row.SendStartedAtUtc = completedAt.AddSeconds(-1).UtcDateTime;
                row.FailurePhase = PublicChatOutboxFailurePhase.Send;
                row.RejectionCode = "followers_only";
                break;
            case PublicChatOutboxStatus.Ambiguous:
                row.AttemptCount = 1;
                row.SendStartedAtUtc = completedAt.AddSeconds(-1).UtcDateTime;
                row.FailurePhase = PublicChatOutboxFailurePhase.Send;
                row.FailureType = typeof(IOException).FullName;
                break;
            case PublicChatOutboxStatus.Unexpected:
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                row.FailureType = typeof(InvalidOperationException).FullName;
                break;
            case PublicChatOutboxStatus.Expired:
                break;
            default:
                throw new UnreachableException($"{status} is not a terminal public chat status.");
        }

        return row;
    }

    private static PublicChatOutboxMessage OutstandingRow(
        PublicChatOutboxStatus status,
        DateTimeOffset now
    )
    {
        var row = new PublicChatOutboxMessage
        {
            Channel = "streamer",
            Message = "must survive terminal cleanup",
            DeduplicationKey = PublicChatMessageDeduplication
                .Key("streamer", "must survive terminal cleanup")
                .Value,
            CreatedAtUtc = now.AddHours(-1).UtcDateTime,
            ExpiresAtUtc = now.AddHours(2).UtcDateTime,
            NextAttemptAtUtc = now.AddHours(1).UtcDateTime,
            Status = status,
        };
        switch (status)
        {
            case PublicChatOutboxStatus.Pending:
                break;
            case PublicChatOutboxStatus.Claimed:
                row.ClaimToken = Guid.NewGuid();
                row.ClaimSlot = 1;
                row.ClaimExpiresAtUtc = now.AddHours(1).UtcDateTime;
                break;
            case PublicChatOutboxStatus.Sending:
                row.AttemptCount = 1;
                row.ClaimToken = Guid.NewGuid();
                row.ClaimSlot = 1;
                row.ClaimExpiresAtUtc = now.AddHours(1).UtcDateTime;
                row.SendStartedAtUtc = now.UtcDateTime;
                break;
            case PublicChatOutboxStatus.SafePreSendTransient:
                row.SafePreSendFailureCount = 1;
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                row.FailureType = typeof(IOException).FullName;
                break;
            default:
                throw new UnreachableException($"{status} is not outstanding public chat work.");
        }

        return row;
    }

    private sealed record TerminalScenario
    {
        internal required PublicChatOutboxStatus ExpectedStatus { get; init; }

        internal required PublicChatOutboxFailurePhase ExpectedPhase { get; init; }

        internal required string? ExpectedFailureType { get; init; }

        internal required string? ExpectedRejectionCode { get; init; }

        internal required int ExpectedInitialSendCount { get; init; }

        internal required Func<ScriptedPublicChatTransport> CreateTransport { get; init; }
    }
}

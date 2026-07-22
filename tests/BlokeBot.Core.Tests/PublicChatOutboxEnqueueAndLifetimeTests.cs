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

public sealed class PublicChatOutboxEnqueueAndLifetimeTests : PublicChatOutboxIntegrationTestBase
{
    [Test]
    public async Task PinIntent_DeliveredMessage_PromotesExactProviderIdentityWithoutResend()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            var profile = new GuessRoundProfile
            {
                HostId = 0,
                Name = "default",
                Slug = "default",
                IsDefault = true,
            };
            host.Id = 41;
            profile.HostId = host.Id;
            seed.Hosts.Add(host);
            seed.Profiles.Add(profile);
            await seed.SaveChangesAsync();
            seed.Rounds.Add(
                new GuessRound
                {
                    Id = 73,
                    HostId = host.Id,
                    GuessRoundProfileId = profile.Id,
                    Status = GuessRoundStatus.Open,
                    StartedAtUtc = DateTime.UtcNow,
                }
            );
            await seed.SaveChangesAsync();
        }

        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var batch = Batch("streamer", now, "round started") with
        {
            Items =
            [
                new PublicChatOutboxItem
                {
                    Message = "round started",
                    DeduplicationKey = PublicChatMessageDeduplication.Key(
                        "streamer",
                        "round started"
                    ),
                    PinIntent = new PublicChatPinIntent(
                        41,
                        73,
                        "guessing",
                        "round_started",
                        300,
                        true
                    ),
                },
            ],
        };
        var accepted = (
            await outbox.EnqueueAsync(batch, CancellationToken.None)
        ).ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>();
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        (
            await outbox.BeginSendAsync(claimed, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Sent("exact-twitch-message-id"),
                now.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
        var receipt = await verify.PublicChatSendReceipts.SingleAsync();
        receipt.OutboxMessageId.ShouldBe(accepted.Receipt.MessageIds.Single());
        receipt.TwitchMessageId.ShouldBe("exact-twitch-message-id");
        var operation = await verify.PublicChatPinOperations.SingleAsync();
        operation.Status.ShouldBe(PublicChatPinOperationStatus.Ready);
        operation.TwitchMessageId.ShouldBe("exact-twitch-message-id");
        operation.OwnerId.ShouldBe(73);
    }

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
}

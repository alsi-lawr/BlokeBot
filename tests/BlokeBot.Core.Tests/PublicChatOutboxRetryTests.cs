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

public sealed class PublicChatOutboxRetryTests : PublicChatOutboxIntegrationTestBase
{
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
}

using System.Collections.Immutable;
using BlokeBot.Features.PublicChat;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Polly;
using Shouldly;
using TUnit.Core;
using static BlokeBot.Integration.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Integration.Tests;

public sealed class PublicChatOutboxIntegrationTests
{
    private const string ClassifiedOutboxMigration =
        "20260712184117_ClassifyPublicChatDeliveryOutcomes";
    private const string MigratedDeduplicationKey =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Test]
    public async Task SplitMessage_Enqueueing_PersistsWholeBatchBeforeAcknowledgement()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var queue = CreateQueue(
            outbox,
            new RecordingPublicChatTransport(),
            new ManualTestTimeProvider(now),
            new TwitchBotOptions { MaxChatMessageLength = 10 }
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
        rows.Select(row => row.Id).ShouldBe(receipt.MessageIds);
        rows.Select(row => row.Message).ShouldBe(["alpha", "beta gamma"]);
        rows.Select(row => row.Status).ShouldAllBe(status =>
            status == PublicChatOutboxStatus.Pending
        );
        rows.Select(row => row.CreatedAtUtc).ShouldAllBe(value =>
            value == now.UtcDateTime
        );
        rows.Select(row => row.NextAttemptAtUtc).ShouldAllBe(value =>
            value == now.UtcDateTime
        );
        rows.Select(row => row.AttemptCount).ShouldAllBe(attempts => attempts == 0);
        rows.Select(row => row.ClaimToken).ShouldAllBe(token => token == null);
    }

    [Test]
    public async Task PendingMessage_RestartingQueue_DeliversOnceAndCompletesRedacted()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var originalQueue = CreateQueue(
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy),
            new RecordingPublicChatTransport(),
            clock
        );
        var receipt = await originalQueue.EnqueueAsync(
            Command("streamer", "survives restart"),
            CancellationToken.None
        );

        var restartedOutbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy)
        );
        var restartedTransport = new RecordingPublicChatTransport();
        var restartedQueue = CreateQueue(
            restartedOutbox,
            restartedTransport,
            clock
        );
        using var stopping = new CancellationTokenSource();
        var worker = restartedQueue.RunAsync(stopping.Token);

        var delivery = await restartedTransport.ReadAsync();
        var completion = await restartedOutbox.ReadDeliveryAsync();
        await StopAsync(stopping, worker);

        delivery.Id.ShouldBe(receipt.MessageIds.ShouldHaveSingleItem());
        delivery.Message.ShouldBe("survives restart");
        delivery.Attempt.ShouldBe(1);
        completion.Id.ShouldBe(delivery.Id);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Delivered);
        row.Message.ShouldBeNull();
        row.AttemptCount.ShouldBe(1);
        row.SendStartedAtUtc.ShouldNotBeNull();
        row.CompletedAtUtc.ShouldNotBeNull();
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();

        var next = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy
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
    public async Task MessagesWithSameCreationTime_Processing_PreservesIdentityOrder()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var outbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy)
        );
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(
            outbox,
            transport,
            clock,
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
            }
        );
        _ = await queue.EnqueueAsync(
            Command("streamer", "first"),
            CancellationToken.None
        );
        _ = await queue.EnqueueAsync(
            Command("streamer", "second"),
            CancellationToken.None
        );
        _ = await queue.EnqueueAsync(
            Command("streamer", "third"),
            CancellationToken.None
        );
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
        var outbox = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "first", "second"),
            CancellationToken.None
        );
        var first = await ClaimAsync(outbox, now, TimeSpan.FromSeconds(10));
        (await outbox.BeginSendAsync(
                first,
                now,
                now.AddMinutes(5),
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (await outbox.RecordDeliveryOutcomeAsync(
                first,
                new PublicChatDeliveryOutcome.Sent(),
                now.AddSeconds(2),
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();

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

        var second = await ClaimAsync(
            outbox,
            now.AddSeconds(12),
            TimeSpan.FromSeconds(10)
        );
        second.Message.ShouldBe("second");
    }

    [Test]
    public async Task DuplicateAndDistinctMessages_Claiming_DelaysOnlyDuplicateFromCompletion()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var outbox = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "same", "same", "different"),
            CancellationToken.None
        );
        var first = await ClaimAsync(
            outbox,
            now,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10)
        );
        first.Message.ShouldBe("same");
        await BeginAndDeliverAsync(outbox, first, now, now.AddSeconds(2));

        var distinct = await ClaimAsync(
            outbox,
            now.AddSeconds(2),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10)
        );
        distinct.Message.ShouldBe("different");
        await BeginAndDeliverAsync(
            outbox,
            distinct,
            now.AddSeconds(2),
            now.AddSeconds(3)
        );

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
        var firstStore = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var secondStore = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var now = Utc(12, 0, 0);
        _ = await firstStore.EnqueueAsync(
            Batch("streamer", now, "only once"),
            CancellationToken.None
        );
        await using var firstContext = await dbFactory.CreateDbContextAsync();
        await using var secondContext = await dbFactory.CreateDbContextAsync();
        firstContext.ShouldNotBeSameAs(secondContext);
        firstContext.Database.GetDbConnection().ShouldNotBeSameAs(
            secondContext.Database.GetDbConnection()
        );

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
        claims.Count(outcome =>
                outcome
                    is PublicChatClaimOutcome.AwaitingAvailability
                        or PublicChatClaimOutcome.Contended
            )
            .ShouldBe(1);
        await using var verification = await dbFactory.CreateDbContextAsync();
        var claimed = await verification
            .PublicChatOutboxMessages.AsNoTracking()
            .SingleAsync();
        claimed.Status.ShouldBe(PublicChatOutboxStatus.Claimed);
        claimed.ClaimSlot.ShouldBe(1);
        claimed.ClaimToken.ShouldNotBeNull();
    }

    [Test]
    public async Task WorkerCanceledBeforeSend_Restarting_ReleasesAndDeliversPendingMessage()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var persistedOutbox = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var blockingOutbox = new BlockingBeginSendPublicChatOutbox(persistedOutbox);
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(blockingOutbox, transport, clock);
        _ = await queue.EnqueueAsync(
            Command("streamer", "recover me"),
            CancellationToken.None
        );
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
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy)
        );
        var restartedTransport = new RecordingPublicChatTransport();
        var restartedQueue = CreateQueue(
            restartedOutbox,
            restartedTransport,
            clock
        );
        using var restartedStopping = new CancellationTokenSource();
        var restartedWorker = restartedQueue.RunAsync(restartedStopping.Token);
        var delivery = await restartedTransport.ReadAsync();
        _ = await restartedOutbox.ReadDeliveryAsync();
        await StopAsync(restartedStopping, restartedWorker);

        delivery.Message.ShouldBe("recover me");
        delivery.Attempt.ShouldBe(1);
        await using var completedDb = await dbFactory.CreateDbContextAsync();
        var completed = await completedDb
            .PublicChatOutboxMessages.AsNoTracking()
            .SingleAsync();
        completed.Status.ShouldBe(PublicChatOutboxStatus.Delivered);
        completed.AttemptCount.ShouldBe(1);
    }

    [Test]
    public async Task ClaimedLeaseExpired_AfterRestart_IsReclaimedWithoutStartingAttempt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var outbox = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
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
        ).ShouldBeOfType<PublicChatClaimOutcome.Claimed>().Message;

        var reclaimed = await ClaimAsync(
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy),
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
        var outbox = new EfPublicChatOutbox(dbFactory, StandardRetryPolicy);
        var now = Utc(12, 0, 0);
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "may have sent"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        (await outbox.BeginSendAsync(
                claimed,
                now,
                now.AddSeconds(1),
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        var afterRestart = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy
        ).TryClaimNextAsync(
            now.AddSeconds(2),
            now.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        afterRestart.ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Ambiguous);
        row.Message.ShouldBeNull();
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
                                        new PublicChatProviderRejectionCode(
                                            "followers_only"
                                        )
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
                                    new InvalidOperationException(
                                        "secret provider response"
                                    ),
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
                new EfPublicChatOutbox(dbFactory, StandardRetryPolicy)
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
            transport.SendCount.ShouldBe(scenario.ExpectedInitialSendCount);

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
                row.Status.ShouldBe(scenario.ExpectedStatus);
                row.Message.ShouldBeNull();
                row.FailurePhase.ShouldBe(scenario.ExpectedPhase);
                row.FailureType.ShouldBe(scenario.ExpectedFailureType);
                row.RejectionCode.ShouldBe(scenario.ExpectedRejectionCode);
                row.HttpStatusCode.ShouldBeNull();
                row.ClaimToken.ShouldBeNull();
                row.ClaimSlot.ShouldBeNull();
                row.CompletedAtUtc.ShouldNotBeNull();
                string.Join(
                        "|",
                        row.FailureType,
                        row.RejectionCode,
                        row.Status,
                        row.FailurePhase
                    )
                    .ShouldNotContain("secret");
            }

            var afterRestart = await new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy
            ).TryClaimNextAsync(
                clock.GetUtcNow(),
                clock.GetUtcNow().AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            );
            afterRestart.ShouldBeOfType<PublicChatClaimOutcome.Empty>();

            var restartedTransport = new RecordingPublicChatTransport();
            var restartedQueue = CreateQueue(
                new EfPublicChatOutbox(dbFactory, StandardRetryPolicy),
                restartedTransport,
                clock,
                new TwitchBotOptions
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
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy)
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
                throw new InvalidOperationException(
                    "A safe preparation failure cannot send."
                )
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
                StandardRetryPolicy
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
            StandardRetryPolicy
        ).TryClaimNextAsync(
            beforeRetry.AvailableAt,
            beforeRetry.AvailableAt.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        retry.ShouldBeOfType<PublicChatClaimOutcome.Claimed>().Message.Message.ShouldBe(
            "retained payload"
        );
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
        var schedulingStore = new EfPublicChatOutbox(dbFactory, schedulingPolicy);
        _ = await schedulingStore.EnqueueAsync(
            Batch("streamer", now, "must not prepare again"),
            CancellationToken.None
        );
        var initial = await ClaimAsync(schedulingStore, now, TimeSpan.Zero);
        (await schedulingStore.RecordDeliveryOutcomeAsync(
                initial,
                SafePreSendTransientOutcome(),
                now,
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        var retryAt = now.AddSeconds(1);
        var firstRestartStore = new EfPublicChatOutbox(dbFactory, boundedPolicy);
        var secondRestartStore = new EfPublicChatOutbox(dbFactory, boundedPolicy);
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
            (outcome is PublicChatClaimOutcome.Empty or PublicChatClaimOutcome.Contended)
                .ShouldBeTrue();
        (
            await firstRestartStore.TryClaimNextAsync(
                retryAt,
                retryAt.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendExhausted);
        row.Message.ShouldBeNull();
        row.AttemptCount.ShouldBe(0);
        row.SafePreSendFailureCount.ShouldBe(1);
        row.NextAttemptAtUtc.ShouldBe(retryAt.UtcDateTime);
        row.CompletedAtUtc.ShouldBe(retryAt.UtcDateTime);
        row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Preparation);
        row.FailureType.ShouldBe(typeof(IOException).FullName);
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();
    }

    [Test]
    public async Task MigratedSafePreSendRetry_NormalizingConcurrently_SchedulesOnceAndClaimsOnceAtDueTime()
    {
        var failedAt = Utc(12, 0, 0);
        await using var dbFactory = await CreateMigratedSafePreSendRetryAsync(failedAt);
        var retryPolicy = CreateRetryPolicy(
            3,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            DelayBackoffType.Exponential
        );
        var firstStore = new EfPublicChatOutbox(dbFactory, retryPolicy);
        var secondStore = new EfPublicChatOutbox(dbFactory, retryPolicy);

        var normalization = await Task.WhenAll(
            firstStore
                .TryClaimNextAsync(
                    failedAt,
                    failedAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            secondStore
                .TryClaimNextAsync(
                    failedAt,
                    failedAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask()
        );
        normalization.OfType<PublicChatClaimOutcome.Claimed>().ShouldBeEmpty();
        foreach (var outcome in normalization)
        {
            (outcome
                    is PublicChatClaimOutcome.AwaitingAvailability
                        or PublicChatClaimOutcome.Contended)
                .ShouldBeTrue();
        }

        var dueAt = failedAt.AddSeconds(5);
        var normalized = (
            await firstStore.TryClaimNextAsync(
                failedAt,
                failedAt.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        normalized.AvailableAt.ShouldBe(dueAt);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendTransient);
            row.SafePreSendFailureCount.ShouldBe(1);
            row.NextAttemptAtUtc.ShouldBe(dueAt.UtcDateTime);
            row.CompletedAtUtc.ShouldBeNull();
        }

        var afterSecondRestart = (
            await new EfPublicChatOutbox(dbFactory, retryPolicy).TryClaimNextAsync(
                failedAt.AddSeconds(4),
                failedAt.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        afterSecondRestart.AvailableAt.ShouldBe(dueAt);

        var dueClaims = await Task.WhenAll(
            new EfPublicChatOutbox(dbFactory, retryPolicy)
                .TryClaimNextAsync(
                    dueAt,
                    dueAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            new EfPublicChatOutbox(dbFactory, retryPolicy)
                .TryClaimNextAsync(
                    dueAt,
                    dueAt.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask()
        );
        dueClaims.OfType<PublicChatClaimOutcome.Claimed>().ShouldHaveSingleItem();
        dueClaims.Count(outcome =>
                outcome
                    is PublicChatClaimOutcome.AwaitingAvailability
                        or PublicChatClaimOutcome.Contended
            )
            .ShouldBe(1);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.Claimed);
            row.SafePreSendFailureCount.ShouldBe(1);
            row.NextAttemptAtUtc.ShouldBe(dueAt.UtcDateTime);
        }
    }

    [Test]
    public async Task MigratedSafePreSendRetry_WithAttemptLimitOne_TerminalizesRedactedWithoutClaim()
    {
        var failedAt = Utc(12, 0, 0);
        await using var dbFactory = await CreateMigratedSafePreSendRetryAsync(failedAt);
        var retryPolicy = CreateRetryPolicy(
            1,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            DelayBackoffType.Exponential
        );

        var outcome = await new EfPublicChatOutbox(
            dbFactory,
            retryPolicy
        ).TryClaimNextAsync(
            failedAt,
            failedAt.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendExhausted);
        row.Message.ShouldBeNull();
        row.AttemptCount.ShouldBe(0);
        row.SafePreSendFailureCount.ShouldBe(1);
        row.CompletedAtUtc.ShouldBe(failedAt.UtcDateTime);
        row.SendStartedAtUtc.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();
        row.FailureType.ShouldBe(typeof(IOException).FullName);
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
            new EfPublicChatOutbox(dbFactory, retryPolicy)
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
                    throw new InvalidOperationException(
                        "A safe preparation failure cannot send."
                    )
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
            new EfPublicChatOutbox(dbFactory, retryPolicy)
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
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Delivered);
        row.Message.ShouldBeNull();
        row.AttemptCount.ShouldBe(1);
        row.SafePreSendFailureCount.ShouldBe(1);
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
            new EfPublicChatOutbox(dbFactory, retryPolicy)
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
                throw new InvalidOperationException(
                    "A safe preparation failure cannot send."
                )
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
            row.AttemptCount.ShouldBe(0);
            row.SafePreSendFailureCount.ShouldBe(2);
            row.CompletedAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
            row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Preparation);
            row.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
            row.HttpStatusCode.ShouldBe(503);
        }

        var afterExhaustion = await new EfPublicChatOutbox(
            dbFactory,
            retryPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        afterExhaustion.ShouldBeOfType<PublicChatClaimOutcome.Empty>();
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
        var firstStore = new EfPublicChatOutbox(dbFactory, retryPolicy);
        var secondStore = new EfPublicChatOutbox(dbFactory, retryPolicy);
        var now = Utc(12, 0, 0);
        _ = await firstStore.EnqueueAsync(
            Batch("streamer", now, "safe concurrent retry"),
            CancellationToken.None
        );
        var initial = await ClaimAsync(firstStore, now, TimeSpan.Zero);
        (await firstStore.RecordDeliveryOutcomeAsync(
                initial,
                SafePreSendTransientOutcome(),
                now,
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        await using var firstContext = await dbFactory.CreateDbContextAsync();
        await using var secondContext = await dbFactory.CreateDbContextAsync();
        firstContext.ShouldNotBeSameAs(secondContext);
        firstContext.Database.GetDbConnection().ShouldNotBeSameAs(
            secondContext.Database.GetDbConnection()
        );

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
        claims.Count(outcome =>
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
        var persistedOutbox = new EfPublicChatOutbox(dbFactory, retryPolicy);
        _ = await persistedOutbox.EnqueueAsync(
            Batch("streamer", clock.GetUtcNow(), "retain safe retry"),
            CancellationToken.None
        );
        var initial = await ClaimAsync(
            persistedOutbox,
            clock.GetUtcNow(),
            TimeSpan.Zero
        );
        (await persistedOutbox.RecordDeliveryOutcomeAsync(
                initial,
                SafePreSendTransientOutcome(),
                clock.GetUtcNow(),
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        clock.Advance(TimeSpan.FromSeconds(2));

        using var stopping = new CancellationTokenSource();
        var queue = CreateQueue(
            new EfPublicChatOutbox(dbFactory, retryPolicy),
            new ScriptedPublicChatTransport(
                (_, cancellationToken) =>
                {
                    stopping.Cancel();
                    return ValueTask.FromException<PublicChatPreparationOutcome>(
                        new OperationCanceledException(cancellationToken)
                    );
                },
                static (_, _) =>
                    throw new InvalidOperationException(
                        "Canceled preparation cannot send."
                    )
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
            retryPolicy
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
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy),
            transport,
            new ManualTestTimeProvider(Utc(12, 0, 0))
        );
        _ = await queue.EnqueueAsync(
            Command("streamer", "still pending"),
            CancellationToken.None
        );

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
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy),
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
            StandardRetryPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        afterRestart.ShouldBeOfType<PublicChatClaimOutcome.Empty>();
    }

    [Test]
    public async Task DatabaseUnavailable_Enqueueing_ReportsFailureWithoutDelivery()
    {
        var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await dbFactory.DisposeAsync();
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy),
            transport,
            new ManualTestTimeProvider(Utc(12, 0, 0))
        );

        await Should.ThrowAsync<DbUpdateException>(() =>
            queue
                .EnqueueAsync(
                    Command("streamer", "not accepted"),
                    CancellationToken.None
                )
                .AsTask()
        );
        transport.DeliveryCount.ShouldBe(0);
    }

    private static PublicChatOutboxBatch Batch(
        string channel,
        DateTimeOffset enqueuedAt,
        params string[] messages
    ) =>
        new()
        {
            Channel = channel,
            EnqueuedAt = enqueuedAt,
            Items = messages
                .Select(message =>
                    new PublicChatOutboxItem
                    {
                        Message = message,
                        DeduplicationKey = PublicChatMessageDeduplication.Key(
                            channel,
                            message
                        ),
                    }
                )
                .ToImmutableArray(),
        };

    private static async Task<PublicChatClaimedMessage> ClaimAsync(
        IPublicChatOutbox outbox,
        DateTimeOffset now,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown = default
    ) =>
        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                sendInterval,
                duplicateCooldown,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Claimed>().Message;

    private static async Task BeginAndDeliverAsync(
        IPublicChatOutbox outbox,
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset deliveredAt
    )
    {
        (await outbox.BeginSendAsync(
                message,
                sendStartedAt,
                sendStartedAt.AddMinutes(5),
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (await outbox.RecordDeliveryOutcomeAsync(
                message,
                new PublicChatDeliveryOutcome.Sent(),
                deliveredAt,
                CancellationToken.None
            ))
            .ShouldBeOfType<PublicChatClaimUpdate.Applied>();
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

    private static PublicChatDeliveryOutcome SafePreSendTransientOutcome() =>
        PublicChatDeliveryClassifier.MapPreparationFailure(
            PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                new IOException("secret preparation detail"),
                CancellationToken.None
            )
        );

    private static async Task<SqliteBlokeBotDbFactory> CreateMigratedSafePreSendRetryAsync(
        DateTimeOffset failedAt
    )
    {
        var dbFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(ClassifiedOutboxMigration);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public_chat_outbox
                (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                 Status, AttemptCount, CompletedAtUtc, FailurePhase, FailureType)
            VALUES
                ('streamer', 'migrated safe retry', {MigratedDeduplicationKey},
                 {failedAt.UtcDateTime}, {failedAt.UtcDateTime}, 'SafePreSendTransient', 0,
                 {failedAt.UtcDateTime}, 'Preparation', {typeof(IOException).FullName})
            """
        );
        await migrator.MigrateAsync();
        return dbFactory;
    }

    private sealed record TerminalScenario
    {
        internal required PublicChatOutboxStatus ExpectedStatus { get; init; }

        internal required PublicChatOutboxFailurePhase ExpectedPhase { get; init; }

        internal required string? ExpectedFailureType { get; init; }

        internal required string? ExpectedRejectionCode { get; init; }

        internal required int ExpectedInitialSendCount { get; init; }

        internal required Func<ScriptedPublicChatTransport> CreateTransport
        {
            get;
            init;
        }
    }
}

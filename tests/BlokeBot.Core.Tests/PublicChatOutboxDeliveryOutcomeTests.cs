using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class PublicChatOutboxDeliveryOutcomeTests : PublicChatOutboxIntegrationTestBase
{
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
}

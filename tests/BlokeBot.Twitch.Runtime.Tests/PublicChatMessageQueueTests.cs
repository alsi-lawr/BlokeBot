using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using BlokeBot.Eventing;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatMessageQueueTests
{
    private static readonly PublicChatRetryPolicy _standardRetryPolicy = new()
    {
        AttemptLimit = 3,
        Delay = TimeSpan.FromSeconds(1),
        MaximumDelay = TimeSpan.FromSeconds(30),
        DelayBackoffType = DelayBackoffType.Exponential,
    };

    [Test]
    public void MessageWithMultipleBreakTypes_Splitting_PrefersLineSentenceThenWord()
    {
        PublicChatMessageSplitter
            .Split("first line\nsecond line", 12)
            .ShouldBe(["first line", "second line"]);
        PublicChatMessageSplitter
            .Split("First sentence. Second one.", 20)
            .ShouldBe(["First sentence.", "Second one."]);
        PublicChatMessageSplitter.Split("alpha beta gamma", 10).ShouldBe(["alpha", "beta gamma"]);
    }

    [Test]
    [Arguments(DelayBackoffType.Constant, 2, 2, 2)]
    [Arguments(DelayBackoffType.Linear, 2, 4, 5)]
    [Arguments(DelayBackoffType.Exponential, 2, 4, 5)]
    public void SafePreSendRetryPolicy_Scheduling_UsesConfiguredBoundedBackoff(
        DelayBackoffType backoffType,
        int firstDelaySeconds,
        int secondDelaySeconds,
        int thirdDelaySeconds
    )
    {
        var policy = new PublicChatRetryPolicy
        {
            AttemptLimit = 4,
            Delay = TimeSpan.FromSeconds(2),
            MaximumDelay = TimeSpan.FromSeconds(5),
            DelayBackoffType = backoffType,
        };
        var failedAt = Utc(12, 0, 0);

        var first = PublicChatSafePreSendRetrySchedule
            .Create(policy, new PublicChatSafePreSendFailureCount(0), failedAt)
            .ShouldBeOfType<PublicChatSafePreSendRetryDecision.Scheduled>();
        first.NextAttemptAtUtc.ShouldBe(failedAt.AddSeconds(firstDelaySeconds));

        var second = PublicChatSafePreSendRetrySchedule
            .Create(policy, first.FailureCount, first.NextAttemptAtUtc)
            .ShouldBeOfType<PublicChatSafePreSendRetryDecision.Scheduled>();
        second.NextAttemptAtUtc.ShouldBe(first.NextAttemptAtUtc.AddSeconds(secondDelaySeconds));

        var third = PublicChatSafePreSendRetrySchedule
            .Create(policy, second.FailureCount, second.NextAttemptAtUtc)
            .ShouldBeOfType<PublicChatSafePreSendRetryDecision.Scheduled>();
        third.NextAttemptAtUtc.ShouldBe(second.NextAttemptAtUtc.AddSeconds(thirdDelaySeconds));

        PublicChatSafePreSendRetrySchedule
            .Create(policy, third.FailureCount, third.NextAttemptAtUtc)
            .ShouldBeOfType<PublicChatSafePreSendRetryDecision.Exhausted>()
            .FailureCount.Value.ShouldBe(4);
    }

    [Test]
    [Arguments("", "message")]
    [Arguments("channel", "")]
    [Arguments(" ", "message")]
    [Arguments("channel", " ")]
    public async Task InvalidMessage_Enqueueing_ReturnsRejectedWithoutWriteOrDelivery(
        string channel,
        string message
    )
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport);

        var outcome = await queue.EnqueueAsync(Command(channel, message), CancellationToken.None);

        outcome.ShouldBeOfType<PublicChatEnqueueOutcome.Rejected>();
        outbox.EnqueueCount.ShouldBe(0);
        outbox.PendingMessages.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task ValidMessage_Sending_ReturnsAcceptedAfterDurableEnqueue()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var sender = new PublicChatMessageSender(CreateQueue(new BotOptions(), outbox, transport));
        var deadline = new PublicChatDeliveryDeadline.ProducerAbsolute(
            Utc(12, 0, 0).AddSeconds(30)
        );

        var outcome = await sender.SendAsync(
            "channel",
            "message",
            deadline,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<PublicChatSendOutcome.Accepted>();
        outbox.EnqueueCount.ShouldBe(1);
        outbox.LastEnqueuedDeadline.ShouldBeSameAs(deadline);
        outbox.PendingMessages.ShouldBe(["message"]);
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    [Arguments("", "message")]
    [Arguments("channel", "")]
    public async Task InvalidMessage_Sending_ReturnsRejectedWithoutWriteOrDelivery(
        string channel,
        string message
    )
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var sender = new PublicChatMessageSender(CreateQueue(new BotOptions(), outbox, transport));

        var outcome = await sender.SendAsync(
            channel,
            message,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            CancellationToken.None
        );

        outcome.ShouldBeOfType<PublicChatSendOutcome.Rejected>();
        outbox.EnqueueCount.ShouldBe(0);
        outbox.PendingMessages.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task InfrastructureFailure_Sending_PreservesOriginalException()
    {
        var failure = new IOException("private persistence detail");
        var outbox = new InMemoryOutbox(_standardRetryPolicy) { EnqueueFailure = failure };
        var transport = new RecordingTransport();
        var sender = new PublicChatMessageSender(CreateQueue(new BotOptions(), outbox, transport));

        var thrown = await Should.ThrowAsync<IOException>(() =>
            sender
                .SendAsync(
                    "channel",
                    "message",
                    new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                    CancellationToken.None
                )
                .AsTask()
        );

        thrown.ShouldBeSameAs(failure);
        outbox.PendingMessages.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCancellation_Sending_PropagatesWithoutWriteOrDelivery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var sender = new PublicChatMessageSender(CreateQueue(new BotOptions(), outbox, transport));

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            sender
                .SendAsync(
                    "channel",
                    "message",
                    new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                    cancellation.Token
                )
                .AsTask()
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        outbox.PendingMessages.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCanceledAfterCommit_Sending_ReturnsAcceptedDurableOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        var outbox = new InMemoryOutbox(_standardRetryPolicy)
        {
            AfterEnqueue = cancellation.Cancel,
        };
        var transport = new RecordingTransport();
        var sender = new PublicChatMessageSender(CreateQueue(new BotOptions(), outbox, transport));

        var outcome = await sender.SendAsync(
            "channel",
            "message",
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellation.Token
        );

        outcome.ShouldBeOfType<PublicChatSendOutcome.Accepted>();
        cancellation.IsCancellationRequested.ShouldBeTrue();
        outbox.PendingMessages.ShouldBe(["message"]);
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task MessageOverLength_Enqueueing_PersistsEveryPartBeforeDelivery()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var queue = CreateQueue(
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
                MaxChatMessageLength = 10,
            },
            outbox,
            transport
        );

        var receipt = await queue.EnqueueAsync(
            Command("channel", "alpha beta gamma"),
            CancellationToken.None
        );

        receipt
            .ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>()
            .Receipt.MessageIds.Length.ShouldBe(2);
        outbox.PendingMessages.ShouldBe(["alpha", "beta gamma"]);
        transport.Deliveries.ShouldBeEmpty();

        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        (await transport.ReadAsync()).Message.ShouldBe("alpha");
        (await transport.ReadAsync()).Message.ShouldBe("beta gamma");
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task DuplicateAndDistinctMessages_Processing_DelaysOnlyDuplicate()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var queue = CreateQueue(
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 1,
            },
            outbox,
            transport,
            clock
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("channel", "same"), CancellationToken.None);
        (await transport.ReadAsync()).Message.ShouldBe("same");
        _ = await queue.EnqueueAsync(Command("channel", "same"), CancellationToken.None);
        _ = await queue.EnqueueAsync(Command("channel", "different"), CancellationToken.None);

        (await transport.ReadAsync()).Message.ShouldBe("different");
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        (await transport.ReadAsync()).Message.ShouldBe("same");
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task RepeatedAndLaterBackups_MonitoringPublicChatQueue_AlertsOncePerIncident()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var observer = new RecordingQueueAlertObserver();
        var queue = CreateQueue(
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 10,
                DuplicateChatMessageCooldownSeconds = 0,
                PublicChatQueueAlerts = new PublicChatQueueAlertOptions { StuckAfterSeconds = 5 },
            },
            outbox,
            transport,
            clock,
            [observer]
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("channel", "first"), CancellationToken.None);
        (await transport.ReadAsync()).Message.ShouldBe("first");
        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.SentAndDeleted);
        _ = await queue.EnqueueAsync(Command("channel", "second"), CancellationToken.None);
        _ = await queue.EnqueueAsync(Command("channel", "third"), CancellationToken.None);

        await clock.WaitForTimerAtAsync(Utc(12, 0, 5));
        clock.Advance(TimeSpan.FromSeconds(5));
        var firstAlert = await observer.ReadAsync();
        firstAlert.Channel.ShouldBe("channel");
        firstAlert.OldestPendingAge.ShouldBe(TimeSpan.FromSeconds(5));
        firstAlert.PendingCount.ShouldBe(2);

        await clock.WaitForTimerAtAsync(Utc(12, 0, 10));
        clock.Advance(TimeSpan.FromSeconds(5));
        (await transport.ReadAsync()).Message.ShouldBe("second");
        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.SentAndDeleted);
        await clock.WaitForTimerAtAsync(Utc(12, 0, 20));
        clock.Advance(TimeSpan.FromSeconds(10));
        (await transport.ReadAsync()).Message.ShouldBe("third");
        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.SentAndDeleted);
        observer.Alerts.Count.ShouldBe(1);

        _ = await queue.EnqueueAsync(Command("channel", "fourth"), CancellationToken.None);
        await clock.WaitForTimerAtAsync(Utc(12, 0, 25));
        clock.Advance(TimeSpan.FromSeconds(5));
        _ = await observer.ReadAsync();
        await clock.WaitForTimerAtAsync(Utc(12, 0, 30));
        clock.Advance(TimeSpan.FromSeconds(5));
        (await transport.ReadAsync()).Message.ShouldBe("fourth");
        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.SentAndDeleted);
        observer.Alerts.Count.ShouldBe(2);
        await StopAsync(stopping, worker);
    }

    [Test]
    public void BacklogsAcrossChannels_CapturingAlerts_TracksIndependentlyAndResetsDrained()
    {
        var monitor = new PublicChatQueueBacklogMonitor();
        var now = Utc(12, 0, 10);
        PublicChatPendingMessage[] pending =
        [
            new("first", now.AddSeconds(-10)),
            new("first", now.AddSeconds(-6)),
            new("second", now.AddSeconds(-7)),
        ];

        var firstIncidents = monitor.CaptureAlerts(
            pending,
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        firstIncidents.Select(alert => alert.Channel).ShouldBe(["first", "second"]);
        monitor.CaptureAlerts(pending, now, TimeSpan.FromSeconds(5), enabled: true).ShouldBeEmpty();

        monitor.ResetDrainedChannels([new("second", now)]);
        var nextFirstIncident = monitor.CaptureAlerts(
            [new("first", now.AddSeconds(-5)), new("second", now.AddSeconds(-5))],
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        nextFirstIncident.Select(alert => alert.Channel).ShouldBe(["first"]);
    }

    [Test]
    public async Task FailingPublicChatQueueAlertObserver_DispatchingAlert_NotifiesRemainingObservers()
    {
        var recording = new RecordingQueueAlertObserver();
        var dispatcher = new PublicChatQueueAlertDispatcher(
            [new ThrowingQueueAlertObserver("Observer failed."), recording],
            QueueAlertFanOut()
        );
        var alert = new PublicChatQueueBacklog(
            "channel",
            2,
            TimeSpan.FromSeconds(5),
            Utc(12, 0, 0)
        );

        await dispatcher.NotifyAsync([alert], CancellationToken.None);

        recording.Alerts.ShouldBe([alert]);
    }

    [Test]
    public async Task AlertHandlingEscalation_ProcessingPublicChatQueue_ContinuesDelivery()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new RecordingTransport();
        var logger = new RecordingLogger<PublicChatMessageQueue>();
        var queue = CreateQueue(
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 10,
                DuplicateChatMessageCooldownSeconds = 0,
                PublicChatQueueAlerts = new PublicChatQueueAlertOptions { StuckAfterSeconds = 5 },
            },
            outbox,
            transport,
            clock,
            [new ThrowingQueueAlertObserver("observer secret payload")],
            RuntimeTestObserverFanOut.EscalatingContinue<
                PublicChatQueueAlertObserverBoundary,
                PublicChatQueueBacklog,
                PublicChatQueueAlertDeadLetter
            >(
                BotObserverBoundaries.PublicChatQueueAlerts,
                new IOException("reporter secret payload")
            ),
            logger
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("channel", "first"), CancellationToken.None);
        _ = await transport.ReadAsync();
        _ = await queue.EnqueueAsync(
            Command("channel", "second secret chat payload"),
            CancellationToken.None
        );
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        (await transport.ReadAsync()).Message.ShouldBe("second secret chat payload");

        logger.Entries.ShouldNotBeEmpty();
        foreach (var entry in logger.Entries)
        {
            entry.Exception.ShouldBeNull();
            entry.Message.ShouldContain("Continuing queued chat processing");
            entry.Message.ShouldNotContain("observer secret payload");
            entry.Message.ShouldNotContain("reporter secret payload");
            entry.Message.ShouldNotContain("second secret chat payload");
        }
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task PersistenceFailure_Enqueueing_IsObservableWithoutDelivery()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy)
        {
            EnqueueFailure = new IOException("Persistence unavailable."),
        };
        var transport = new RecordingTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport);

        await Should.ThrowAsync<IOException>(() =>
            queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None).AsTask()
        );

        outbox.PendingMessages.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCanceledAfterCommit_Enqueueing_LeavesDeliveryRecoverable()
    {
        using var caller = new CancellationTokenSource();
        var outbox = new InMemoryOutbox(_standardRetryPolicy) { AfterEnqueue = caller.Cancel };
        var transport = new RecordingTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport);

        var receipt = await queue.EnqueueAsync(Command("channel", "message"), caller.Token);

        receipt
            .ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>()
            .Receipt.MessageIds.Length.ShouldBe(1);
        caller.IsCancellationRequested.ShouldBeTrue();
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        (await transport.ReadAsync()).Message.ShouldBe("message");
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task SentResult_Processing_DeletesAfterOneSendAttempt()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = SuccessfulScriptedTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.SentAndDeleted);
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(1);
        snapshot.Message.ShouldBeNull();
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task SafePreparationFailure_Processing_SchedulesDurableRetryWithoutSendAttempt()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var failure = new IOException("secret preparation detail");
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new ScriptedTransport(
            (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        failure,
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("A safe preparation failure cannot send.")
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport, clock);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(
            InMemoryOutbox.RowStatus.SafePreSendTransient
        );
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.SafePreSendFailureCount.ShouldBe(1);
        snapshot.NextAttemptAt.ShouldBe(clock.GetUtcNow().AddSeconds(1));
        snapshot.Message.ShouldBe("message");
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task SafePreparationFailure_ThenReady_Processing_RetriesAfterConfiguredSchedule()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var preparationCalls = 0;
        var transport = new ScriptedTransport(
            (message, cancellationToken) =>
            {
                preparationCalls++;
                return preparationCalls == 1
                    ? ValueTask.FromResult(
                        PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                            new IOException("secret preparation detail"),
                            cancellationToken
                        )
                    )
                    : Ready(message, cancellationToken);
            },
            static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<PublicChatTransportSendResult>(
                    new PublicChatTransportSendResult.Sent()
                );
            }
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport, clock);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(
            InMemoryOutbox.RowStatus.SafePreSendTransient
        );
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.SentAndDeleted);
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(1);
        snapshot.SafePreSendFailureCount.ShouldBe(1);
        snapshot.Message.ShouldBeNull();
        transport.PrepareCount.ShouldBe(2);
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task SafePreparationFailures_ExhaustingPolicy_RedactsTerminalWithoutSendAttempt()
    {
        var policy = new PublicChatRetryPolicy
        {
            AttemptLimit = 2,
            Delay = TimeSpan.FromSeconds(2),
            MaximumDelay = TimeSpan.FromSeconds(2),
            DelayBackoffType = DelayBackoffType.Constant,
        };
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(policy);
        var transport = new ScriptedTransport(
            (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        new IOException("secret preparation detail"),
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("A safe preparation failure cannot send.")
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport, clock);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(
            InMemoryOutbox.RowStatus.SafePreSendTransient
        );
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        (await outbox.ReadCompletionAsync()).ShouldBe(
            InMemoryOutbox.RowStatus.SafePreSendExhausted
        );
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.SafePreSendFailureCount.ShouldBe(2);
        snapshot.Message.ShouldBeNull();
        transport.PrepareCount.ShouldBe(2);
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task UnexpectedPreparationFailure_Processing_RedactsTerminalWithoutSendAttempt()
    {
        var failure = new InvalidOperationException("secret preparation detail");
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new ScriptedTransport(
            (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        failure,
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("An unexpected preparation cannot send.")
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.Unexpected);
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.Message.ShouldBeNull();
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task UnexpectedPreparationFailure_Reporting_UsesOnlyRedactedStructuredContext()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var logger = new RecordingLogger<PublicChatMessageQueue>();
        var transport = new ScriptedTransport(
            (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        new InvalidOperationException("secret provider response"),
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("Unexpected preparation cannot send.")
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport, logger: logger);
        _ = await queue.EnqueueAsync(
            Command("channel", "secret chat payload"),
            CancellationToken.None
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await outbox.ReadCompletionAsync();
        await StopAsync(stopping, worker);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("Unexpected");
        entry.Message.ShouldContain(typeof(InvalidOperationException).FullName!);
        entry.Message.ShouldNotContain("secret provider response");
        entry.Message.ShouldNotContain("secret chat payload");
    }

    [Test]
    public Task MissingChannelIdentity_Processing_IsRedactedTerminalWithoutRetryOrSend()
    {
        return AssertMissingIdentityAsync(
            new PublicChatPreparationOutcome.MissingChannel(),
            InMemoryOutbox.RowStatus.MissingChannel,
            nameof(PublicChatPreparationOutcome.MissingChannel)
        );
    }

    [Test]
    public Task MissingBotIdentity_Processing_IsRedactedTerminalWithoutRetryOrSend()
    {
        return AssertMissingIdentityAsync(
            new PublicChatPreparationOutcome.MissingBot(),
            InMemoryOutbox.RowStatus.MissingBot,
            nameof(PublicChatPreparationOutcome.MissingBot)
        );
    }

    [Test]
    public async Task ExplicitRejection_Processing_RecordsRedactedTerminalAfterSendBoundary()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new ScriptedTransport(
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
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.Rejected);
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(1);
        snapshot.Message.ShouldBeNull();
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task PostBoundaryTransientFailure_Processing_IsAmbiguousWithoutRetry()
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new ScriptedTransport(
            Ready,
            static (_, _) =>
                ValueTask.FromException<PublicChatTransportSendResult>(
                    new IOException("secret response detail")
                )
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(InMemoryOutbox.RowStatus.Ambiguous);
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(1);
        snapshot.Message.ShouldBeNull();
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task CallerCanceledDuringPreparation_Processing_ReleasesPendingWithoutAttempt()
    {
        using var stopping = new CancellationTokenSource();
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new ScriptedTransport(
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
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);

        await queue.RunAsync(stopping.Token);

        var snapshot = outbox.SingleSnapshot;
        snapshot.Status.ShouldBe(InMemoryOutbox.RowStatus.Pending);
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.Message.ShouldBe("message");
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task CallerCanceledAfterSendBoundary_Processing_PersistsAmbiguousAndPropagatesToWorker()
    {
        using var stopping = new CancellationTokenSource();
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var transport = new ScriptedTransport(
            Ready,
            (_, cancellationToken) =>
            {
                stopping.Cancel();
                return ValueTask.FromException<PublicChatTransportSendResult>(
                    new OperationCanceledException(cancellationToken)
                );
            }
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);

        await queue.RunAsync(stopping.Token);

        var snapshot = outbox.SingleSnapshot;
        snapshot.Status.ShouldBe(InMemoryOutbox.RowStatus.Ambiguous);
        snapshot.AttemptCount.ShouldBe(1);
        snapshot.Message.ShouldBeNull();
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task SafePreparationFailure_MonitoringOutstandingBacklog_StillRaisesAlert()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var observer = new RecordingQueueAlertObserver();
        var transport = new ScriptedTransport(
            (_, cancellationToken) =>
                ValueTask.FromResult(
                    PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                        new IOException("preparation failed"),
                        cancellationToken
                    )
                ),
            static (_, _) =>
                throw new InvalidOperationException("A safe preparation failure cannot send.")
        );
        var queue = CreateQueue(
            new BotOptions
            {
                PublicChatQueueAlerts = new PublicChatQueueAlertOptions { StuckAfterSeconds = 5 },
            },
            outbox,
            transport,
            clock,
            [observer]
        );
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        _ = await outbox.ReadCompletionAsync();

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        var alert = await observer.ReadAsync();
        await StopAsync(stopping, worker);

        alert.Channel.ShouldBe("channel");
        alert.PendingCount.ShouldBe(1);
        alert.OldestPendingAge.ShouldBe(TimeSpan.FromSeconds(5));
    }

    private static PublicChatMessageQueue CreateQueue(
        BotOptions options,
        IPublicChatOutbox outbox,
        IPublicChatTransport transport,
        TimeProvider? timeProvider = null,
        IEnumerable<IPublicChatQueueAlertObserver>? observers = null,
        ObserverFanOut<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >? fanOut = null,
        ILogger<PublicChatMessageQueue>? logger = null
    )
    {
        return new(
            BotSettings.FromOptions(options),
            timeProvider ?? TimeProvider.System,
            new PublicChatQueueBacklogMonitor(),
            new PublicChatQueueAlertDispatcher(observers ?? [], fanOut ?? QueueAlertFanOut()),
            outbox,
            transport,
            logger ?? NullLogger<PublicChatMessageQueue>.Instance
        );
    }

    private static async Task AssertMissingIdentityAsync(
        PublicChatPreparationOutcome outcome,
        InMemoryOutbox.RowStatus expectedStatus,
        string expectedDiagnostic
    )
    {
        var outbox = new InMemoryOutbox(_standardRetryPolicy);
        var logger = new RecordingLogger<PublicChatMessageQueue>();
        var transport = new ScriptedTransport(
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(outcome);
            },
            static (_, _) =>
                throw new InvalidOperationException("Missing identity preparation cannot send.")
        );
        var queue = CreateQueue(new BotOptions(), outbox, transport, logger: logger);
        _ = await queue.EnqueueAsync(
            Command("private-channel-login", "secret chat payload"),
            CancellationToken.None
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        (await outbox.ReadCompletionAsync()).ShouldBe(expectedStatus);
        await StopAsync(stopping, worker);

        var snapshot = outbox.SingleSnapshot;
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.Message.ShouldBeNull();
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(0);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain(expectedDiagnostic);
        entry.Message.ShouldNotContain("private-channel-login");
        entry.Message.ShouldNotContain("secret chat payload");
    }

    private static PublicChatEnqueueCommand Command(string channel, string message)
    {
        return new()
        {
            Channel = channel,
            Message = message,
            Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
        };
    }

    private static PublicChatPreparedSend Prepared(PublicChatClaimedMessage message)
    {
        return new()
        {
            Message = message,
            AppAccessToken = "app-token",
            BroadcasterId = "broadcaster-id",
            BotUserId = "bot-user-id",
        };
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

    private static ScriptedTransport SuccessfulScriptedTransport()
    {
        return new(
            Ready,
            static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<PublicChatTransportSendResult>(
                    new PublicChatTransportSendResult.Sent()
                );
            }
        );
    }

    private static ObserverFanOut<
        PublicChatQueueAlertObserverBoundary,
        PublicChatQueueBacklog,
        PublicChatQueueAlertDeadLetter
    > QueueAlertFanOut()
    {
        return RuntimeTestObserverFanOut.Continue<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >(BotObserverBoundaries.PublicChatQueueAlerts);
    }

    private static async Task StopAsync(CancellationTokenSource stopping, Task worker)
    {
        await stopping.CancelAsync();
        await worker;
    }

    private static DateTimeOffset Utc(int hour, int minute, int second)
    {
        return new(2026, 7, 12, hour, minute, second, TimeSpan.Zero);
    }

    private sealed class RecordingTransport : IPublicChatTransport
    {
        private readonly Channel<PublicChatClaimedMessage> _delivered =
            Channel.CreateUnbounded<PublicChatClaimedMessage>();

        public List<PublicChatClaimedMessage> Deliveries { get; } = [];

        public ValueTask<PublicChatPreparationOutcome> PrepareAsync(
            PublicChatClaimedMessage message,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult<PublicChatPreparationOutcome>(
                new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
            );
        }

        public ValueTask<PublicChatTransportSendResult> SendAsync(
            PublicChatPreparedSend prepared,
            CancellationToken cancellationToken
        )
        {
            var message = prepared.Message;
            Deliveries.Add(message);
            if (!_delivered.Writer.TryWrite(message))
            {
                throw new InvalidOperationException(
                    "The transport delivery could not be observed."
                );
            }

            return ValueTask.FromResult<PublicChatTransportSendResult>(
                new PublicChatTransportSendResult.Sent()
            );
        }

        public ValueTask<PublicChatClaimedMessage> ReadAsync()
        {
            return _delivered.Reader.ReadAsync();
        }
    }

    private sealed class ScriptedTransport(
        Func<
            PublicChatClaimedMessage,
            CancellationToken,
            ValueTask<PublicChatPreparationOutcome>
        > prepare,
        Func<
            PublicChatPreparedSend,
            CancellationToken,
            ValueTask<PublicChatTransportSendResult>
        > send
    ) : IPublicChatTransport
    {
        public int PrepareCount { get; private set; }

        public int SendCount { get; private set; }

        public ValueTask<PublicChatPreparationOutcome> PrepareAsync(
            PublicChatClaimedMessage message,
            CancellationToken cancellationToken
        )
        {
            PrepareCount++;
            return prepare(message, cancellationToken);
        }

        public ValueTask<PublicChatTransportSendResult> SendAsync(
            PublicChatPreparedSend prepared,
            CancellationToken cancellationToken
        )
        {
            SendCount++;
            return send(prepared, cancellationToken);
        }
    }

    private sealed class RecordingQueueAlertObserver : IPublicChatQueueAlertObserver
    {
        private readonly Channel<PublicChatQueueBacklog> _alerts =
            Channel.CreateUnbounded<PublicChatQueueBacklog>();

        public List<PublicChatQueueBacklog> Alerts { get; } = [];

        public ValueTask QueueBackedUpAsync(
            PublicChatQueueBacklog backlog,
            CancellationToken cancellationToken
        )
        {
            Alerts.Add(backlog);
            if (!_alerts.Writer.TryWrite(backlog))
            {
                throw new InvalidOperationException("The queue alert could not be observed.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<PublicChatQueueBacklog> ReadAsync()
        {
            return _alerts.Reader.ReadAsync();
        }
    }

    private sealed class ThrowingQueueAlertObserver(string failureMessage)
        : IPublicChatQueueAlertObserver
    {
        public ValueTask QueueBackedUpAsync(
            PublicChatQueueBacklog backlog,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    private sealed class InMemoryOutbox(PublicChatRetryPolicy retryPolicy) : IPublicChatOutbox
    {
        private readonly object _gate = new();
        private readonly List<Row> _rows = [];
        private readonly List<Delivery> _deliveries = [];
        private readonly Channel<RowStatus> _completions = Channel.CreateUnbounded<RowStatus>();
        private readonly PublicChatRetryPolicy _safePreSendRetryPolicy =
            retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        private long _nextId = 1;

        public Action? AfterEnqueue { get; init; }

        public Exception? EnqueueFailure { get; init; }

        public int EnqueueCount { get; private set; }

        public PublicChatDeliveryDeadline? LastEnqueuedDeadline { get; private set; }

        public IReadOnlyList<string> PendingMessages
        {
            get
            {
                lock (_gate)
                {
                    return _rows
                        .Where(row => row.Status == RowStatus.Pending)
                        .Select(row => row.Message!)
                        .ToArray();
                }
            }
        }

        public OutboxSnapshot SingleSnapshot
        {
            get
            {
                lock (_gate)
                {
                    if (_rows.Count == 0)
                    {
                        return field.ShouldNotBeNull();
                    }

                    var row = _rows.ShouldHaveSingleItem();
                    return Snapshot(row);
                }
            }
            private set;
        }

        private static OutboxSnapshot Snapshot(Row row)
        {
            return new()
            {
                Status = row.Status,
                AttemptCount = row.AttemptCount,
                SafePreSendFailureCount = row.SafePreSendFailureCount,
                NextAttemptAt = row.NextAttemptAt,
                Message = row.Message,
            };
        }

        public ValueTask<RowStatus> ReadCompletionAsync()
        {
            return _completions.Reader.ReadAsync();
        }

        public ValueTask<PublicChatEnqueueOutcome> EnqueueAsync(
            PublicChatOutboxBatch batch,
            CancellationToken cancellationToken
        )
        {
            EnqueueCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (EnqueueFailure is { } failure)
            {
                throw failure;
            }

            long[] ids;
            lock (_gate)
            {
                LastEnqueuedDeadline = batch.Deadline;
                ids = batch
                    .Items.Select(item =>
                    {
                        var id = _nextId++;
                        _rows.Add(new Row(id, batch.Channel, item, batch.EnqueuedAt));
                        return id;
                    })
                    .ToArray();
            }

            AfterEnqueue?.Invoke();
            return ValueTask.FromResult<PublicChatEnqueueOutcome>(
                new PublicChatEnqueueOutcome.Accepted(
                    new PublicChatOutboxReceipt(ImmutableArray.Create(ids))
                )
            );
        }

        public ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
            DateTimeOffset now,
            DateTimeOffset claimExpiresAt,
            TimeSpan sendInterval,
            TimeSpan duplicateCooldown,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var active = _rows.FirstOrDefault(row =>
                    row.Status is RowStatus.Claimed or RowStatus.Sending
                );
                if (active is not null)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.AwaitingAvailability(active.ClaimExpiresAt)
                    );
                }

                var previousAttempt = _rows
                    .Where(row => row.CompletedAt is not null && row.AttemptCount > 0)
                    .Select(row => row.CompletedAt!.Value)
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Append(
                        _deliveries
                            .Select(delivery => delivery.CompletedAt)
                            .DefaultIfEmpty(DateTimeOffset.MinValue)
                            .Max()
                    )
                    .Max();
                var claimable = _rows
                    .Where(row => row.Status is RowStatus.Pending or RowStatus.SafePreSendTransient)
                    .Select(row =>
                    {
                        var eligibleAt = row.NextAttemptAt;
                        if (previousAttempt != DateTimeOffset.MinValue)
                        {
                            eligibleAt = Max(eligibleAt, previousAttempt + sendInterval);
                        }

                        var previousDelivery = _deliveries
                            .Where(delivery =>
                                delivery.DeduplicationKey == row.Item.DeduplicationKey
                            )
                            .Select(delivery => delivery.CompletedAt)
                            .DefaultIfEmpty(DateTimeOffset.MinValue)
                            .Max();
                        if (previousDelivery != DateTimeOffset.MinValue)
                        {
                            eligibleAt = Max(eligibleAt, previousDelivery + duplicateCooldown);
                        }

                        return new Candidate(row, eligibleAt);
                    })
                    .OrderBy(candidate => candidate.EligibleAt)
                    .ThenBy(candidate => candidate.Row.EnqueuedAt)
                    .ThenBy(candidate => candidate.Row.Id)
                    .FirstOrDefault();
                if (claimable is null)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.Empty()
                    );
                }

                if (claimable.EligibleAt > now)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.AwaitingAvailability(claimable.EligibleAt)
                    );
                }

                var token = new PublicChatClaimToken(Guid.NewGuid());
                claimable.Row.Status = RowStatus.Claimed;
                claimable.Row.ClaimToken = token;
                claimable.Row.ClaimExpiresAt = claimExpiresAt;
                return ValueTask.FromResult<PublicChatClaimOutcome>(
                    new PublicChatClaimOutcome.Claimed(claimable.Row.Claimed(token))
                );
            }
        }

        public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset sendStartedAt,
            DateTimeOffset claimExpiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = RowStatus.Sending;
                row.AttemptCount++;
                row.ClaimExpiresAt = claimExpiresAt;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
            PublicChatClaimedMessage message,
            PublicChatDeliveryOutcome outcome,
            DateTimeOffset recordedAt,
            CancellationToken cancellationToken
        )
        {
            return outcome.Match(
                _ => DeleteSending(message, recordedAt, cancellationToken),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.MissingChannel,
                        recordedAt,
                        cancellationToken
                    ),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.MissingBot,
                        recordedAt,
                        cancellationToken
                    ),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.Unexpected,
                        recordedAt,
                        cancellationToken
                    ),
                _ => RecordSafePreSendTransient(message, recordedAt, cancellationToken),
                _ => CompleteSending(message, RowStatus.Rejected, recordedAt, cancellationToken),
                _ => CompleteSending(message, RowStatus.Ambiguous, recordedAt, cancellationToken),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.Unexpected,
                        recordedAt,
                        cancellationToken
                    )
            );
        }

        public ValueTask<PublicChatClaimUpdate> RecordPostBoundaryInterruptionAsync(
            PublicChatClaimedMessage message,
            PublicChatFailureDiagnostic.Send diagnostic,
            DateTimeOffset interruptedAt,
            CancellationToken cancellationToken
        )
        {
            return CompleteSending(message, RowStatus.Ambiguous, interruptedAt, cancellationToken);
        }

        public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset releasedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status =
                    row.SafePreSendFailureCount > 0
                        ? RowStatus.SafePreSendTransient
                        : RowStatus.Pending;
                row.ClaimToken = null;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IReadOnlyList<PublicChatPendingMessage> pending = _rows
                    .Where(row =>
                        row.Status == RowStatus.Sending
                        || (
                            row.ExpiresAt > now
                            && row.Status
                                is RowStatus.Pending
                                    or RowStatus.Claimed
                                    or RowStatus.SafePreSendTransient
                        )
                    )
                    .OrderBy(row => row.EnqueuedAt)
                    .ThenBy(row => row.Id)
                    .Select(row => new PublicChatPendingMessage(row.Channel, row.EnqueuedAt))
                    .ToArray();
                return ValueTask.FromResult(pending);
            }
        }

        private ValueTask<PublicChatClaimUpdate> CompleteSending(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Sending);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = status;
                row.CompletedAt = completedAt;
                row.Message = null;
                row.ClaimToken = null;
                NotifyCompletion(status);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private ValueTask<PublicChatClaimUpdate> DeleteSending(
            PublicChatClaimedMessage message,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Sending);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = RowStatus.SentAndDeleted;
                row.CompletedAt = completedAt;
                row.Message = null;
                row.ClaimToken = null;
                SingleSnapshot = Snapshot(row);
                _deliveries.Add(new Delivery(row.Item.DeduplicationKey, completedAt));
                _rows.Remove(row);
                NotifyCompletion(RowStatus.SentAndDeleted);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private ValueTask<PublicChatClaimUpdate> RecordSafePreSendTransient(
            PublicChatClaimedMessage message,
            DateTimeOffset recordedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                var decision = PublicChatSafePreSendRetrySchedule.Create(
                    _safePreSendRetryPolicy,
                    new PublicChatSafePreSendFailureCount(row.SafePreSendFailureCount),
                    recordedAt
                );
                switch (decision)
                {
                    case PublicChatSafePreSendRetryDecision.Scheduled scheduled:
                        row.Status = RowStatus.SafePreSendTransient;
                        row.SafePreSendFailureCount = scheduled.FailureCount.Value;
                        row.NextAttemptAt = scheduled.NextAttemptAtUtc;
                        row.CompletedAt = null;
                        break;
                    case PublicChatSafePreSendRetryDecision.Exhausted exhausted:
                        row.Status = RowStatus.SafePreSendExhausted;
                        row.SafePreSendFailureCount = exhausted.FailureCount.Value;
                        row.NextAttemptAt = recordedAt;
                        row.CompletedAt = recordedAt;
                        row.Message = null;
                        break;
                    default:
                        throw new UnreachableException(
                            $"Unknown public chat safe pre-send retry decision {decision.GetType().Name}."
                        );
                }

                row.ClaimToken = null;
                NotifyCompletion(row.Status);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private ValueTask<PublicChatClaimUpdate> CompleteClaimedRedacted(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            return CompleteClaimed(
                message,
                status,
                completedAt,
                static row => row.Message = null,
                cancellationToken
            );
        }

        private ValueTask<PublicChatClaimUpdate> CompleteClaimed(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            Action<Row> applyCase,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = status;
                row.CompletedAt = completedAt;
                applyCase(row);
                row.ClaimToken = null;
                NotifyCompletion(status);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private Row? Owned(PublicChatClaimedMessage message, RowStatus status)
        {
            return _rows.SingleOrDefault(row =>
                row.Id == message.Id && row.Status == status && row.ClaimToken == message.ClaimToken
            );
        }

        private void NotifyCompletion(RowStatus status)
        {
            if (!_completions.Writer.TryWrite(status))
            {
                throw new InvalidOperationException(
                    "The public chat outcome could not be observed."
                );
            }
        }

        private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
        {
            return left >= right ? left : right;
        }

        private sealed class Row(
            long id,
            string channel,
            PublicChatOutboxItem item,
            DateTimeOffset enqueuedAt
        )
        {
            public long Id { get; } = id;

            public string Channel { get; } = channel;

            public PublicChatOutboxItem Item { get; } = item;

            public string? Message { get; set; } = item.Message;

            public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;

            public DateTimeOffset ExpiresAt { get; } = enqueuedAt.AddMinutes(1);

            public DateTimeOffset NextAttemptAt { get; set; } = enqueuedAt;

            public RowStatus Status { get; set; }

            public int AttemptCount { get; set; }

            public int SafePreSendFailureCount { get; set; }

            public PublicChatClaimToken? ClaimToken { get; set; }

            public DateTimeOffset ClaimExpiresAt { get; set; }

            public DateTimeOffset? CompletedAt { get; set; }

            public PublicChatClaimedMessage Claimed(PublicChatClaimToken token)
            {
                return new()
                {
                    Id = Id,
                    Channel = Channel,
                    Message = Message!,
                    EnqueuedAt = EnqueuedAt,
                    ExpiresAt = ExpiresAt,
                    Attempt = AttemptCount + 1,
                    ClaimToken = token,
                    ClaimExpiresAt = ClaimExpiresAt,
                    DeduplicationKey = Item.DeduplicationKey,
                };
            }
        }

        private sealed record Candidate(Row Row, DateTimeOffset EligibleAt);

        private sealed record Delivery(
            PublicChatDeduplicationKey DeduplicationKey,
            DateTimeOffset CompletedAt
        );

        internal sealed record OutboxSnapshot
        {
            internal required RowStatus Status { get; init; }

            internal required int AttemptCount { get; init; }

            internal required int SafePreSendFailureCount { get; init; }

            internal required DateTimeOffset NextAttemptAt { get; init; }

            internal required string? Message { get; init; }
        }

        internal enum RowStatus
        {
            Pending,
            Claimed,
            Sending,
            SentAndDeleted,
            SafePreSendTransient,
            SafePreSendExhausted,
            MissingChannel,
            MissingBot,
            Rejected,
            Ambiguous,
            Unexpected,
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(string Message, Exception? Exception);

    private sealed class ManualTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly Channel<ManualTimer> _timerRegistrations =
            Channel.CreateUnbounded<ManualTimer>();

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _currentNowLocked;
            }
        }

        public override long GetTimestamp()
        {
            return GetUtcNow().UtcTicks;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _currentNowLocked = _currentNowLocked.Add(delta);
                due = _timers.Where(timer => timer.IsDue(_currentNowLocked)).ToList();
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        public async ValueTask WaitForTimerRegistrationAsync()
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_timers.Count > 0)
                    {
                        return;
                    }
                }

                _ = await _timerRegistrations.Reader.ReadAsync();
            }
        }

        public async ValueTask WaitForTimerAtAsync(DateTimeOffset dueAt)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_timers.Any(timer => timer.IsScheduledAt(dueAt)))
                    {
                        return;
                    }
                }

                _ = await _timerRegistrations.Reader.ReadAsync();
            }
        }

        private void AddTimer(ManualTimer timer)
        {
            lock (_gate)
            {
                if (!_timers.Contains(timer))
                {
                    _timers.Add(timer);
                }

                if (!_timerRegistrations.Writer.TryWrite(timer))
                {
                    throw new InvalidOperationException(
                        "The timer observer could not be notified."
                    );
                }
            }
        }

        private void RemoveTimer(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private DateTimeOffset _currentNowLocked { get; set; } = initialNow;

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan _period;
            private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _period = period;
                    _dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._currentNowLocked.Add(dueTime);
                    owner.AddTimer(this);
                }

                if (dueTime != Timeout.InfiniteTimeSpan && dueTime <= TimeSpan.Zero)
                {
                    Fire();
                }

                return true;
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    owner.RemoveTimer(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(DateTimeOffset value)
            {
                lock (owner._gate)
                {
                    return !_disposed && _dueAt <= value;
                }
            }

            public bool IsScheduledAt(DateTimeOffset value)
            {
                lock (owner._gate)
                {
                    return !_disposed && _dueAt == value;
                }
            }

            public void Fire()
            {
                lock (owner._gate)
                {
                    if (_disposed || _dueAt > owner._currentNowLocked)
                    {
                        return;
                    }

                    if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
                    {
                        _dueAt = owner._currentNowLocked.Add(_period);
                    }
                    else
                    {
                        _disposed = true;
                        owner.RemoveTimer(this);
                    }
                }

                callback(state);
            }
        }
    }
}

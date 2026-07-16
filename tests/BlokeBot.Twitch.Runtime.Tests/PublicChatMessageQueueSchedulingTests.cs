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

public sealed class PublicChatMessageQueueSchedulingTests : PublicChatMessageQueueTestBase
{
    [Test]
    public async Task DuplicateAndDistinctMessages_Processing_DelaysOnlyDuplicate()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
}

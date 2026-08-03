using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatMessageQueueSchedulingTests : PublicChatMessageQueueTestBase
{
    [Test]
    public async Task AcceptedEnqueue_WaitingForFutureAvailability_WakesWorkerBeforeTimer()
    {
        var now = Utc(12, 0, 0);
        var clock = new ManualTimeProvider(now);
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(
            new PublicChatClaimOutcome.AwaitingAvailability(now.AddSeconds(1)),
            new PublicChatClaimOutcome.Claimed(Claimed("message"))
        );
        var transport = new RecordingTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport, clock);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        await clock.WaitForTimerAtAsync(now.AddSeconds(1));

        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);

        (await transport.ReadAsync()).Message.ShouldBe("message");
        _ = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        clock.GetUtcNow().ShouldBe(now);
        outbox
            .ClaimCalls.Take(2)
            .Select(static call => call.Outcome.GetType())
            .ShouldBe([
                typeof(PublicChatClaimOutcome.AwaitingAvailability),
                typeof(PublicChatClaimOutcome.Claimed),
            ]);
    }

    [Test]
    public async Task ClaimContention_Processing_RetriesAndDelivers()
    {
        var now = Utc(12, 0, 0);
        var clock = new ManualTimeProvider(now);
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(
            new PublicChatClaimOutcome.Contended(),
            new PublicChatClaimOutcome.Claimed(Claimed("message"))
        );
        var transport = SuccessfulScriptedTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport, clock);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        await clock.WaitForTimerAtAsync(now.AddMilliseconds(25));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        _ = recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Sent>();
        outbox
            .ClaimCalls.Take(2)
            .Select(static call => call.Outcome.GetType())
            .ShouldBe([
                typeof(PublicChatClaimOutcome.Contended),
                typeof(PublicChatClaimOutcome.Claimed),
            ]);
    }

    [Test]
    public async Task BeginSendContention_Processing_RetriesAndDelivers()
    {
        var now = Utc(12, 0, 0);
        var clock = new ManualTimeProvider(now);
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
        outbox.ScriptBeginSend(
            new PublicChatClaimUpdate.Contended(),
            new PublicChatClaimUpdate.Applied()
        );
        var transport = SuccessfulScriptedTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport, clock);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await outbox.ReadBeginSendAsync();
        await clock.WaitForTimerAtAsync(now.AddMilliseconds(25));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        _ = recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Sent>();
        outbox
            .BeginSendCalls.Select(static call => call.Update.GetType())
            .ShouldBe([
                typeof(PublicChatClaimUpdate.Contended),
                typeof(PublicChatClaimUpdate.Applied),
            ]);
        transport.SendCount.ShouldBe(1);
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
        firstIncidents.Select(static alert => alert.Channel).ShouldBe(["first", "second"]);
        monitor.CaptureAlerts(pending, now, TimeSpan.FromSeconds(5), enabled: true).ShouldBeEmpty();

        monitor.ResetDrainedChannels([new("second", now)]);
        var nextFirstIncident = monitor.CaptureAlerts(
            [new("first", now.AddSeconds(-5)), new("second", now.AddSeconds(-5))],
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        nextFirstIncident.Select(static alert => alert.Channel).ShouldBe(["first"]);
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
        var now = Utc(12, 0, 5);
        var clock = new ManualTimeProvider(now);
        var outbox = new ScriptedOutbox();
        outbox.ScriptOutstanding([new PublicChatPendingMessage("channel", now.AddSeconds(-5))]);
        outbox.ScriptClaims(
            new PublicChatClaimOutcome.Claimed(Claimed("second secret chat payload"))
        );
        var transport = new RecordingTransport();
        var logger = new RecordingLogger<PublicChatMessageQueue>();
        var queue = CreateQueue(
            new BotOptions
            {
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

        (await transport.ReadAsync()).Message.ShouldBe("second secret chat payload");
        _ = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        logger.Entries.ShouldNotBeEmpty();
        foreach (var entry in logger.Entries)
        {
            entry.Exception.ShouldBeNull();
            entry.Message.ShouldContain("Continuing queued chat processing");
            entry.Message.ShouldNotContain("observer secret payload");
            entry.Message.ShouldNotContain("reporter secret payload");
            entry.Message.ShouldNotContain("second secret chat payload");
        }
    }
}

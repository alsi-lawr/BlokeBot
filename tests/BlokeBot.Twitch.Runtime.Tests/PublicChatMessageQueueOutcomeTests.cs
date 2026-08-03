using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatMessageQueueOutcomeTests : PublicChatMessageQueueTestBase
{
    [Test]
    public Task MissingChannelIdentity_Processing_IsRedactedTerminalWithoutRetryOrSend() =>
        AssertMissingIdentityAsync(
            new PublicChatPreparationOutcome.MissingChannel(),
            typeof(PublicChatDeliveryOutcome.MissingChannel),
            nameof(PublicChatPreparationOutcome.MissingChannel)
        );

    [Test]
    public Task MissingBotIdentity_Processing_IsRedactedTerminalWithoutRetryOrSend() =>
        AssertMissingIdentityAsync(
            new PublicChatPreparationOutcome.MissingBot(),
            typeof(PublicChatDeliveryOutcome.MissingBot),
            nameof(PublicChatPreparationOutcome.MissingBot)
        );

    [Test]
    public async Task ExplicitRejection_Processing_RecordsTerminalAfterSendBoundary()
    {
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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

        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        _ = recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Rejection>();
        outbox.BeginSendCalls.Count.ShouldBe(1);
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task PostBoundaryTransientFailure_Processing_IsAmbiguousWithoutRetry()
    {
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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

        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        _ = recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Ambiguous>();
        outbox.BeginSendCalls.Count.ShouldBe(1);
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task CallerCanceledDuringPreparation_Processing_ReleasesWithNonCanceledToken()
    {
        using var stopping = new CancellationTokenSource();
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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

        await queue.RunAsync(stopping.Token);

        var released = outbox.ReleaseCalls.ShouldHaveSingleItem();
        released.CancellationToken.ShouldBe(CancellationToken.None);
        outbox.BeginSendCalls.ShouldBeEmpty();
        outbox.RecordDeliveryCalls.ShouldBeEmpty();
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task CallerCanceledAfterSendBoundary_Processing_RecordsInterruptionBeforeStopping()
    {
        using var stopping = new CancellationTokenSource();
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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

        await queue.RunAsync(stopping.Token);

        var beginSend = outbox.BeginSendCalls.ShouldHaveSingleItem();
        var interrupted = outbox.RecordInterruptionCalls.ShouldHaveSingleItem();
        interrupted.CancellationToken.ShouldBe(CancellationToken.None);
        interrupted.Sequence.ShouldBeGreaterThan(beginSend.Sequence);
        outbox.RecordDeliveryCalls.ShouldBeEmpty();
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task SafePreparationFailure_MonitoringOutstandingBacklog_StillRaisesAlert()
    {
        var now = Utc(12, 0, 0);
        var pending = new PublicChatPendingMessage("channel", now);
        var clock = new ManualTimeProvider(now);
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
        outbox.ScriptOutstanding([pending], [pending], [pending]);
        var observer = new RecordingQueueAlertObserver();
        var transport = new ScriptedTransport(
            static (_, cancellationToken) =>
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
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        var recorded = await outbox.ReadRecordDeliveryAsync();

        await clock.WaitForTimerAtAsync(now.AddSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        var alert = await observer.ReadAsync();
        await StopAsync(stopping, worker);

        _ = recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.SafePreSendTransient>();
        alert.Channel.ShouldBe("channel");
        alert.PendingCount.ShouldBe(1);
        alert.OldestPendingAge.ShouldBe(TimeSpan.FromSeconds(5));
    }
}

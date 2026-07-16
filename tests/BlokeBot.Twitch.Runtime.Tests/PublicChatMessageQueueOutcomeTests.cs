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

public sealed class PublicChatMessageQueueOutcomeTests : PublicChatMessageQueueTestBase
{
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
}

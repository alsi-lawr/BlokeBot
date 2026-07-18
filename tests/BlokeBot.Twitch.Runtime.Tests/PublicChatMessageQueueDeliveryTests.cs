using BlokeBot.Twitch.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatMessageQueueDeliveryTests : PublicChatMessageQueueTestBase
{
    [Test]
    public async Task PersistenceFailure_Enqueueing_IsObservableWithoutDelivery()
    {
        var outbox = new ScriptedOutbox
        {
            Enqueue = static (_, _) =>
                ValueTask.FromException<PublicChatEnqueueOutcome>(
                    new IOException("Persistence unavailable.")
                ),
        };
        var transport = new RecordingTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport);

        await Should.ThrowAsync<IOException>(() =>
            queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None).AsTask()
        );

        outbox.EnqueueCalls.Count.ShouldBe(1);
        outbox.ClaimCalls.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCanceledAfterCommit_Enqueueing_LeavesDeliveryRecoverable()
    {
        using var caller = new CancellationTokenSource();
        var outbox = new ScriptedOutbox
        {
            Enqueue = (_, _) =>
            {
                caller.Cancel();
                return ValueTask.FromResult<PublicChatEnqueueOutcome>(Accepted());
            },
        };
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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

        outbox
            .RecordDeliveryCalls.ShouldHaveSingleItem()
            .Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Sent>();
    }

    [Test]
    public async Task SentResult_Processing_RecordsOutcomeAfterOneSendAttempt()
    {
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
        var transport = SuccessfulScriptedTransport();
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Sent>();
        recorded.CancellationToken.ShouldBe(CancellationToken.None);
        outbox.BeginSendCalls.Count.ShouldBe(1);
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(1);
    }

    [Test]
    public async Task SafePreparationFailure_Processing_RecordsRetryableOutcomeWithoutSending()
    {
        var failure = new IOException("secret preparation detail");
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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
        var queue = CreateQueue(new BotOptions(), outbox, transport);
        _ = await queue.EnqueueAsync(Command("channel", "message"), CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.SafePreSendTransient>();
        recorded.CancellationToken.ShouldBe(CancellationToken.None);
        outbox.BeginSendCalls.ShouldBeEmpty();
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task UnexpectedPreparationFailure_Processing_RecordsTerminalWithoutSending()
    {
        var failure = new InvalidOperationException("secret preparation detail");
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("message")));
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

        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        recorded.Outcome.ShouldBeOfType<PublicChatDeliveryOutcome.Unexpected>();
        outbox.BeginSendCalls.ShouldBeEmpty();
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task UnexpectedPreparationFailure_Reporting_UsesOnlyRedactedStructuredContext()
    {
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("secret chat payload")));
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

        _ = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("Unexpected");
        entry.Message.ShouldContain(typeof(InvalidOperationException).FullName!);
        entry.Message.ShouldNotContain("secret provider response");
        entry.Message.ShouldNotContain("secret chat payload");
    }
}

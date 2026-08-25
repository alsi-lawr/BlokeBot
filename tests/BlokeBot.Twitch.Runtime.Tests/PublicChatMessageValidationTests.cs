using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatMessageValidationTests : PublicChatMessageQueueTestBase
{
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
    public async Task ValidMessage_Sending_ReturnsAcceptedAfterDurableEnqueue()
    {
        var outbox = new ScriptedOutbox();
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

        _ = outcome.ShouldBeOfType<PublicChatSendOutcome.Accepted>();
        var enqueued = outbox.EnqueueCalls.ShouldHaveSingleItem();
        enqueued.Batch.Deadline.ShouldBeSameAs(deadline);
        enqueued.Batch.Items.ShouldHaveSingleItem().Message.ShouldBe("message");
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task InfrastructureFailure_Sending_PreservesOriginalException()
    {
        var failure = new IOException("private persistence detail");
        var outbox = new ScriptedOutbox
        {
            Enqueue = (_, _) => ValueTask.FromException<PublicChatEnqueueOutcome>(failure),
        };
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
        outbox.EnqueueCalls.Count.ShouldBe(1);
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCancellation_Sending_PropagatesWithoutWriteOrDelivery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var outbox = new ScriptedOutbox();
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
        outbox.EnqueueCalls.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCanceledAfterCommit_Sending_ReturnsAcceptedDurableOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        var outbox = new ScriptedOutbox
        {
            Enqueue = (_, _) =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult<PublicChatEnqueueOutcome>(Accepted());
            },
        };
        var transport = new RecordingTransport();
        var sender = new PublicChatMessageSender(CreateQueue(new BotOptions(), outbox, transport));

        var outcome = await sender.SendAsync(
            "channel",
            "message",
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellation.Token
        );

        _ = outcome.ShouldBeOfType<PublicChatSendOutcome.Accepted>();
        cancellation.IsCancellationRequested.ShouldBeTrue();
        outbox.EnqueueCalls.Count.ShouldBe(1);
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task MessageOverLength_Enqueueing_WritesEveryPartInOneBatch()
    {
        var outbox = new ScriptedOutbox();
        var transport = new RecordingTransport();
        var queue = CreateQueue(new BotOptions { MaxChatMessageLength = 10 }, outbox, transport);

        var receipt = await queue.EnqueueAsync(
            Command("channel", "alpha beta gamma"),
            CancellationToken.None
        );

        receipt
            .ShouldBeOfType<PublicChatEnqueueOutcome.Accepted>()
            .Receipt.MessageIds.Length.ShouldBe(2);
        outbox
            .EnqueueCalls.ShouldHaveSingleItem()
            .Batch.Items.Select(static item => item.Message)
            .ShouldBe(["alpha", "beta gamma"]);
        transport.Deliveries.ShouldBeEmpty();
    }
}

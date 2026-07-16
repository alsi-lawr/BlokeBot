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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy) { EnqueueFailure = failure };
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy) { AfterEnqueue = cancellation.Cancel };
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
}

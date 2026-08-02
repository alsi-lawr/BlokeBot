using System.Collections.Immutable;
using BlokeBot.Eventing;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class PublicChatMessageQueueTestBase
{
    private protected static PublicChatMessageQueue CreateQueue(
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
    ) =>
        new(
            BotSettings.FromOptions(options),
            timeProvider ?? TimeProvider.System,
            new PublicChatQueueBacklogMonitor(),
            new PublicChatQueueAlertDispatcher(observers ?? [], fanOut ?? QueueAlertFanOut()),
            outbox,
            transport,
            logger ?? NullLogger<PublicChatMessageQueue>.Instance
        );

    private protected static async Task AssertMissingIdentityAsync(
        PublicChatPreparationOutcome outcome,
        Type expectedOutcomeType,
        string expectedDiagnostic
    )
    {
        var outbox = new ScriptedOutbox();
        outbox.ScriptClaims(new PublicChatClaimOutcome.Claimed(Claimed("secret chat payload")));
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

        var recorded = await outbox.ReadRecordDeliveryAsync();
        await StopAsync(stopping, worker);

        recorded.Outcome.GetType().ShouldBe(expectedOutcomeType);
        recorded.CancellationToken.ShouldBe(CancellationToken.None);
        outbox.BeginSendCalls.ShouldBeEmpty();
        transport.PrepareCount.ShouldBe(1);
        transport.SendCount.ShouldBe(0);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain(expectedDiagnostic);
        entry.Message.ShouldNotContain("private-channel-login");
        entry.Message.ShouldNotContain("secret chat payload");
    }

    private protected static PublicChatEnqueueCommand Command(string channel, string message) =>
        new()
        {
            Channel = channel,
            Message = message,
            Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
        };

    private protected static PublicChatEnqueueOutcome.Accepted Accepted(int messageCount = 1) =>
        new(
            new PublicChatOutboxReceipt(
                Enumerable.Range(1, messageCount).Select(Convert.ToInt64).ToImmutableArray()
            )
        );

    private protected static PublicChatClaimedMessage Claimed(
        string message,
        string channel = "channel",
        long id = 1
    )
    {
        var enqueuedAt = Utc(12, 0, 0);
        return new()
        {
            Id = id,
            Channel = channel,
            Message = message,
            EnqueuedAt = enqueuedAt,
            ExpiresAt = enqueuedAt.AddMinutes(1),
            Attempt = 1,
            ClaimToken = new PublicChatClaimToken(
                Guid.Parse("10000000-0000-0000-0000-000000000001")
            ),
            ClaimExpiresAt = enqueuedAt.AddMinutes(5),
            DeduplicationKey = PublicChatMessageDeduplication.Key(channel, message),
        };
    }

    private protected static PublicChatPreparedSend Prepared(PublicChatClaimedMessage message) =>
        new()
        {
            Message = message,
            AppAccessToken = "app-token",
            BroadcasterId = "broadcaster-id",
            BotUserId = "bot-user-id",
        };

    private protected static ValueTask<PublicChatPreparationOutcome> Ready(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<PublicChatPreparationOutcome>(
            new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
        );
    }

    private protected static ScriptedTransport SuccessfulScriptedTransport() =>
        new(
            Ready,
            static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<PublicChatTransportSendResult>(
                    new PublicChatTransportSendResult.Sent()
                );
            }
        );

    private protected static ObserverFanOut<
        PublicChatQueueAlertObserverBoundary,
        PublicChatQueueBacklog,
        PublicChatQueueAlertDeadLetter
    > QueueAlertFanOut() =>
        RuntimeTestObserverFanOut.Continue<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >(BotObserverBoundaries.PublicChatQueueAlerts);

    private protected static async Task StopAsync(CancellationTokenSource stopping, Task worker)
    {
        await stopping.CancelAsync();
        await worker;
    }

    private protected static DateTimeOffset Utc(int hour, int minute, int second) =>
        new(2026, 7, 12, hour, minute, second, TimeSpan.Zero);
}

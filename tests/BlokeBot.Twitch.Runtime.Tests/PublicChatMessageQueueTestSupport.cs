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

public abstract partial class PublicChatMessageQueueTestBase
{
    private protected static readonly PublicChatRetryPolicy StandardRetryPolicy = new()
    {
        AttemptLimit = 3,
        Delay = TimeSpan.FromSeconds(1),
        MaximumDelay = TimeSpan.FromSeconds(30),
        DelayBackoffType = DelayBackoffType.Exponential,
    };

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

    private protected static async Task AssertMissingIdentityAsync(
        PublicChatPreparationOutcome outcome,
        InMemoryOutbox.RowStatus expectedStatus,
        string expectedDiagnostic
    )
    {
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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

    private protected static PublicChatEnqueueCommand Command(string channel, string message)
    {
        return new()
        {
            Channel = channel,
            Message = message,
            Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
        };
    }

    private protected static PublicChatPreparedSend Prepared(PublicChatClaimedMessage message)
    {
        return new()
        {
            Message = message,
            AppAccessToken = "app-token",
            BroadcasterId = "broadcaster-id",
            BotUserId = "bot-user-id",
        };
    }

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

    private protected static ScriptedTransport SuccessfulScriptedTransport()
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

    private protected static ObserverFanOut<
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

    private protected static async Task StopAsync(CancellationTokenSource stopping, Task worker)
    {
        await stopping.CancelAsync();
        await worker;
    }

    private protected static DateTimeOffset Utc(int hour, int minute, int second)
    {
        return new(2026, 7, 12, hour, minute, second, TimeSpan.Zero);
    }
}

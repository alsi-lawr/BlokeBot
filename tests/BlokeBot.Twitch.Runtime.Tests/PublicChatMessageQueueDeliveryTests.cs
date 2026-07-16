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

public sealed class PublicChatMessageQueueDeliveryTests : PublicChatMessageQueueTestBase
{
    [Test]
    public async Task PersistenceFailure_Enqueueing_IsObservableWithoutDelivery()
    {
        var outbox = new InMemoryOutbox(StandardRetryPolicy)
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy) { AfterEnqueue = caller.Cancel };
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
        var outbox = new InMemoryOutbox(StandardRetryPolicy);
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
}

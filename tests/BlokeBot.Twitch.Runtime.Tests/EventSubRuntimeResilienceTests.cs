using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubRuntimeResilienceTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task ReconciliationFault_OutsideShutdown_KeepsTheRuntimeRunning()
    {
        using var stopping = new CancellationTokenSource();
        var channels = new EmptyChannelProvider();
        var transport = new FailingListTransport();
        var idleWait = new CancelAfterIdleWait(stopping, 3);
        var runtime = new EventSubRuntime(
            BotSettings.FromOptions(new BotOptions { Runtime = ChatRuntime.EventSub }),
            channels,
            CreateSessionFactory(),
            new EventSubChannelReconciliationTrigger(channels),
            transport,
            idleWait,
            NullLogger<EventSubRuntime>.Instance
        );

        await runtime.RunAsync(stopping.Token);

        idleWait.CallCount.ShouldBe(3);
        transport.ListAttempts.ShouldBe(3);
    }

    private static EventSubChannelSessionFactory CreateSessionFactory()
    {
        var clock = new FixedTimeProvider(Now);
        var attemptBuilder = new ResiliencePipelineBuilder { TimeProvider = clock };
        var recoveryBuilder = new ResiliencePipelineBuilder<EventSubChannelReconciliationOutcome>
        {
            TimeProvider = clock,
        };
        var policy = new EventSubChannelRecoveryPolicy
        {
            AttemptLimit = 1,
            Delay = TimeSpan.Zero,
            MaximumDelay = TimeSpan.Zero,
            DelayBackoffType = DelayBackoffType.Constant,
            AttemptTimeout = TimeSpan.FromMinutes(1),
        };
        EventSubChannelRecoveryResilience.ConfigureAttempt(attemptBuilder, policy);
        EventSubChannelRecoveryResilience.Configure(recoveryBuilder, policy);
        return new EventSubChannelSessionFactory(
            new ScriptedChannelOperations(),
            new EventSubChannelRecoveryPipeline(attemptBuilder.Build(), recoveryBuilder.Build()),
            new EventSubSubscriptionReconciliationStore(),
            new EventSubChannelStatusStore(),
            new BotRuntimeStatusStore(),
            new RecordingDiagnostics(),
            clock
        );
    }

    private sealed class EmptyChannelProvider : IBotChannelProvider
    {
        public ValueTask<IReadOnlyList<string>> GetChannelsAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FailingListTransport : IEventSubSubscriptionTransport
    {
        internal int ListAttempts { get; private set; }

        public Task ResetAsync(string clientId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlySet<string>> ListEnabledOwnedIdsAsync(
            string clientId,
            CancellationToken cancellationToken
        )
        {
            ListAttempts++;
            throw new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout."
            );
        }

        public Task<string> CreateAsync(
            string clientId,
            EventSubSubscriptionRequest subscription,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task DeleteAsync(
            string clientId,
            string subscriptionId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class CancelAfterIdleWait(CancellationTokenSource stopping, int cycles)
        : IRuntimeIdleWait
    {
        internal int CallCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount >= cycles)
            {
                stopping.Cancel();
            }

            return ValueTask.CompletedTask;
        }
    }
}

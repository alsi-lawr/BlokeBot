using Polly;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class BotRuntimeHostedServiceTests
{
    [Test]
    public async Task IrcConfigured_RunningSelectedRuntime_RunsOnlyIrcAndPropagatesCancellation()
    {
        using var stopping = new CancellationTokenSource();
        using var harness = CreateHarness(ChatRuntime.Irc, stopping);

        await harness.Service.RunSelectedRuntimeAsync(stopping.Token);

        harness.IrcSession.CallCount.ShouldBe(1);
        stopping.IsCancellationRequested.ShouldBeTrue();
        harness.IrcSession.ReceivedCancellationToken.IsCancellationRequested.ShouldBeTrue();
        harness.Health.Reports.ShouldBeEmpty();
        harness.IdleWait.CallCount.ShouldBe(0);
    }

    private static RuntimeHarness CreateHarness(
        ChatRuntime runtime,
        CancellationTokenSource stopping
    )
    {
        var ircSession = new CancelingConnectionSession(stopping);
        var health = new RecordingHealthReporter();
        var status = new BotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var irc = new IrcRuntime(
            ircSession,
            new IrcSessionResiliencePipeline(new ResiliencePipelineBuilder().Build()),
            health,
            status,
            idleWait
        );
        var eventSub = new EventSubRuntime(null!, null!, null!, null!, null!, null!);
        return new RuntimeHarness(
            new BotRuntimeHostedService(
                BotSettings.FromOptions(new BotOptions { Runtime = runtime }),
                irc,
                eventSub
            ),
            ircSession,
            health,
            idleWait
        );
    }

    private sealed class RuntimeHarness(
        BotRuntimeHostedService service,
        CancelingConnectionSession ircSession,
        RecordingHealthReporter health,
        RecordingIdleWait idleWait
    ) : IDisposable
    {
        internal BotRuntimeHostedService Service { get; } = service;

        internal CancelingConnectionSession IrcSession { get; } = ircSession;

        internal RecordingHealthReporter Health { get; } = health;

        internal RecordingIdleWait IdleWait { get; } = idleWait;

        public void Dispose() => Service.Dispose();
    }

    private sealed class CancelingConnectionSession(CancellationTokenSource stopping)
        : IIrcConnectionSession
    {
        internal int CallCount { get; private set; }

        internal CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<RuntimeSessionEstablishment> EstablishAsync(
            RuntimeConnectionTarget target,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            ReceivedCancellationToken = cancellationToken;
            stopping.Cancel();
            return Task.FromCanceled<RuntimeSessionEstablishment>(cancellationToken);
        }
    }

    private sealed class RecordingHealthReporter : IRuntimeSessionHealthReporter
    {
        internal List<RuntimeSessionHealthReport> Reports { get; } = [];

        public void Report(RuntimeSessionHealthReport report) => Reports.Add(report);
    }

    private sealed class RecordingIdleWait : IRuntimeIdleWait
    {
        internal int CallCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }
}

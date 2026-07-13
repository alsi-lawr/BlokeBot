using Polly;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotRuntimeHostedServiceTests
{
    [Test]
    public async Task IrcConfigured_RunningSelectedRuntime_RunsOnlyIrcAndPropagatesCancellation()
    {
        using var stopping = new CancellationTokenSource();
        using var harness = CreateHarness(TwitchBotRuntime.Irc, stopping);

        await harness.Service.RunSelectedRuntimeAsync(stopping.Token);

        harness.IrcSession.CallCount.ShouldBe(1);
        harness.EventSubSession.CallCount.ShouldBe(0);
        stopping.IsCancellationRequested.ShouldBeTrue();
        harness.IrcSession.ReceivedCancellationToken.IsCancellationRequested.ShouldBeTrue();
        harness.Health.Reports.ShouldBeEmpty();
        harness.IdleWait.CallCount.ShouldBe(0);
    }

    [Test]
    public async Task EventSubConfigured_RunningSelectedRuntime_RunsOnlyEventSubAndPropagatesCancellation()
    {
        using var stopping = new CancellationTokenSource();
        using var harness = CreateHarness(TwitchBotRuntime.EventSub, stopping);

        await harness.Service.RunSelectedRuntimeAsync(stopping.Token);

        harness.IrcSession.CallCount.ShouldBe(0);
        harness.EventSubSession.CallCount.ShouldBe(1);
        stopping.IsCancellationRequested.ShouldBeTrue();
        harness.EventSubSession.ReceivedCancellationToken.IsCancellationRequested.ShouldBeTrue();
        harness.Health.Reports.ShouldBeEmpty();
        harness.IdleWait.CallCount.ShouldBe(0);
    }

    private static RuntimeHarness CreateHarness(
        TwitchBotRuntime runtime,
        CancellationTokenSource stopping
    )
    {
        var ircSession = new CancelingConnectionSession(stopping);
        var eventSubSession = new CancelingConnectionSession(stopping);
        var health = new RecordingHealthReporter();
        var status = new TwitchBotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var irc = new TwitchIrcRuntime(
            ircSession,
            new TwitchIrcSessionResiliencePipeline(new ResiliencePipelineBuilder().Build()),
            health,
            status,
            idleWait
        );
        var eventSub = new EventSubRuntime(
            eventSubSession,
            new EventSubSessionResiliencePipeline(new ResiliencePipelineBuilder().Build()),
            health,
            status,
            idleWait
        );
        return new RuntimeHarness(
            new TwitchBotRuntimeHostedService(
                TwitchBotSettings.FromOptions(new TwitchBotOptions { Runtime = runtime }),
                irc,
                eventSub
            ),
            ircSession,
            eventSubSession,
            health,
            idleWait
        );
    }

    private sealed class RuntimeHarness(
        TwitchBotRuntimeHostedService service,
        CancelingConnectionSession ircSession,
        CancelingConnectionSession eventSubSession,
        RecordingHealthReporter health,
        RecordingIdleWait idleWait
    ) : IDisposable
    {
        internal TwitchBotRuntimeHostedService Service { get; } = service;

        internal CancelingConnectionSession IrcSession { get; } = ircSession;

        internal CancelingConnectionSession EventSubSession { get; } = eventSubSession;

        internal RecordingHealthReporter Health { get; } = health;

        internal RecordingIdleWait IdleWait { get; } = idleWait;

        public void Dispose()
        {
            Service.Dispose();
        }
    }

    private sealed class CancelingConnectionSession(CancellationTokenSource stopping)
        : ITwitchIrcConnectionSession,
            IEventSubConnectionSession
    {
        internal int CallCount { get; private set; }

        internal CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<TwitchRuntimeSessionEstablishment> EstablishAsync(
            TwitchRuntimeConnectionTarget target,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            ReceivedCancellationToken = cancellationToken;
            stopping.Cancel();
            return Task.FromCanceled<TwitchRuntimeSessionEstablishment>(cancellationToken);
        }
    }

    private sealed class RecordingHealthReporter : ITwitchRuntimeSessionHealthReporter
    {
        internal List<TwitchRuntimeSessionHealthReport> Reports { get; } = [];

        public void Report(TwitchRuntimeSessionHealthReport report)
        {
            Reports.Add(report);
        }
    }

    private sealed class RecordingIdleWait : ITwitchRuntimeIdleWait
    {
        internal int CallCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }
}

using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class RuntimeSessionProtocolHandoffTests : RuntimeSessionResilienceTestBase
{
    [Test]
    public async Task EventSubProtocolReconnect_RunningRuntime_EstablishesRequestedTargetThroughPipeline()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateEventSubProtocolHarness(attemptLimit: 3);
        var reconnectEndpoint = new Uri("wss://example.test/reconnect");
        var firstListening = new ScriptedEstablishedSession();
        firstListening.Enqueue(_ =>
            Task.FromResult(
                new RuntimeReconnectRequest
                {
                    Target = new RuntimeConnectionTarget.EventSubReconnect
                    {
                        Uri = reconnectEndpoint,
                    },
                }
            )
        );
        var secondListening = new ScriptedEstablishedSession();
        secondListening.Enqueue(listeningToken =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<RuntimeReconnectRequest>(listeningToken);
        });
        harness.Session.Enqueue((_, _) => EstablishedAsync(firstListening));
        harness.Session.Enqueue(
            (target, _) =>
            {
                target
                    .ShouldBeOfType<RuntimeConnectionTarget.EventSubReconnect>()
                    .Uri.ShouldBe(reconnectEndpoint);
                return EstablishedAsync(secondListening);
            }
        );

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(2);
        firstListening.DisposeCount.ShouldBe(1);
        secondListening.DisposeCount.ShouldBe(1);
        _ = harness.Session.Targets[0].ShouldBeOfType<RuntimeConnectionTarget.Initial>();
        _ = harness.Session.Targets[1].ShouldBeOfType<RuntimeConnectionTarget.EventSubReconnect>();
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    public async Task EventSubProtocolHandoff_OldCleanupFailsAfterReplacementEstablishment_DisposesReplacementAndReportsBothFailures()
    {
        const int PreviousAttempt = 3;
        const int ReplacementAttempt = 2;
        var reconnectEndpoint = new Uri("wss://example.test/reconnect");
        var previousCleanupFailure = new IOException("previous session cleanup failed");
        var replacementCleanupFailure = new IOException("replacement session cleanup failed");
        var previousSession = new ScriptedEstablishedSession
        {
            DisposeException = previousCleanupFailure,
        };
        previousSession.Enqueue(_ =>
            Task.FromResult(
                new RuntimeReconnectRequest
                {
                    Target = new RuntimeConnectionTarget.EventSubReconnect
                    {
                        Uri = reconnectEndpoint,
                    },
                }
            )
        );
        var replacementSession = new ScriptedEstablishedSession
        {
            DisposeException = replacementCleanupFailure,
        };
        var outcomes = new Queue<RuntimeSessionOutcome>([
            new RuntimeSessionOutcome.Established
            {
                Session = previousSession,
                Attempt = PreviousAttempt,
            },
            new RuntimeSessionOutcome.Established
            {
                Session = replacementSession,
                Attempt = ReplacementAttempt,
            },
        ]);
        var health = new RecordingHealthReporter();
        var status = new BotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var targets = new List<RuntimeConnectionTarget>();

        await RuntimeSessionRunner.RunUntilStoppedAsync(
            ChatRuntime.EventSub,
            new RuntimeConnectionTarget.Initial(),
            (target, _) =>
            {
                targets.Add(target);
                status.MarkConnected(["channel"]);
                return Task.FromResult(outcomes.Dequeue());
            },
            EventSubSessionFailureClassifier.Classify,
            health,
            status,
            idleWait,
            CancellationToken.None
        );

        targets.Count.ShouldBe(2);
        _ = targets[0].ShouldBeOfType<RuntimeConnectionTarget.Initial>();
        targets[1]
            .ShouldBeOfType<RuntimeConnectionTarget.EventSubReconnect>()
            .Uri.ShouldBe(reconnectEndpoint);
        previousSession.ListenCount.ShouldBe(1);
        previousSession.DisposeCount.ShouldBe(1);
        replacementSession.ListenCount.ShouldBe(0);
        replacementSession.DisposeCount.ShouldBe(1);
        _ = status.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();
        var report = health
            .Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>();
        report.Runtime.ShouldBe(ChatRuntime.EventSub);
        report.Classification.ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        report.Attempt.ShouldBe(PreviousAttempt);
        var cleanup = report.Exception.ShouldBeOfType<RuntimeSessionCleanupException>();
        cleanup.Attempt.ShouldBe(PreviousAttempt);
        var combined = cleanup.InnerException.ShouldBeOfType<AggregateException>();
        var previousCleanup = combined
            .InnerExceptions[0]
            .ShouldBeOfType<RuntimeSessionCleanupException>();
        previousCleanup.Attempt.ShouldBe(PreviousAttempt);
        previousCleanup.InnerException.ShouldBeSameAs(previousCleanupFailure);
        var replacementCleanup = combined
            .InnerExceptions[1]
            .ShouldBeOfType<RuntimeSessionCleanupException>();
        replacementCleanup.Attempt.ShouldBe(ReplacementAttempt);
        replacementCleanup.InnerException.ShouldBeSameAs(replacementCleanupFailure);
    }

    [Test]
    public async Task EventSubProtocolHandoff_FollowedByIdle_ResetsExpiredTargetBeforeRecheck()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateEventSubProtocolHarness(attemptLimit: 3);
        var reconnectEndpoint = new Uri("wss://example.test/expired-reconnect");
        var previousSession = new ScriptedEstablishedSession();
        previousSession.Enqueue(_ =>
            Task.FromResult(
                new RuntimeReconnectRequest
                {
                    Target = new RuntimeConnectionTarget.EventSubReconnect
                    {
                        Uri = reconnectEndpoint,
                    },
                }
            )
        );
        harness.Session.Enqueue((_, _) => EstablishedAsync(previousSession));
        harness.Session.Enqueue(
            (target, _) =>
            {
                target
                    .ShouldBeOfType<RuntimeConnectionTarget.EventSubReconnect>()
                    .Uri.ShouldBe(reconnectEndpoint);
                return IdleAsync();
            }
        );
        harness.Session.Enqueue(
            (target, attemptToken) =>
            {
                _ = target.ShouldBeOfType<RuntimeConnectionTarget.Initial>();
                cancellation.Cancel();
                return Task.FromCanceled<RuntimeSessionEstablishment>(attemptToken);
            }
        );

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(3);
        previousSession.DisposeCount.ShouldBe(1);
        harness.IdleWait.CallCount.ShouldBe(1);
        _ = harness.Session.Targets[2].ShouldBeOfType<RuntimeConnectionTarget.Initial>();
        harness.Health.Reports.ShouldBeEmpty();
    }
}

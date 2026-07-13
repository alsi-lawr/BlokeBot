using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class RuntimeSessionResilienceTests
{
    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task FirstEstablishment_Succeeding_ReturnsEstablishedWithoutFailureReport(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var listening = new ScriptedEstablishedSession();
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.SetConnected(true, ["channel"]);
                return EstablishedAsync(listening);
            }
        );

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var established = outcome.ShouldBeOfType<RuntimeSessionOutcome.Established>();
        established.Attempt.ShouldBe(1);
        established.Session.ShouldBeSameAs(listening);
        harness.Session.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
        harness.Status.Current.IsConnected.ShouldBeTrue();
        harness.Status.Current.ConnectedChannels.ShouldBe(["channel"]);
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task TransientFailureThenEstablishment_RunningPipeline_RetriesAndResetsStatus(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new IOException("transport unavailable");
        var listening = new ScriptedEstablishedSession();
        var connectedTransitions = new List<bool>();
        harness.Status.Changed += () =>
            connectedTransitions.Add(harness.Status.Current.IsConnected);
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.SetConnected(true, ["stale"]);
                return FailedEstablishmentAsync(failure);
            }
        );
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.SetConnected(true, ["fresh"]);
                return EstablishedAsync(listening);
            }
        );

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var established = outcome.ShouldBeOfType<RuntimeSessionOutcome.Established>();
        established.Attempt.ShouldBe(2);
        harness.Session.CallCount.ShouldBe(2);
        connectedTransitions.ShouldBe([true, false, true]);
        harness.Status.Current.ConnectedChannels.ShouldBe(["fresh"]);
        AssertReport(
            harness
                .Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 1,
            failure
        );
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task TerminalEstablishmentFailure_RunningPipeline_ReportsUnhealthyWithoutRetry(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new AccessTokenUnavailableException(
            AccessTokenUnavailableReason.MissingRefreshToken,
            AccessTokenUnavailableException.MissingRefreshTokenMessage
        );
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.SetConnected(true, ["stale"]);
                return FailedEstablishmentAsync(failure);
            }
        );

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var unhealthy = outcome.ShouldBeOfType<RuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Status.Current.IsAuthorized.ShouldBeFalse();
        harness.Status.Current.IsConnected.ShouldBeFalse();
        var report = harness
            .Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>();
        unhealthy.Report.ShouldBeSameAs(report);
        AssertReport(
            report,
            runtime,
            RuntimeSessionFailureClassification.Terminal,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task UnexpectedEstablishmentFailure_RunningPipeline_ReportsUnhealthyWithoutRetry(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new ApplicationException("unexpected runtime defect");
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(failure));

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        outcome.ShouldBeOfType<RuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        AssertReport(
            harness
                .Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            RuntimeSessionFailureClassification.Unexpected,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task TimeoutThenEstablishment_RunningPipeline_RetriesThroughDirectTimeoutHook(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 2);
        var failure = new TimeoutRejectedException("establishment timed out");
        var listening = new ScriptedEstablishedSession();
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(failure));
        harness.Session.Enqueue((_, _) => EstablishedAsync(listening));

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var established = outcome.ShouldBeOfType<RuntimeSessionOutcome.Established>();
        established.Attempt.ShouldBe(2);
        harness.Session.CallCount.ShouldBe(2);
        AssertReport(
            harness
                .Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Timeout,
            attempt: 1,
            failure
        );
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task TransientEstablishmentFailures_ExhaustingAttempts_ReportBoundedUnhealthy(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var first = new IOException("first transport failure");
        var second = new IOException("second transport failure");
        var final = new IOException("final transport failure");
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(first));
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(second));
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(final));

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var unhealthy = outcome.ShouldBeOfType<RuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(3);
        harness.Health.Reports.Count.ShouldBe(3);
        AssertReport(
            harness.Health.Reports[0].ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 1,
            first
        );
        AssertReport(
            harness.Health.Reports[1].ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 2,
            second
        );
        var finalReport = harness
            .Health.Reports[2]
            .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>();
        unhealthy.Report.ShouldBeSameAs(finalReport);
        AssertReport(
            finalReport,
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 3,
            final
        );
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task SingleAttemptPolicy_TransientFailure_DoesNotAddCompatibilityRetry(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 1);
        var failure = new IOException("only establishment attempt failed");
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(failure));

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        outcome.ShouldBeOfType<RuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        AssertReport(
            harness
                .Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task CallerCancellation_DuringEstablishment_StopsWithoutFailureReport(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue(
            (_, attemptToken) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<RuntimeSessionEstablishment>(attemptToken);
            }
        );

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            cancellation.Token
        );

        outcome.ShouldBeOfType<RuntimeSessionOutcome.Canceled>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task IdleEstablishment_RunningRuntime_WaitsOutsideRetryThenRechecks(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue((_, _) => IdleAsync());
        harness.Session.Enqueue(
            (_, attemptToken) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<RuntimeSessionEstablishment>(attemptToken);
            }
        );

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(2);
        harness.IdleWait.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task EstablishedSession_Listening_UsesHostLifetimeTokenNotAttemptTimeout(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var listening = new ScriptedEstablishedSession();
        listening.Enqueue(listeningToken =>
        {
            listeningToken.ShouldBe(cancellation.Token);
            cancellation.Cancel();
            return Task.FromCanceled<RuntimeReconnectRequest>(listeningToken);
        });
        harness.Session.Enqueue((_, _) => EstablishedAsync(listening));

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(1);
        listening.ListenCount.ShouldBe(1);
        listening.DisposeCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task SuccessfulEstablishment_AfterDisconnect_ResetsConsecutiveAttemptBudget(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var firstEstablishmentFailure = new IOException("cycle one failure");
        var disconnect = new IOException("established session disconnected");
        var secondCycleFirstFailure = new IOException("cycle two first failure");
        var secondCycleSecondFailure = new IOException("cycle two second failure");
        var firstListening = new ScriptedEstablishedSession();
        firstListening.Enqueue(_ => FailedListeningAsync(disconnect));
        var secondListening = new ScriptedEstablishedSession();
        secondListening.Enqueue(listeningToken =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<RuntimeReconnectRequest>(listeningToken);
        });
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(firstEstablishmentFailure));
        harness.Session.Enqueue((_, _) => EstablishedAsync(firstListening));
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(secondCycleFirstFailure));
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(secondCycleSecondFailure));
        harness.Session.Enqueue((_, _) => EstablishedAsync(secondListening));

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(5);
        firstListening.DisposeCount.ShouldBe(1);
        secondListening.DisposeCount.ShouldBe(1);
        harness.Health.Reports.Count.ShouldBe(4);
        AssertReport(
            harness.Health.Reports[0].ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 1,
            firstEstablishmentFailure
        );
        AssertReport(
            harness
                .Health.Reports[1]
                .ShouldBeOfType<RuntimeSessionHealthReport.ReconnectScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 2,
            disconnect
        );
        AssertReport(
            harness.Health.Reports[2].ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 1,
            secondCycleFirstFailure
        );
        AssertReport(
            harness.Health.Reports[3].ShouldBeOfType<RuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            RuntimeSessionFailureClassification.Transient,
            attempt: 2,
            secondCycleSecondFailure
        );
    }

    [Test]
    public async Task EventSubProtocolReconnect_RunningRuntime_EstablishesRequestedTargetThroughPipeline()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(ChatRuntime.EventSub, attemptLimit: 3);
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
        harness.Session.Targets[0].ShouldBeOfType<RuntimeConnectionTarget.Initial>();
        harness.Session.Targets[1].ShouldBeOfType<RuntimeConnectionTarget.EventSubReconnect>();
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
                status.SetConnected(true, ["channel"]);
                return Task.FromResult(outcomes.Dequeue());
            },
            EventSubSessionFailureClassifier.Classify,
            health,
            status,
            idleWait,
            CancellationToken.None
        );

        targets.Count.ShouldBe(2);
        targets[0].ShouldBeOfType<RuntimeConnectionTarget.Initial>();
        targets[1]
            .ShouldBeOfType<RuntimeConnectionTarget.EventSubReconnect>()
            .Uri.ShouldBe(reconnectEndpoint);
        previousSession.ListenCount.ShouldBe(1);
        previousSession.DisposeCount.ShouldBe(1);
        replacementSession.ListenCount.ShouldBe(0);
        replacementSession.DisposeCount.ShouldBe(1);
        status.Current.IsConnected.ShouldBeFalse();
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
        var harness = CreateHarness(ChatRuntime.EventSub, attemptLimit: 3);
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
                target.ShouldBeOfType<RuntimeConnectionTarget.Initial>();
                cancellation.Cancel();
                return Task.FromCanceled<RuntimeSessionEstablishment>(attemptToken);
            }
        );

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(3);
        previousSession.DisposeCount.ShouldBe(1);
        harness.IdleWait.CallCount.ShouldBe(1);
        harness.Session.Targets[2].ShouldBeOfType<RuntimeConnectionTarget.Initial>();
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task TerminalListeningFailure_RunningRuntime_ReportsUnhealthyWithoutReconnect(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new InvalidOperationException("terminal protocol failure");
        var listening = new ScriptedEstablishedSession();
        listening.Enqueue(_ => FailedListeningAsync(failure));
        harness.Session.Enqueue((_, _) => EstablishedAsync(listening));

        await harness.RunRuntimeAsync(CancellationToken.None);

        harness.Session.CallCount.ShouldBe(1);
        AssertReport(
            harness
                .Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            RuntimeSessionFailureClassification.Terminal,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task UnexpectedListeningFailure_RunningRuntime_ReportsUnhealthyWithoutReconnect(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new ApplicationException("unexpected listening defect");
        var listening = new ScriptedEstablishedSession();
        listening.Enqueue(_ => FailedListeningAsync(failure));
        harness.Session.Enqueue((_, _) => EstablishedAsync(listening));

        await harness.RunRuntimeAsync(CancellationToken.None);

        harness.Session.CallCount.ShouldBe(1);
        AssertReport(
            harness
                .Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            RuntimeSessionFailureClassification.Unexpected,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    [Arguments(ChatRuntime.EventSub)]
    public async Task ListeningAndCleanupFailure_RunningRuntime_ReportsCombinedUnhealthyWithoutHostFault(
        ChatRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var listeningFailure = new IOException("established session disconnected");
        var cleanupFailure = new IOException("session cleanup failed");
        var listening = new ScriptedEstablishedSession { DisposeException = cleanupFailure };
        listening.Enqueue(_ => FailedListeningAsync(listeningFailure));
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.SetConnected(true, ["channel"]);
                return EstablishedAsync(listening);
            }
        );

        await harness.RunRuntimeAsync(CancellationToken.None);

        harness.Session.CallCount.ShouldBe(1);
        listening.DisposeCount.ShouldBe(1);
        harness.Status.Current.IsConnected.ShouldBeFalse();
        var report = harness
            .Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<RuntimeSessionHealthReport.Unhealthy>();
        report.Runtime.ShouldBe(runtime);
        report.Classification.ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        report.Attempt.ShouldBe(1);
        var combined = report.Exception.ShouldBeOfType<AggregateException>();
        combined.InnerExceptions[0].ShouldBeSameAs(listeningFailure);
        var cleanup = combined.InnerExceptions[1].ShouldBeOfType<RuntimeSessionCleanupException>();
        cleanup.Attempt.ShouldBe(1);
        cleanup.InnerException.ShouldBeSameAs(cleanupFailure);
    }

    [Test]
    public void BoundaryClassifiers_ClassifyingHttpAndCancellation_UseExplicitCases()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellation = new OperationCanceledException(canceled.Token);
        var transientHttp = new HttpRequestException(
            "service unavailable",
            null,
            System.Net.HttpStatusCode.ServiceUnavailable
        );
        var terminalHttp = new HttpRequestException(
            "unauthorized",
            null,
            System.Net.HttpStatusCode.Unauthorized
        );

        IrcSessionFailureClassifier
            .Classify(cancellation, canceled.Token)
            .ShouldBe(RuntimeSessionFailureClassification.Cancellation);
        EventSubSessionFailureClassifier
            .Classify(cancellation, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        IrcSessionFailureClassifier
            .Classify(transientHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        EventSubSessionFailureClassifier
            .Classify(transientHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        IrcSessionFailureClassifier
            .Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Terminal);
        EventSubSessionFailureClassifier
            .Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Terminal);
    }

    [Test]
    public void BoundaryClassifiers_ClassifyingTransportAndProtocolFaults_UseBoundaryCases()
    {
        IrcSessionFailureClassifier
            .Classify(new SocketException((int)SocketError.ConnectionReset), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        EventSubSessionFailureClassifier
            .Classify(
                new WebSocketException(WebSocketError.ConnectionClosedPrematurely),
                CancellationToken.None
            )
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        IrcSessionFailureClassifier
            .Classify(new JsonException("invalid payload"), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Terminal);
        EventSubSessionFailureClassifier
            .Classify(new TimeoutException("establishment timeout"), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Timeout);
    }

    [Test]
    public void StructuredHealthReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string Secret = "oauth:do-not-log";
        var logger = new RecordingLogger<RuntimeSessionHealthLogger>();
        var health = new RuntimeSessionHealthLogger(logger);

        health.Report(
            new RuntimeSessionHealthReport.Unhealthy
            {
                Runtime = ChatRuntime.Irc,
                Classification = RuntimeSessionFailureClassification.Unexpected,
                Attempt = 2,
                Exception = new ApplicationException(Secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(Secret);
        entry.Properties["Runtime"].ShouldBe(ChatRuntime.Irc);
        entry.Properties["Classification"].ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        entry.Properties["Attempt"].ShouldBe(2);
        entry.Properties["FailureType"].ShouldBe(typeof(ApplicationException).FullName);
    }

    [Test]
    public void StructuredReconnectReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string Secret = "oauth:do-not-log";
        var logger = new RecordingLogger<RuntimeSessionHealthLogger>();
        var health = new RuntimeSessionHealthLogger(logger);

        health.Report(
            new RuntimeSessionHealthReport.ReconnectScheduled
            {
                Runtime = ChatRuntime.EventSub,
                Classification = RuntimeSessionFailureClassification.Transient,
                Attempt = 3,
                Exception = new IOException(Secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(Secret);
        entry.Properties["Runtime"].ShouldBe(ChatRuntime.EventSub);
        entry.Properties["Classification"].ShouldBe(RuntimeSessionFailureClassification.Transient);
        entry.Properties["Attempt"].ShouldBe(3);
        entry.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
    }

    private static Task<RuntimeSessionEstablishment> IdleAsync()
    {
        return Task.FromResult<RuntimeSessionEstablishment>(new RuntimeSessionEstablishment.Idle());
    }

    private static Task<RuntimeSessionEstablishment> EstablishedAsync(
        ScriptedEstablishedSession session
    )
    {
        return Task.FromResult<RuntimeSessionEstablishment>(
            new RuntimeSessionEstablishment.Established { Session = session }
        );
    }

    private static Task<RuntimeSessionEstablishment> FailedEstablishmentAsync(Exception exception)
    {
        return Task.FromException<RuntimeSessionEstablishment>(exception);
    }

    private static Task<RuntimeReconnectRequest> FailedListeningAsync(Exception exception)
    {
        return Task.FromException<RuntimeReconnectRequest>(exception);
    }

    private static void AssertReport(
        RuntimeSessionHealthReport report,
        ChatRuntime runtime,
        RuntimeSessionFailureClassification classification,
        int attempt,
        Exception exception
    )
    {
        report.Runtime.ShouldBe(runtime);
        report.Classification.ShouldBe(classification);
        report.Attempt.ShouldBe(attempt);
        report.Exception.ShouldBeSameAs(exception);
    }

    private static RuntimeHarness CreateHarness(ChatRuntime runtime, int attemptLimit)
    {
        var session = new ScriptedConnectionSession();
        var health = new RecordingHealthReporter();
        var status = new BotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var builder = new ResiliencePipelineBuilder();
        switch (runtime)
        {
            case ChatRuntime.Irc:
                RuntimeSessionResilience.ConfigureIrc(
                    builder,
                    new IrcSessionResiliencePolicy
                    {
                        AttemptLimit = attemptLimit,
                        Delay = TimeSpan.Zero,
                        MaximumDelay = TimeSpan.FromTicks(1),
                        DelayBackoffType = DelayBackoffType.Constant,
                        AttemptTimeout = TimeSpan.FromMinutes(1),
                    },
                    health
                );
                var irc = new IrcRuntime(
                    session,
                    new IrcSessionResiliencePipeline(builder.Build()),
                    health,
                    status,
                    idleWait
                );
                return new RuntimeHarness(
                    session,
                    health,
                    status,
                    idleWait,
                    irc.EstablishSessionAsync,
                    irc.RunAsync
                );
            case ChatRuntime.EventSub:
                RuntimeSessionResilience.ConfigureEventSub(
                    builder,
                    new EventSubSessionResiliencePolicy
                    {
                        AttemptLimit = attemptLimit,
                        Delay = TimeSpan.Zero,
                        MaximumDelay = TimeSpan.FromTicks(1),
                        DelayBackoffType = DelayBackoffType.Constant,
                        AttemptTimeout = TimeSpan.FromMinutes(1),
                    },
                    health
                );
                var eventSub = new EventSubRuntime(
                    session,
                    new EventSubSessionResiliencePipeline(builder.Build()),
                    health,
                    status,
                    idleWait
                );
                return new RuntimeHarness(
                    session,
                    health,
                    status,
                    idleWait,
                    eventSub.EstablishSessionAsync,
                    eventSub.RunAsync
                );
            default:
                throw new UnreachableException($"Unknown Twitch runtime: {runtime}.");
        }
    }

    private sealed class RuntimeHarness(
        ScriptedConnectionSession session,
        RecordingHealthReporter health,
        BotRuntimeStatusStore status,
        RecordingIdleWait idleWait,
        Func<
            RuntimeConnectionTarget,
            CancellationToken,
            Task<RuntimeSessionOutcome>
        > establishSession,
        Func<CancellationToken, Task> runRuntime
    )
    {
        internal ScriptedConnectionSession Session { get; } = session;

        internal RecordingHealthReporter Health { get; } = health;

        internal BotRuntimeStatusStore Status { get; } = status;

        internal RecordingIdleWait IdleWait { get; } = idleWait;

        internal Task<RuntimeSessionOutcome> EstablishSessionAsync(
            RuntimeConnectionTarget target,
            CancellationToken cancellationToken
        )
        {
            return establishSession(target, cancellationToken);
        }

        internal Task RunRuntimeAsync(CancellationToken cancellationToken)
        {
            return runRuntime(cancellationToken);
        }
    }

    private sealed class ScriptedConnectionSession
        : IIrcConnectionSession,
            IEventSubConnectionSession
    {
        private readonly Queue<
            Func<RuntimeConnectionTarget, CancellationToken, Task<RuntimeSessionEstablishment>>
        > _operations = [];

        internal int CallCount { get; private set; }

        internal List<RuntimeConnectionTarget> Targets { get; } = [];

        internal void Enqueue(
            Func<
                RuntimeConnectionTarget,
                CancellationToken,
                Task<RuntimeSessionEstablishment>
            > operation
        )
        {
            _operations.Enqueue(operation);
        }

        public Task<RuntimeSessionEstablishment> EstablishAsync(
            RuntimeConnectionTarget target,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            Targets.Add(target);
            return _operations.Dequeue()(target, cancellationToken);
        }
    }

    private sealed class ScriptedEstablishedSession : IRuntimeEstablishedSession
    {
        private readonly Queue<Func<CancellationToken, Task<RuntimeReconnectRequest>>> _listeners =
        [];

        internal int ListenCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal Exception? DisposeException { get; init; }

        internal void Enqueue(Func<CancellationToken, Task<RuntimeReconnectRequest>> listener)
        {
            _listeners.Enqueue(listener);
        }

        public Task<RuntimeReconnectRequest> ListenAsync(CancellationToken cancellationToken)
        {
            ListenCount++;
            return _listeners.Dequeue()(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeException is { } exception
                ? new ValueTask(Task.FromException(exception))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHealthReporter : IRuntimeSessionHealthReporter
    {
        internal List<RuntimeSessionHealthReport> Reports { get; } = [];

        public void Report(RuntimeSessionHealthReport report)
        {
            Reports.Add(report);
        }
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return Scope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed class LogEntry(
        LogLevel level,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, object?> properties
    )
    {
        internal LogLevel Level { get; } = level;

        internal string Message { get; } = message;

        internal Exception? Exception { get; } = exception;

        internal IReadOnlyDictionary<string, object?> Properties { get; } = properties;
    }

    private sealed class Scope : IDisposable
    {
        internal static Scope Instance { get; } = new();

        public void Dispose() { }
    }
}

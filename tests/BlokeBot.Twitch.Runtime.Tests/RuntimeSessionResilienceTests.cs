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
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task FirstEstablishment_Succeeding_ReturnsEstablishedWithoutFailureReport(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var listening = new ScriptedEstablishedSession();
        harness.Session.Enqueue((_, _) =>
        {
            harness.Status.SetConnected(true, ["channel"]);
            return EstablishedAsync(listening);
        });

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var established = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Established>();
        established.Attempt.ShouldBe(1);
        established.Session.ShouldBeSameAs(listening);
        harness.Session.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
        harness.Status.Current.IsConnected.ShouldBeTrue();
        harness.Status.Current.ConnectedChannels.ShouldBe(["channel"]);
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TransientFailureThenEstablishment_RunningPipeline_RetriesAndResetsStatus(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new IOException("transport unavailable");
        var listening = new ScriptedEstablishedSession();
        var connectedTransitions = new List<bool>();
        harness.Status.Changed += () =>
            connectedTransitions.Add(harness.Status.Current.IsConnected);
        harness.Session.Enqueue((_, _) =>
        {
            harness.Status.SetConnected(true, ["stale"]);
            return FailedEstablishmentAsync(failure);
        });
        harness.Session.Enqueue((_, _) =>
        {
            harness.Status.SetConnected(true, ["fresh"]);
            return EstablishedAsync(listening);
        });

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var established = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Established>();
        established.Attempt.ShouldBe(2);
        harness.Session.CallCount.ShouldBe(2);
        connectedTransitions.ShouldBe([true, false, true]);
        harness.Status.Current.ConnectedChannels.ShouldBe(["fresh"]);
        AssertReport(
            harness.Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            failure
        );
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TerminalEstablishmentFailure_RunningPipeline_ReportsUnhealthyWithoutRetry(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new TwitchAccessTokenUnavailableException(
            TwitchAccessTokenUnavailableReason.MissingRefreshToken,
            TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
        );
        harness.Session.Enqueue((_, _) =>
        {
            harness.Status.SetConnected(true, ["stale"]);
            return FailedEstablishmentAsync(failure);
        });

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var unhealthy = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Status.Current.IsAuthorized.ShouldBeFalse();
        harness.Status.Current.IsConnected.ShouldBeFalse();
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        unhealthy.Report.ShouldBeSameAs(report);
        AssertReport(
            report,
            runtime,
            TwitchRuntimeSessionFailureClassification.Terminal,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task UnexpectedEstablishmentFailure_RunningPipeline_ReportsUnhealthyWithoutRetry(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new ApplicationException("unexpected runtime defect");
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(failure));

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        AssertReport(
            harness.Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Unexpected,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TimeoutThenEstablishment_RunningPipeline_RetriesThroughDirectTimeoutHook(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 2);
        var failure = new TimeoutRejectedException("establishment timed out");
        var listening = new ScriptedEstablishedSession();
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(failure));
        harness.Session.Enqueue((_, _) => EstablishedAsync(listening));

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var established = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Established>();
        established.Attempt.ShouldBe(2);
        harness.Session.CallCount.ShouldBe(2);
        AssertReport(
            harness.Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Timeout,
            attempt: 1,
            failure
        );
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TransientEstablishmentFailures_ExhaustingAttempts_ReportBoundedUnhealthy(
        TwitchBotRuntime runtime
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
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var unhealthy = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(3);
        harness.Health.Reports.Count.ShouldBe(3);
        AssertReport(
            harness.Health.Reports[0]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            first
        );
        AssertReport(
            harness.Health.Reports[1]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 2,
            second
        );
        var finalReport = harness.Health.Reports[2]
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        unhealthy.Report.ShouldBeSameAs(finalReport);
        AssertReport(
            finalReport,
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 3,
            final
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task SingleAttemptPolicy_TransientFailure_DoesNotAddCompatibilityRetry(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 1);
        var failure = new IOException("only establishment attempt failed");
        harness.Session.Enqueue((_, _) => FailedEstablishmentAsync(failure));

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        AssertReport(
            harness.Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task CallerCancellation_DuringEstablishment_StopsWithoutFailureReport(
        TwitchBotRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue((_, attemptToken) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<TwitchRuntimeSessionEstablishment>(attemptToken);
        });

        var outcome = await harness.EstablishSessionAsync(
            new TwitchRuntimeConnectionTarget.Initial(),
            cancellation.Token
        );

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Canceled>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task IdleEstablishment_RunningRuntime_WaitsOutsideRetryThenRechecks(
        TwitchBotRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue((_, _) => IdleAsync());
        harness.Session.Enqueue((_, attemptToken) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<TwitchRuntimeSessionEstablishment>(attemptToken);
        });

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(2);
        harness.IdleWait.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task EstablishedSession_Listening_UsesHostLifetimeTokenNotAttemptTimeout(
        TwitchBotRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var listening = new ScriptedEstablishedSession();
        listening.Enqueue(listeningToken =>
        {
            listeningToken.ShouldBe(cancellation.Token);
            cancellation.Cancel();
            return Task.FromCanceled<TwitchRuntimeReconnectRequest>(listeningToken);
        });
        harness.Session.Enqueue((_, _) => EstablishedAsync(listening));

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(1);
        listening.ListenCount.ShouldBe(1);
        listening.DisposeCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task SuccessfulEstablishment_AfterDisconnect_ResetsConsecutiveAttemptBudget(
        TwitchBotRuntime runtime
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
            return Task.FromCanceled<TwitchRuntimeReconnectRequest>(listeningToken);
        });
        harness.Session.Enqueue((_, _) =>
            FailedEstablishmentAsync(firstEstablishmentFailure)
        );
        harness.Session.Enqueue((_, _) => EstablishedAsync(firstListening));
        harness.Session.Enqueue((_, _) =>
            FailedEstablishmentAsync(secondCycleFirstFailure)
        );
        harness.Session.Enqueue((_, _) =>
            FailedEstablishmentAsync(secondCycleSecondFailure)
        );
        harness.Session.Enqueue((_, _) => EstablishedAsync(secondListening));

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(5);
        firstListening.DisposeCount.ShouldBe(1);
        secondListening.DisposeCount.ShouldBe(1);
        harness.Health.Reports.Count.ShouldBe(4);
        AssertReport(
            harness.Health.Reports[0]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            firstEstablishmentFailure
        );
        AssertReport(
            harness.Health.Reports[1]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.ReconnectScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 2,
            disconnect
        );
        AssertReport(
            harness.Health.Reports[2]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            secondCycleFirstFailure
        );
        AssertReport(
            harness.Health.Reports[3]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 2,
            secondCycleSecondFailure
        );
    }

    [Test]
    public async Task EventSubProtocolReconnect_RunningRuntime_EstablishesRequestedTargetThroughPipeline()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(TwitchBotRuntime.EventSub, attemptLimit: 3);
        var reconnectEndpoint = new Uri("wss://example.test/reconnect");
        var firstListening = new ScriptedEstablishedSession();
        firstListening.Enqueue(_ =>
            Task.FromResult(
                new TwitchRuntimeReconnectRequest
                {
                    Target = new TwitchRuntimeConnectionTarget.EventSubReconnect
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
            return Task.FromCanceled<TwitchRuntimeReconnectRequest>(listeningToken);
        });
        harness.Session.Enqueue((_, _) => EstablishedAsync(firstListening));
        harness.Session.Enqueue((target, _) =>
        {
            target
                .ShouldBeOfType<TwitchRuntimeConnectionTarget.EventSubReconnect>()
                .Uri.ShouldBe(reconnectEndpoint);
            return EstablishedAsync(secondListening);
        });

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(2);
        firstListening.DisposeCount.ShouldBe(1);
        secondListening.DisposeCount.ShouldBe(1);
        harness.Session.Targets[0].ShouldBeOfType<TwitchRuntimeConnectionTarget.Initial>();
        harness
            .Session.Targets[1]
            .ShouldBeOfType<TwitchRuntimeConnectionTarget.EventSubReconnect>();
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    public async Task EventSubProtocolHandoff_OldCleanupFailsAfterReplacementEstablishment_DisposesReplacementAndReportsBothFailures()
    {
        const int previousAttempt = 3;
        const int replacementAttempt = 2;
        var reconnectEndpoint = new Uri("wss://example.test/reconnect");
        var previousCleanupFailure = new IOException("previous session cleanup failed");
        var replacementCleanupFailure = new IOException(
            "replacement session cleanup failed"
        );
        var previousSession = new ScriptedEstablishedSession
        {
            DisposeException = previousCleanupFailure,
        };
        previousSession.Enqueue(_ =>
            Task.FromResult(
                new TwitchRuntimeReconnectRequest
                {
                    Target = new TwitchRuntimeConnectionTarget.EventSubReconnect
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
        var outcomes = new Queue<TwitchRuntimeSessionOutcome>(
            [
                new TwitchRuntimeSessionOutcome.Established
                {
                    Session = previousSession,
                    Attempt = previousAttempt,
                },
                new TwitchRuntimeSessionOutcome.Established
                {
                    Session = replacementSession,
                    Attempt = replacementAttempt,
                },
            ]
        );
        var health = new RecordingHealthReporter();
        var status = new TwitchBotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var targets = new List<TwitchRuntimeConnectionTarget>();

        await TwitchRuntimeSessionRunner.RunUntilStoppedAsync(
            TwitchBotRuntime.EventSub,
            new TwitchRuntimeConnectionTarget.Initial(),
            (target, _) =>
            {
                targets.Add(target);
                status.SetConnected(true, ["channel"]);
                return Task.FromResult(outcomes.Dequeue());
            },
            TwitchEventSubSessionFailureClassifier.Classify,
            health,
            status,
            idleWait,
            CancellationToken.None
        );

        targets.Count.ShouldBe(2);
        targets[0].ShouldBeOfType<TwitchRuntimeConnectionTarget.Initial>();
        targets[1]
            .ShouldBeOfType<TwitchRuntimeConnectionTarget.EventSubReconnect>()
            .Uri.ShouldBe(reconnectEndpoint);
        previousSession.ListenCount.ShouldBe(1);
        previousSession.DisposeCount.ShouldBe(1);
        replacementSession.ListenCount.ShouldBe(0);
        replacementSession.DisposeCount.ShouldBe(1);
        status.Current.IsConnected.ShouldBeFalse();
        var report = health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        report.Runtime.ShouldBe(TwitchBotRuntime.EventSub);
        report.Classification.ShouldBe(
            TwitchRuntimeSessionFailureClassification.Unexpected
        );
        report.Attempt.ShouldBe(previousAttempt);
        var cleanup = report.Exception
            .ShouldBeOfType<TwitchRuntimeSessionCleanupException>();
        cleanup.Attempt.ShouldBe(previousAttempt);
        var combined = cleanup.InnerException.ShouldBeOfType<AggregateException>();
        var previousCleanup = combined.InnerExceptions[0]
            .ShouldBeOfType<TwitchRuntimeSessionCleanupException>();
        previousCleanup.Attempt.ShouldBe(previousAttempt);
        previousCleanup.InnerException.ShouldBeSameAs(previousCleanupFailure);
        var replacementCleanup = combined.InnerExceptions[1]
            .ShouldBeOfType<TwitchRuntimeSessionCleanupException>();
        replacementCleanup.Attempt.ShouldBe(replacementAttempt);
        replacementCleanup.InnerException.ShouldBeSameAs(replacementCleanupFailure);
    }

    [Test]
    public async Task EventSubProtocolHandoff_FollowedByIdle_ResetsExpiredTargetBeforeRecheck()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(TwitchBotRuntime.EventSub, attemptLimit: 3);
        var reconnectEndpoint = new Uri("wss://example.test/expired-reconnect");
        var previousSession = new ScriptedEstablishedSession();
        previousSession.Enqueue(_ =>
            Task.FromResult(
                new TwitchRuntimeReconnectRequest
                {
                    Target = new TwitchRuntimeConnectionTarget.EventSubReconnect
                    {
                        Uri = reconnectEndpoint,
                    },
                }
            )
        );
        harness.Session.Enqueue((_, _) => EstablishedAsync(previousSession));
        harness.Session.Enqueue((target, _) =>
        {
            target
                .ShouldBeOfType<TwitchRuntimeConnectionTarget.EventSubReconnect>()
                .Uri.ShouldBe(reconnectEndpoint);
            return IdleAsync();
        });
        harness.Session.Enqueue((target, attemptToken) =>
        {
            target.ShouldBeOfType<TwitchRuntimeConnectionTarget.Initial>();
            cancellation.Cancel();
            return Task.FromCanceled<TwitchRuntimeSessionEstablishment>(attemptToken);
        });

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(3);
        previousSession.DisposeCount.ShouldBe(1);
        harness.IdleWait.CallCount.ShouldBe(1);
        harness.Session.Targets[2].ShouldBeOfType<TwitchRuntimeConnectionTarget.Initial>();
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TerminalListeningFailure_RunningRuntime_ReportsUnhealthyWithoutReconnect(
        TwitchBotRuntime runtime
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
            harness.Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Terminal,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task UnexpectedListeningFailure_RunningRuntime_ReportsUnhealthyWithoutReconnect(
        TwitchBotRuntime runtime
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
            harness.Health.Reports.ShouldHaveSingleItem()
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Unexpected,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task ListeningAndCleanupFailure_RunningRuntime_ReportsCombinedUnhealthyWithoutHostFault(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var listeningFailure = new IOException("established session disconnected");
        var cleanupFailure = new IOException("session cleanup failed");
        var listening = new ScriptedEstablishedSession
        {
            DisposeException = cleanupFailure,
        };
        listening.Enqueue(_ => FailedListeningAsync(listeningFailure));
        harness.Session.Enqueue((_, _) =>
        {
            harness.Status.SetConnected(true, ["channel"]);
            return EstablishedAsync(listening);
        });

        await harness.RunRuntimeAsync(CancellationToken.None);

        harness.Session.CallCount.ShouldBe(1);
        listening.DisposeCount.ShouldBe(1);
        harness.Status.Current.IsConnected.ShouldBeFalse();
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        report.Runtime.ShouldBe(runtime);
        report.Classification.ShouldBe(
            TwitchRuntimeSessionFailureClassification.Unexpected
        );
        report.Attempt.ShouldBe(1);
        var combined = report.Exception.ShouldBeOfType<AggregateException>();
        combined.InnerExceptions[0].ShouldBeSameAs(listeningFailure);
        var cleanup = combined.InnerExceptions[1]
            .ShouldBeOfType<TwitchRuntimeSessionCleanupException>();
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

        TwitchIrcSessionFailureClassifier.Classify(cancellation, canceled.Token)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Cancellation);
        TwitchEventSubSessionFailureClassifier.Classify(cancellation, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Unexpected);
        TwitchIrcSessionFailureClassifier.Classify(transientHttp, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchEventSubSessionFailureClassifier.Classify(
                transientHttp,
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchIrcSessionFailureClassifier.Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Terminal);
        TwitchEventSubSessionFailureClassifier.Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Terminal);
    }

    [Test]
    public void BoundaryClassifiers_ClassifyingTransportAndProtocolFaults_UseBoundaryCases()
    {
        TwitchIrcSessionFailureClassifier.Classify(
                new SocketException((int)SocketError.ConnectionReset),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchEventSubSessionFailureClassifier.Classify(
                new WebSocketException(WebSocketError.ConnectionClosedPrematurely),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchIrcSessionFailureClassifier.Classify(
                new JsonException("invalid payload"),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Terminal);
        TwitchEventSubSessionFailureClassifier.Classify(
                new TimeoutException("establishment timeout"),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Timeout);
    }

    [Test]
    public void StructuredHealthReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string secret = "oauth:do-not-log";
        var logger = new RecordingLogger<TwitchRuntimeSessionHealthLogger>();
        var health = new TwitchRuntimeSessionHealthLogger(logger);

        health.Report(
            new TwitchRuntimeSessionHealthReport.Unhealthy
            {
                Runtime = TwitchBotRuntime.Irc,
                Classification = TwitchRuntimeSessionFailureClassification.Unexpected,
                Attempt = 2,
                Exception = new ApplicationException(secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(secret);
        entry.Properties["Runtime"].ShouldBe(TwitchBotRuntime.Irc);
        entry.Properties["Classification"].ShouldBe(
            TwitchRuntimeSessionFailureClassification.Unexpected
        );
        entry.Properties["Attempt"].ShouldBe(2);
        entry.Properties["FailureType"].ShouldBe(typeof(ApplicationException).FullName);
    }

    [Test]
    public void StructuredReconnectReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string secret = "oauth:do-not-log";
        var logger = new RecordingLogger<TwitchRuntimeSessionHealthLogger>();
        var health = new TwitchRuntimeSessionHealthLogger(logger);

        health.Report(
            new TwitchRuntimeSessionHealthReport.ReconnectScheduled
            {
                Runtime = TwitchBotRuntime.EventSub,
                Classification = TwitchRuntimeSessionFailureClassification.Transient,
                Attempt = 3,
                Exception = new IOException(secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(secret);
        entry.Properties["Runtime"].ShouldBe(TwitchBotRuntime.EventSub);
        entry.Properties["Classification"].ShouldBe(
            TwitchRuntimeSessionFailureClassification.Transient
        );
        entry.Properties["Attempt"].ShouldBe(3);
        entry.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
    }

    private static Task<TwitchRuntimeSessionEstablishment> IdleAsync() =>
        Task.FromResult<TwitchRuntimeSessionEstablishment>(
            new TwitchRuntimeSessionEstablishment.Idle()
        );

    private static Task<TwitchRuntimeSessionEstablishment> EstablishedAsync(
        ScriptedEstablishedSession session
    ) =>
        Task.FromResult<TwitchRuntimeSessionEstablishment>(
            new TwitchRuntimeSessionEstablishment.Established { Session = session }
        );

    private static Task<TwitchRuntimeSessionEstablishment> FailedEstablishmentAsync(
        Exception exception
    ) => Task.FromException<TwitchRuntimeSessionEstablishment>(exception);

    private static Task<TwitchRuntimeReconnectRequest> FailedListeningAsync(
        Exception exception
    ) => Task.FromException<TwitchRuntimeReconnectRequest>(exception);

    private static void AssertReport(
        TwitchRuntimeSessionHealthReport report,
        TwitchBotRuntime runtime,
        TwitchRuntimeSessionFailureClassification classification,
        int attempt,
        Exception exception
    )
    {
        report.Runtime.ShouldBe(runtime);
        report.Classification.ShouldBe(classification);
        report.Attempt.ShouldBe(attempt);
        report.Exception.ShouldBeSameAs(exception);
    }

    private static RuntimeHarness CreateHarness(
        TwitchBotRuntime runtime,
        int attemptLimit
    )
    {
        var session = new ScriptedConnectionSession();
        var health = new RecordingHealthReporter();
        var status = new TwitchBotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var builder = new ResiliencePipelineBuilder();
        switch (runtime)
        {
            case TwitchBotRuntime.Irc:
                TwitchRuntimeSessionResilience.ConfigureIrc(
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
                var irc = new TwitchIrcRuntime(
                    session,
                    new TwitchIrcSessionResiliencePipeline(builder.Build()),
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
            case TwitchBotRuntime.EventSub:
                TwitchRuntimeSessionResilience.ConfigureEventSub(
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
                var eventSub = new TwitchEventSubRuntime(
                    session,
                    new TwitchEventSubSessionResiliencePipeline(builder.Build()),
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
        TwitchBotRuntimeStatusStore status,
        RecordingIdleWait idleWait,
        Func<
            TwitchRuntimeConnectionTarget,
            CancellationToken,
            Task<TwitchRuntimeSessionOutcome>
        > establishSession,
        Func<CancellationToken, Task> runRuntime
    )
    {
        internal ScriptedConnectionSession Session { get; } = session;

        internal RecordingHealthReporter Health { get; } = health;

        internal TwitchBotRuntimeStatusStore Status { get; } = status;

        internal RecordingIdleWait IdleWait { get; } = idleWait;

        internal Task<TwitchRuntimeSessionOutcome> EstablishSessionAsync(
            TwitchRuntimeConnectionTarget target,
            CancellationToken cancellationToken
        ) => establishSession(target, cancellationToken);

        internal Task RunRuntimeAsync(CancellationToken cancellationToken) =>
            runRuntime(cancellationToken);
    }

    private sealed class ScriptedConnectionSession
        : ITwitchIrcConnectionSession,
            ITwitchEventSubConnectionSession
    {
        private readonly Queue<
            Func<
                TwitchRuntimeConnectionTarget,
                CancellationToken,
                Task<TwitchRuntimeSessionEstablishment>
            >
        > operations = [];

        internal int CallCount { get; private set; }

        internal List<TwitchRuntimeConnectionTarget> Targets { get; } = [];

        internal void Enqueue(
            Func<
                TwitchRuntimeConnectionTarget,
                CancellationToken,
                Task<TwitchRuntimeSessionEstablishment>
            > operation
        ) => operations.Enqueue(operation);

        public Task<TwitchRuntimeSessionEstablishment> EstablishAsync(
            TwitchRuntimeConnectionTarget target,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            Targets.Add(target);
            return operations.Dequeue()(target, cancellationToken);
        }
    }

    private sealed class ScriptedEstablishedSession : ITwitchRuntimeEstablishedSession
    {
        private readonly Queue<
            Func<CancellationToken, Task<TwitchRuntimeReconnectRequest>>
        > listeners = [];

        internal int ListenCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal Exception? DisposeException { get; init; }

        internal void Enqueue(
            Func<CancellationToken, Task<TwitchRuntimeReconnectRequest>> listener
        ) => listeners.Enqueue(listener);

        public Task<TwitchRuntimeReconnectRequest> ListenAsync(
            CancellationToken cancellationToken
        )
        {
            ListenCount++;
            return listeners.Dequeue()(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeException is { } exception
                ? new ValueTask(Task.FromException(exception))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHealthReporter : ITwitchRuntimeSessionHealthReporter
    {
        internal List<TwitchRuntimeSessionHealthReport> Reports { get; } = [];

        public void Report(TwitchRuntimeSessionHealthReport report) => Reports.Add(report);
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => Scope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

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
            Entries.Add(
                new LogEntry(logLevel, formatter(state, exception), exception, properties)
            );
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

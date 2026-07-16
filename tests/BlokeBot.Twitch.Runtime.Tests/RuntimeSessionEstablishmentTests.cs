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

public sealed class RuntimeSessionEstablishmentTests : RuntimeSessionResilienceTestBase
{
    [Test]
    [Arguments(ChatRuntime.Irc)]
    public async Task FirstEstablishment_Succeeding_ReturnsEstablishedWithoutFailureReport(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
        var listening = new ScriptedEstablishedSession();
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.MarkConnected(["channel"]);
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
        harness
            .Status.Current.ShouldBeOfType<BotRuntimeStatus.Connected>()
            .Channels.ShouldBe(["channel"]);
        await established.Session.DisposeAsync();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    public async Task TransientFailureThenEstablishment_RunningPipeline_RetriesAndResetsStatus(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
        var failure = new IOException("transport unavailable");
        var listening = new ScriptedEstablishedSession();
        var connectedTransitions = new List<bool>();
        harness.Status.Changed += () =>
            connectedTransitions.Add(IsConnected(harness.Status.Current));
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.MarkConnected(["stale"]);
                return FailedEstablishmentAsync(failure);
            }
        );
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.MarkConnected(["fresh"]);
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
        harness
            .Status.Current.ShouldBeOfType<BotRuntimeStatus.Connected>()
            .Channels.ShouldBe(["fresh"]);
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
    public async Task TokenUnavailable_DuringEstablishment_ReturnsTypedOutcomeWithoutRetry(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.MarkConnected(["stale"]);
                return Task.FromResult<RuntimeSessionEstablishment>(
                    new RuntimeSessionEstablishment.TokenUnavailable(
                        AccessTokenUnavailableReason.MissingRefreshToken
                    )
                );
            }
        );

        var outcome = await harness.EstablishSessionAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );

        var unavailable = outcome.ShouldBeOfType<RuntimeSessionOutcome.TokenUnavailable>();
        unavailable.Reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        harness.Session.CallCount.ShouldBe(1);
        harness.Status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(ChatRuntime.Irc)]
    public async Task UnexpectedEstablishmentFailure_RunningPipeline_ReportsUnhealthyWithoutRetry(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task TimeoutThenEstablishment_RunningPipeline_RetriesThroughDirectTimeoutHook(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 2);
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
    public async Task TransientEstablishmentFailures_ExhaustingAttempts_ReportBoundedUnhealthy(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task SingleAttemptPolicy_TransientFailure_DoesNotAddCompatibilityRetry(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 1);
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
    public async Task CallerCancellation_DuringEstablishment_StopsWithoutFailureReport(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task IdleEstablishment_RunningRuntime_WaitsOutsideRetryThenRechecks(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task EstablishedSession_Listening_UsesHostLifetimeTokenNotAttemptTimeout(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task SuccessfulEstablishment_AfterDisconnect_ResetsConsecutiveAttemptBudget(
        ChatRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
}

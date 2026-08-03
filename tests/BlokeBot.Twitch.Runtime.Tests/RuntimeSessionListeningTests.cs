using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class RuntimeSessionListeningTests : RuntimeSessionResilienceTestBase
{
    [Test]
    [Arguments(ChatRuntime.Irc)]
    public async Task TerminalListeningFailure_RunningRuntime_ReportsUnhealthyWithoutReconnect(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task UnexpectedListeningFailure_RunningRuntime_ReportsUnhealthyWithoutReconnect(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
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
    public async Task ListeningAndCleanupFailure_RunningRuntime_ReportsCombinedUnhealthyWithoutHostFault(
        ChatRuntime runtime
    )
    {
        var harness = CreateRunnerHarness(attemptLimit: 3);
        var listeningFailure = new IOException("established session disconnected");
        var cleanupFailure = new IOException("session cleanup failed");
        var listening = new ScriptedEstablishedSession { DisposeException = cleanupFailure };
        listening.Enqueue(_ => FailedListeningAsync(listeningFailure));
        harness.Session.Enqueue(
            (_, _) =>
            {
                harness.Status.MarkConnected(["channel"]);
                return EstablishedAsync(listening);
            }
        );

        await harness.RunRuntimeAsync(CancellationToken.None);

        harness.Session.CallCount.ShouldBe(1);
        listening.DisposeCount.ShouldBe(1);
        _ = harness.Status.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();
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
}

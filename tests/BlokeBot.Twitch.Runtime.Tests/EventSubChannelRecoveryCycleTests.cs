using System.Threading.Channels;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelRecoveryCycleTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task Recovery_ExhaustingThenExplicitlyTriggered_StartsFreshBoundedCycle()
    {
        var initialFailure = new IOException("initial failure");
        var firstRecoveryFailure = new IOException("first recovery failure");
        var exhaustedFailure = new IOException("exhausted recovery failure");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccountFailure("channel", initialFailure);
        operations.EnqueueAccountFailure("channel", firstRecoveryFailure);
        operations.EnqueueAccountFailure("channel", exhaustedFailure);
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var exhaustedReports = harness.Diagnostics.Reports;
        exhaustedReports.Count.ShouldBe(4);
        AssertFailure(
            exhaustedReports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.BeginRecoveryCycle,
            Now
        );
        AssertFailure(
            exhaustedReports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        AssertFailure(
            exhaustedReports[2].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        AssertFailure(
            exhaustedReports[3].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        var failureReports = harness.Diagnostics.DiagnosticReports;
        failureReports[0]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(initialFailure);
        failureReports[1]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Recovering>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(initialFailure);
        failureReports[2]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Recovering>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(firstRecoveryFailure);
        failureReports[3]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(exhaustedFailure);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Diagnostics.Clear();
        await ReconcileAsync(harness.Session, ["channel"], EventSubChannelRecoveryTrigger.Explicit);

        var recoveredReports = harness.Diagnostics.Reports;
        AssertFailure(
            recoveredReports[0].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            Now.AddMinutes(1)
        );
        AssertHealthy(
            recoveredReports[1].ShouldBeOfType<EventSubChannelStatus.Healthy>(),
            "channel",
            EventSubChannelRecoveryTrigger.Explicit,
            attempt: 1,
            Now.AddMinutes(1)
        );
        harness
            .Diagnostics.DiagnosticReports[0]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Recovering>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(exhaustedFailure);
    }

    [Test]
    public async Task Diagnostics_ReporterFailure_EscalatesOutsideChannelRecovery()
    {
        var operationFailure = new IOException("account lookup failed");
        var reporterFailure = new InvalidOperationException("diagnostic sink failed");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccountFailure("channel", operationFailure);
        var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Diagnostics.EnqueueFailure(reporterFailure);

        Start(harness, ["channel"], CancellationToken.None);
        var taskFailure = await Should.ThrowAsync<EventSubChannelStatusPublicationException>(
            harness.Session.DrainAsync
        );

        taskFailure.InnerException.ShouldBeSameAs(reporterFailure);
        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        harness.Diagnostics.DiagnosticReports.ShouldBeEmpty();
        var cleanupFailure = await Should.ThrowAsync<EventSubChannelStatusPublicationException>(
            () =>
                harness.DisposeAsync().AsTask()
        );
        cleanupFailure.InnerException.ShouldBeSameAs(reporterFailure);
    }

    [Test]
    public async Task HealthyChannel_SiblingRecoveryPending_RemainsHealthyAndActive()
    {
        var initialFailure = new IOException("temporary account failure");
        var enteredRecovery = Channel.CreateUnbounded<bool>();
        var releaseRecovery = Channel.CreateUnbounded<bool>();
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["bad", "good"], CancellationToken.None);
        await harness.Session.DrainAsync();
        harness.Diagnostics.Clear();

        operations.EnqueueAccountFailure("bad", initialFailure);
        operations.EnqueueAccount(
            "bad",
            async cancellationToken =>
            {
                enteredRecovery.Writer.TryWrite(true).ShouldBeTrue();
                _ = await releaseRecovery.Reader.ReadAsync(cancellationToken);
                return new BotAccount("bad-bot", "bad-secret");
            }
        );

        var reconciliation = ReconcileAsync(
            harness.Session,
            ["bad", "good"],
            EventSubChannelRecoveryTrigger.Explicit
        );
        _ = await enteredRecovery.Reader.ReadAsync();
        var current = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        _ = current["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>();
        _ = current["bad"].ShouldBeOfType<EventSubChannelStatus.Recovering>();
        harness.Session.ActiveChannels.ShouldBe(["bad", "good"]);
        operations.DeleteCount("bad").ShouldBe(0);
        operations.CompleteStopCount("bad").ShouldBe(0);

        releaseRecovery.Writer.TryWrite(true).ShouldBeTrue();
        await reconciliation;
        harness.Status.Current.Channels.ShouldAllBe(state =>
            state is EventSubChannelStatus.Healthy
        );
        operations.CompleteStopCount("bad").ShouldBe(0);
    }
}

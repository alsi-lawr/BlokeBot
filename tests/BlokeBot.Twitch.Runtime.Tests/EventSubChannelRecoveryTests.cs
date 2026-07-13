using System.Reflection;
using System.Threading.Channels;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelRecoveryTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Startup_AccountFailureInOneChannel_DoesNotSeriallyBlockHealthySetup()
    {
        var releaseFailure = Channel.CreateUnbounded<bool>();
        var failure = new IOException("oauth:secret account lookup failed");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccount(
            "bad",
            async cancellationToken =>
            {
                await releaseFailure.Reader.ReadAsync(cancellationToken);
                throw failure;
            }
        );
        for (var attempt = 0; attempt < 3; attempt++)
        {
            operations.EnqueueAccountFailure("bad", failure);
        }

        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["bad", "good"], CancellationToken.None);
        var initialization = harness.Session.DrainAsync();

        initialization.IsCompleted.ShouldBeFalse();
        var healthyDuringFailure = (
            await harness.Diagnostics.NextAsync()
        ).ShouldBeOfType<EventSubChannelStatus.Healthy>();
        operations.CreateCount("good").ShouldBe(1);
        AssertHealthy(
            healthyDuringFailure,
            "good",
            EventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            _now
        );
        releaseFailure.Writer.TryWrite(true).ShouldBeTrue();
        await initialization;

        var states = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        AssertHealthy(
            states["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>(),
            "good",
            EventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            _now
        );
        AssertFailure(
            states["bad"].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "bad",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 3,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            _now
        );
        states["bad"].ToString().ShouldNotContain("oauth:secret");
        harness.RuntimeStatus.Current.IsConnected.ShouldBeTrue();
        harness.RuntimeStatus.Current.ConnectedChannels.ShouldBe(["good"]);
        operations.CompleteStopCount("bad").ShouldBe(0);
    }

    [Test]
    public async Task Startup_ChannelAttemptTimeout_DoesNotInterruptHealthySibling()
    {
        var enteredAttempt = Channel.CreateUnbounded<bool>();
        var neverCompletes = Channel.CreateUnbounded<bool>();
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccount(
            "slow",
            async cancellationToken =>
            {
                enteredAttempt.Writer.TryWrite(true).ShouldBeTrue();
                await neverCompletes.Reader.ReadAsync(cancellationToken);
                return new BotAccount("slow-bot", "slow-secret");
            }
        );
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        harness.Session.Start(["good", "slow"], CancellationToken.None);
        var startup = harness.Session.DrainAsync();
        await enteredAttempt.Reader.ReadAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await startup;

        var timeout = harness
            .Diagnostics.Reports.OfType<EventSubChannelStatus.Degraded>()
            .ShouldHaveSingleItem();
        AssertFailure(
            timeout,
            "slow",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Timeout,
            typeof(TimeoutRejectedException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.BeginRecoveryCycle,
            _now.AddMinutes(1)
        );
        harness.Status.Current.Channels.ShouldAllBe(state =>
            state is EventSubChannelStatus.Healthy
        );
        harness.Session.ActiveChannels.ShouldBe(["good", "slow"]);
    }

    [Test]
    public async Task Startup_SubscriptionSetupFailure_PublishesTerminalDegradedPayload()
    {
        var failure = new InvalidOperationException("raw payload must stay private");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            typeof(InvalidOperationException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            _now
        );
        degraded.ToString().ShouldNotContain("raw payload");
        var diagnostic = harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>();
        ClassifiedFailure(diagnostic.Failure).Exception.ShouldBeSameAs(failure);
        typeof(EventSubChannelFailure)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order()
            .ShouldBe(["Classification", "FailureType"]);
        typeof(EventSubChannelDiagnosticReport.Healthy)
            .GetProperty(
                "Failure",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            .ShouldBeNull();
        harness.RuntimeStatus.Current.IsAuthorized.ShouldBeTrue();
        harness.RuntimeStatus.Current.IsConnected.ShouldBeFalse();
        harness.Session.ActiveChannels.ShouldBeEmpty();
        operations.CreateCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        harness.Status.Current.Channels.ShouldHaveSingleItem().ShouldBeSameAs(degraded);
    }

    [Test]
    public async Task Startup_MissingChannelIdentity_IsTerminalWithoutSubscriptionRetry()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateOutcome(
            "channel",
            new EventSubSubscriptionSetupOutcome.MissingChannel()
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            "MissingChannel",
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            _now
        );
        harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.MissingChannel>();
        operations.CreateCount("channel").ShouldBe(1);
        operations.StartupDeliveryCount("channel").ShouldBe(0);
        degraded.ToString().ShouldNotContain("channel-secret");
    }

    [Test]
    public async Task Startup_MissingBotIdentity_IsTerminalWithoutSubscriptionRetry()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateOutcome(
            "channel",
            new EventSubSubscriptionSetupOutcome.MissingBot()
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            "MissingBot",
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            _now
        );
        harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.MissingBot>();
        operations.CreateCount("channel").ShouldBe(1);
        operations.StartupDeliveryCount("channel").ShouldBe(0);
        degraded.ToString().ShouldNotContain("channel-secret");
    }

    [Test]
    public async Task Startup_PublicChatEnqueueRejected_RemainsTerminalAcrossKeepalive()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueStartupDeliveryOutcome(
            "channel",
            new EventSubStartupDeliveryOutcome.Rejected()
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        harness.Session.TriggerReconciliation(
            ["channel"],
            EventSubChannelRecoveryTrigger.Keepalive
        );
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            "PublicChatEnqueueRejected",
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.NoFurtherAction,
            _now
        );
        harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.StartupMessageRejected>();
        operations.CreateCount("channel").ShouldBe(1);
        operations.StartupDeliveryCount("channel").ShouldBe(1);
        operations.ChannelStartedCount("channel").ShouldBe(0);
    }

    [Test]
    public async Task Setup_LifecycleStartFailure_RetriesWithoutRepeatingStartupDelivery()
    {
        var failure = new IOException("lifecycle start temporarily unavailable");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueChannelStartedFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        operations.StartupDeliveryCount("channel").ShouldBe(1);
        operations.ChannelStartedCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        harness
            .Diagnostics.DiagnosticReports.OfType<EventSubChannelDiagnosticReport.Degraded>()
            .ShouldHaveSingleItem()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task Startup_TransientAccountFailure_RecoversIndependently()
    {
        var failure = new IOException("temporary account lookup failure");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccountFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var reports = harness.Diagnostics.Reports;
        AssertFailure(
            reports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.BeginRecoveryCycle,
            _now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            _now
        );
        AssertHealthy(
            reports[2].ShouldBeOfType<EventSubChannelStatus.Healthy>(),
            "channel",
            EventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            _now
        );
        harness.Status.Current.Channels.ShouldHaveSingleItem().ShouldBe(reports[2]);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
    }

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

        harness.Session.Start(["channel"], CancellationToken.None);
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
            _now
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
            _now
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
            _now
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
            _now
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
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

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
            _now.AddMinutes(1)
        );
        AssertHealthy(
            recoveredReports[1].ShouldBeOfType<EventSubChannelStatus.Healthy>(),
            "channel",
            EventSubChannelRecoveryTrigger.Explicit,
            attempt: 1,
            _now.AddMinutes(1)
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

        harness.Session.Start(["channel"], CancellationToken.None);
        var taskFailure = await Should.ThrowAsync<EventSubChannelStatusPublicationException>(
            harness.Session.DrainAsync
        );

        taskFailure.InnerException.ShouldBeSameAs(reporterFailure);
        harness
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
    public async Task HealthyChannel_DispatchingWhileSiblingRecoveryIsPending_RemainsAvailable()
    {
        var initialFailure = new IOException("temporary account failure");
        var enteredRecovery = Channel.CreateUnbounded<bool>();
        var releaseRecovery = Channel.CreateUnbounded<bool>();
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["bad", "good"], CancellationToken.None);
        await harness.Session.DrainAsync();
        harness.Diagnostics.Clear();

        operations.EnqueueAccountFailure("bad", initialFailure);
        operations.EnqueueAccount(
            "bad",
            async cancellationToken =>
            {
                enteredRecovery.Writer.TryWrite(true).ShouldBeTrue();
                await releaseRecovery.Reader.ReadAsync(cancellationToken);
                return new BotAccount("bad-bot", "bad-secret");
            }
        );

        harness.Session.TriggerReconciliation(
            ["bad", "good"],
            EventSubChannelRecoveryTrigger.Explicit
        );
        await enteredRecovery.Reader.ReadAsync();
        var current = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        current["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>();
        current["bad"].ShouldBeOfType<EventSubChannelStatus.Recovering>();
        harness.Session.ActiveChannels.ShouldBe(["bad", "good"]);
        operations.DeleteCount("bad").ShouldBe(0);
        operations.CompleteStopCount("bad").ShouldBe(0);

        var observer = new RecordingChatObserver();
        var connection = new EventSubConnectionSession(
            null!,
            null!,
            ChatActivityHookTests.BuildDispatcher(new ChatActivityHookTests.RuntimeHookRecorder()),
            new UnusedCommandResponseSender(),
            new BotRuntimeStatusStore(),
            [observer],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            NullLogger<EventSubConnectionSession>.Instance
        );
        await connection.DispatchChatMessageAsync(
            new EventSubChatMessageEvent
            {
                BroadcasterUserLogin = "good",
                ChatterUserLogin = "viewer",
                Message = new EventSubChatMessage { Text = "hello" },
            },
            "{}",
            CancellationToken.None
        );

        observer.Channels.ShouldBe(["good"]);
        releaseRecovery.Writer.TryWrite(true).ShouldBeTrue();
        await harness.Session.DrainAsync();
        harness.Status.Current.Channels.ShouldAllBe(state =>
            state is EventSubChannelStatus.Healthy
        );
        operations.CompleteStopCount("bad").ShouldBe(0);
    }

    [Test]
    public async Task Reconciliation_TransientDeleteFailure_RecoversThroughOwnedPolicy()
    {
        var failure = new IOException("remote delete failed once");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("channel", failure);
        harness.Diagnostics.Clear();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(2);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        var reports = harness.Diagnostics.Reports;
        AssertFailure(
            reports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.BeginRecoveryCycle,
            _now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            _now
        );
        var diagnostics = harness.Diagnostics.DiagnosticReports;
        diagnostics[0]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
        diagnostics[1]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Recovering>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task Reconciliation_TimeoutDeleteFailure_RecoversThroughOwnedPolicy()
    {
        var failure = new TimeoutException("remote delete timed out once");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("channel", failure);
        harness.Diagnostics.Clear();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(2);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        var reports = harness.Diagnostics.Reports;
        AssertFailure(
            reports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Timeout,
            typeof(TimeoutException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.BeginRecoveryCycle,
            _now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Timeout,
            typeof(TimeoutException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            _now
        );
    }

    [Test]
    public async Task Reconciliation_TransientDeleteExhaustion_RetainsPendingEvidence()
    {
        var failure = new IOException("remote delete secret");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            operations.EnqueueAccountResult("channel", "replacement-bot");
            operations.EnqueueDeleteFailure("channel", failure);
        }

        harness.Diagnostics.Clear();
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(3);
        operations.CreateCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            _now
        );
        operations.CompleteStopCount("channel").ShouldBe(0);
        var pending = harness.PendingDeletions.PendingDeletions.ShouldHaveSingleItem();
        pending.Subscription.Channel.ShouldBe("channel");
        pending.Subscription.SubscriptionId.ShouldBe("session-id-channel");
        pending.Subscription.AccessToken.ShouldBe("channel-secret");
        var unresolved = pending.State.ShouldBeOfType<EventSubPendingDeletionState.Unresolved>();
        unresolved.Failure.Classification.ShouldBe(EventSubChannelFailureClassification.Transient);
        unresolved.Failure.Exception.ShouldBeSameAs(failure);
        pending.ToString().ShouldNotContain("channel-secret");
        pending.ToString().ShouldNotContain("remote delete secret");
        degraded.ToString().ShouldNotContain("remote delete secret");
    }

    [Test]
    public async Task Reconciliation_TerminalDeleteFailure_DegradesOnlyOwningChannel()
    {
        var failure = new InvalidOperationException("terminal delete secret");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["bad", "good"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("bad", failure);
        harness.Diagnostics.Clear();

        harness.Session.TriggerReconciliation(["good"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("bad").ShouldBe(1);
        operations.CompleteStopCount("bad").ShouldBe(0);
        harness.Session.ActiveChannels.ShouldBe(["bad", "good"]);
        var states = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        states["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>();
        var degraded = states["bad"].ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "bad",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Terminal,
            typeof(InvalidOperationException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            _now
        );
        degraded.ToString().ShouldNotContain("terminal delete secret");
        harness.RuntimeStatus.Current.IsConnected.ShouldBeTrue();
        harness.RuntimeStatus.Current.ConnectedChannels.ShouldBe(["good"]);
        var pending = harness.PendingDeletions.PendingDeletions.ShouldHaveSingleItem();
        pending.Subscription.Channel.ShouldBe("bad");
        pending
            .State.ShouldBeOfType<EventSubPendingDeletionState.Unresolved>()
            .Failure.Exception.ShouldBeSameAs(failure);
        harness
            .Diagnostics.DiagnosticReports.OfType<EventSubChannelDiagnosticReport.Degraded>()
            .ShouldHaveSingleItem()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task Reconciliation_RemovingHealthyChannel_DeletesThenReportsStopped()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        harness.RuntimeStatus.Current.IsConnected.ShouldBeFalse();
    }

    [Test]
    public async Task Reconciliation_StopFailure_RetriesWithoutRepeatingRemoteDelete()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueCompleteStopFailure(
            "channel",
            new IOException("lifecycle store temporarily unavailable")
        );

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
    }

    [Test]
    public async Task Reconciliation_DeleteCancellation_PreservesScheduledEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], cancellation.Token);
        await harness.Session.DrainAsync();
        operations.EnqueueBeforeDelete("channel", cancellation.Cancel);
        operations.EnqueueDeleteFailure(
            "channel",
            new OperationCanceledException(cancellation.Token)
        );

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            harness.Session.DrainAsync
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        harness
            .PendingDeletions.PendingDeletions.ShouldHaveSingleItem()
            .State.ShouldBeOfType<EventSubPendingDeletionState.Scheduled>();
        harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
    }

    [Test]
    public async Task Startup_PendingDeletionFromReplacedSession_ReconcilesBeforeRemoval()
    {
        var sharedStatus = new EventSubChannelStatusStore();
        var sharedRuntimeStatus = new BotRuntimeStatusStore();
        var sharedPendingDeletions = new EventSubSubscriptionReconciliationStore();
        var oldOperations = new ScriptedChannelOperations();
        await using var old = CreateHarness(
            oldOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus,
            sharedPendingDeletions
        );
        old.Session.Start(["channel"], CancellationToken.None);
        await old.Session.DrainAsync();
        oldOperations.EnqueueDeleteFailure(
            "channel",
            new InvalidOperationException("old session delete secret")
        );
        old.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await old.Session.DrainAsync();
        sharedPendingDeletions.PendingDeletions.ShouldHaveSingleItem();
        sharedPendingDeletions.HasPendingReconciliation.ShouldBeTrue();
        await old.DisposeAsync();

        var replacementOperations = new ScriptedChannelOperations();
        await using var replacement = CreateHarness(
            replacementOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus,
            sharedPendingDeletions
        );
        replacement.Session.Start([], CancellationToken.None);
        await replacement.Session.DrainAsync();

        replacementOperations.DeleteCount("channel").ShouldBe(1);
        var deleted = replacementOperations.DeleteAttempts("channel").ShouldHaveSingleItem();
        deleted.SubscriptionId.ShouldBe("session-id-channel");
        deleted.AccessToken.ShouldBe("channel-secret");
        replacementOperations.CompleteStopCount("channel").ShouldBe(1);
        sharedPendingDeletions.PendingDeletions.ShouldBeEmpty();
        sharedPendingDeletions.HasPendingReconciliation.ShouldBeFalse();
        replacement.Session.ActiveChannels.ShouldBeEmpty();
        sharedStatus.Current.Channels.ShouldBeEmpty();
        sharedRuntimeStatus.Current.ConnectedChannels.ShouldBeEmpty();
    }

    [Test]
    public async Task ReplacementStartup_DisposingOldPendingRecovery_PreventsPostDisposalMutation()
    {
        var sharedStatus = new EventSubChannelStatusStore();
        var sharedRuntimeStatus = new BotRuntimeStatusStore();
        var oldOperations = new ScriptedChannelOperations();
        var enteredRecovery = Channel.CreateUnbounded<bool>();
        var releaseRecovery = Channel.CreateUnbounded<bool>();
        await using var old = CreateHarness(
            oldOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus
        );
        old.Session.Start(["old"], CancellationToken.None);
        await old.Session.DrainAsync();
        oldOperations.EnqueueAccount(
            "old",
            async cancellationToken =>
            {
                enteredRecovery.Writer.TryWrite(true).ShouldBeTrue();
                await releaseRecovery.Reader.ReadAsync(cancellationToken);
                return new BotAccount("old-bot", "old-secret");
            }
        );
        old.Session.TriggerReconciliation(["old"], EventSubChannelRecoveryTrigger.Explicit);
        await enteredRecovery.Reader.ReadAsync();

        var replacementOperations = new ScriptedChannelOperations();
        await using var replacement = CreateHarness(
            replacementOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus
        );
        replacement.Session.Start(["replacement"], CancellationToken.None);
        await replacement.Session.DrainAsync();

        await old.Session.DisposeAsync();
        releaseRecovery.Writer.TryWrite(true).ShouldBeTrue();

        var replacementState = sharedStatus
            .Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        replacementState.Channel.ShouldBe("replacement");
        sharedRuntimeStatus.Current.ConnectedChannels.ShouldBe(["replacement"]);
        oldOperations.CreateCount("old").ShouldBe(1);
        old.Session.ActiveChannels.ShouldBe(["old"]);
        replacement.Session.ActiveChannels.ShouldBe(["replacement"]);
    }

    [Test]
    public void SubscriptionDeletionOutcome_Inspecting_HasDeclaredDirectCases()
    {
        var unionType = typeof(EventSubSubscriptionDeletionOutcome);
        var directCases = unionType
            .GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();

        unionType.IsAbstract.ShouldBeTrue();
        unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        directCases.Select(type => type.Name).ShouldBe(["Deleted", "Unresolved"]);
        directCases.ShouldAllBe(type => type.IsSealed);
    }

    [Test]
    public void ChannelReconciliationOutcome_Inspecting_HasDeclaredCasesAndCompleteHandlerSignatures()
    {
        var unionType = typeof(EventSubChannelReconciliationOutcome);
        var directCases = unionType
            .GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();
        var match =
            unionType.GetMethod("Match", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The channel reconciliation Match is missing.");
        var constructor =
            unionType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                "The channel reconciliation constructor is missing."
            );
        var matchResultType = match.GetGenericArguments().ShouldHaveSingleItem();
        var handlerParameters = match.GetParameters();
        var handledCases = new List<Type>(handlerParameters.Length);

        unionType.IsAbstract.ShouldBeTrue();
        unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        constructor.IsPrivate.ShouldBeTrue();
        unionType
            .GetMethods(
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            .ShouldNotContain(method => method.Name == "Seal");
        match.IsGenericMethodDefinition.ShouldBeTrue();
        matchResultType.IsGenericParameter.ShouldBeTrue();
        matchResultType.Name.ShouldBe("TResult");
        handlerParameters.Length.ShouldBe(directCases.Length);
        foreach (var handlerParameter in handlerParameters)
        {
            var handlerType = handlerParameter.ParameterType;
            handlerType.IsGenericType.ShouldBeTrue();
            handlerType.GetGenericTypeDefinition().ShouldBe(typeof(Func<,>));
            var handlerTypeArguments = handlerType.GetGenericArguments();
            directCases.ShouldContain(handlerTypeArguments[0]);
            handlerTypeArguments[1].ShouldBe(matchResultType);
            handledCases.Add(handlerTypeArguments[0]);
        }

        directCases
            .Select(type => type.Name)
            .ShouldBe([
                "Completed",
                "MissingBot",
                "MissingChannel",
                "StartupMessageRejected",
                "UnresolvedDeletion",
            ]);
        handledCases.OrderBy(type => type.Name).ShouldBe(directCases);
        directCases.ShouldAllBe(type => type.IsSealed);
    }

    [Test]
    public void ChannelLifecycleUnion_Inspecting_HasDeclaredDirectCasesAndCompleteHandlers()
    {
        var unionType = typeof(EventSubChannelStatus);
        var directCases = unionType
            .Assembly.GetTypes()
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();
        var match =
            unionType.GetMethod(nameof(EventSubChannelStatus.Match))
            ?? throw new InvalidOperationException("The channel lifecycle Match is missing.");
        var constructor =
            unionType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            )
            ?? throw new InvalidOperationException("The channel lifecycle constructor is missing.");
        var handledCases = match
            .GetParameters()
            .Select(parameter => parameter.ParameterType.GetGenericArguments()[0])
            .OrderBy(type => type.Name)
            .ToArray();

        unionType.IsAbstract.ShouldBeTrue();
        unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        constructor.IsPrivate.ShouldBeTrue();
        directCases.Select(type => type.Name).ShouldBe(["Degraded", "Healthy", "Recovering"]);
        handledCases.ShouldBe(directCases);
        directCases.ShouldAllBe(type => type.DeclaringType == unionType);
        directCases.ShouldAllBe(type => type.IsSealed);
    }

    [Test]
    public void ChannelFailureClassifier_ClassifyingBoundaryFailures_UsesChannelSemantics()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellation = EventSubChannelFailureClassifier.Classify(
            new OperationCanceledException(canceled.Token),
            EventSubChannelPhase.AccountResolution,
            canceled.Token
        );
        var transientSetup = EventSubChannelFailureClassifier.Classify(
            new EventSubChannelOperationException(
                EventSubChannelPhase.SubscriptionSetup,
                new HttpRequestException(
                    "service unavailable",
                    null,
                    System.Net.HttpStatusCode.ServiceUnavailable
                )
            ),
            EventSubChannelPhase.AccountResolution,
            CancellationToken.None
        );
        var unavailableAccount = EventSubChannelFailureClassifier.Classify(
            new EventSubChannelOperationException(
                EventSubChannelPhase.AccountResolution,
                new AccessTokenUnavailableException(
                    AccessTokenUnavailableReason.MissingRefreshToken,
                    AccessTokenUnavailableException.MissingRefreshTokenMessage
                )
            ),
            EventSubChannelPhase.SubscriptionSetup,
            CancellationToken.None
        );
        var unexpected = EventSubChannelFailureClassifier.Classify(
            new ApplicationException("programmer defect"),
            EventSubChannelPhase.Reconciliation,
            CancellationToken.None
        );
        var deletionCause = new IOException("delete failed");
        var deletionFailure = EventSubChannelFailureClassifier.Classify(
            deletionCause,
            EventSubChannelPhase.SubscriptionDeletion,
            CancellationToken.None
        );

        cancellation.Classification.ShouldBe(EventSubChannelFailureClassification.Cancellation);
        transientSetup.Phase.ShouldBe(EventSubChannelPhase.SubscriptionSetup);
        transientSetup.Classification.ShouldBe(EventSubChannelFailureClassification.Transient);
        unavailableAccount.Phase.ShouldBe(EventSubChannelPhase.AccountResolution);
        unavailableAccount.Classification.ShouldBe(EventSubChannelFailureClassification.Terminal);
        unexpected.Classification.ShouldBe(EventSubChannelFailureClassification.Unexpected);
        deletionFailure.Phase.ShouldBe(EventSubChannelPhase.SubscriptionDeletion);
        deletionFailure.Classification.ShouldBe(EventSubChannelFailureClassification.Transient);
        deletionFailure.Exception.ShouldBeSameAs(deletionCause);
    }

    private static RecoveryHarness CreateHarness(
        ScriptedChannelOperations operations,
        int attemptLimit,
        EventSubChannelStatusStore? sharedStatus = null,
        BotRuntimeStatusStore? sharedRuntimeStatus = null,
        EventSubSubscriptionReconciliationStore? sharedPendingDeletions = null
    )
    {
        var clock = new FixedTimeProvider(_now);
        var attemptBuilder = new ResiliencePipelineBuilder { TimeProvider = clock };
        var recoveryBuilder = new ResiliencePipelineBuilder<EventSubChannelReconciliationOutcome>
        {
            TimeProvider = clock,
        };
        var policy = new EventSubChannelRecoveryPolicy
        {
            AttemptLimit = attemptLimit,
            Delay = TimeSpan.Zero,
            MaximumDelay = TimeSpan.Zero,
            DelayBackoffType = DelayBackoffType.Constant,
            AttemptTimeout = TimeSpan.FromMinutes(1),
        };
        EventSubChannelRecoveryResilience.ConfigureAttempt(attemptBuilder, policy);
        EventSubChannelRecoveryResilience.Configure(recoveryBuilder, policy);
        var status = sharedStatus ?? new EventSubChannelStatusStore();
        var runtimeStatus = sharedRuntimeStatus ?? new BotRuntimeStatusStore();
        var pendingDeletions =
            sharedPendingDeletions ?? new EventSubSubscriptionReconciliationStore();
        var diagnostics = new RecordingDiagnostics();
        return new RecoveryHarness(
            new EventSubChannelSession(
                "session-id",
                operations,
                new EventSubChannelRecoveryPipeline(
                    attemptBuilder.Build(),
                    recoveryBuilder.Build()
                ),
                pendingDeletions,
                status.CreateScope(),
                runtimeStatus,
                diagnostics,
                clock
            ),
            status,
            runtimeStatus,
            pendingDeletions,
            diagnostics,
            clock
        );
    }

    private static void AssertHealthy(
        EventSubChannelStatus.Healthy status,
        string channel,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        DateTimeOffset changedAt
    )
    {
        status.Channel.ShouldBe(channel);
        status.Phase.ShouldBe(EventSubChannelPhase.Reconciliation);
        status.Attempt.ShouldBe(attempt);
        status.ChangedAt.ShouldBe(changedAt);
        status.Trigger.ShouldBe(trigger);
    }

    private static EventSubChannelFailureDetails ClassifiedFailure(
        EventSubChannelFailureContext context
    )
    {
        return context.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>().Details;
    }

    private static void AssertFailure(
        EventSubChannelStatus status,
        string channel,
        EventSubChannelPhase phase,
        EventSubChannelFailureClassification classification,
        Type failureType,
        int attempt,
        EventSubChannelRecoveryTrigger trigger,
        EventSubChannelNextAction nextAction,
        DateTimeOffset changedAt
    )
    {
        AssertFailure(
            status,
            channel,
            phase,
            classification,
            failureType.FullName!,
            attempt,
            trigger,
            nextAction,
            changedAt
        );
    }

    private static void AssertFailure(
        EventSubChannelStatus status,
        string channel,
        EventSubChannelPhase phase,
        EventSubChannelFailureClassification classification,
        string failureType,
        int attempt,
        EventSubChannelRecoveryTrigger trigger,
        EventSubChannelNextAction nextAction,
        DateTimeOffset changedAt
    )
    {
        status.Channel.ShouldBe(channel);
        status.Phase.ShouldBe(phase);
        status.Attempt.ShouldBe(attempt);
        status.ChangedAt.ShouldBe(changedAt);
        status.Trigger.ShouldBe(trigger);
        status
            .Match(
                _ => throw new InvalidOperationException("Expected a failed channel state."),
                recovering => (recovering.Failure, recovering.NextAction),
                degraded => (degraded.Failure, degraded.NextAction)
            )
            .ShouldBe(
                (
                    new EventSubChannelFailure
                    {
                        Classification = classification,
                        FailureType = failureType,
                    },
                    nextAction
                )
            );
    }

    private sealed class RecoveryHarness(
        EventSubChannelSession session,
        EventSubChannelStatusStore status,
        BotRuntimeStatusStore runtimeStatus,
        EventSubSubscriptionReconciliationStore pendingDeletions,
        RecordingDiagnostics diagnostics,
        FixedTimeProvider clock
    ) : IAsyncDisposable
    {
        internal EventSubChannelSession Session { get; } = session;

        internal EventSubChannelStatusStore Status { get; } = status;

        internal BotRuntimeStatusStore RuntimeStatus { get; } = runtimeStatus;

        internal EventSubSubscriptionReconciliationStore PendingDeletions { get; } =
            pendingDeletions;

        internal RecordingDiagnostics Diagnostics { get; } = diagnostics;

        internal FixedTimeProvider Clock { get; } = clock;

        public ValueTask DisposeAsync()
        {
            return Session.DisposeAsync();
        }
    }

    private sealed class ScriptedChannelOperations : IEventSubChannelOperations
    {
        private readonly Dictionary<
            string,
            Queue<Func<CancellationToken, ValueTask<BotAccount>>>
        > _accountScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            Queue<Func<CancellationToken, ValueTask<EventSubSubscriptionSetupOutcome>>>
        > _createScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _createCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> _deleteFailures = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Action>> _beforeDelete = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> _deleteCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, List<ActiveEventSubSubscription>> _deleteAttempts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> _startupDeliveryCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<
            string,
            Queue<EventSubStartupDeliveryOutcome>
        > _startupDeliveryOutcomes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _channelStartedCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> _channelStartedFailures = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> _completeStopCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> _completeStopFailures = new(
            StringComparer.OrdinalIgnoreCase
        );

        internal void EnqueueAccount(
            string channel,
            Func<CancellationToken, ValueTask<BotAccount>> operation
        )
        {
            GetQueue(_accountScripts, channel).Enqueue(operation);
        }

        internal void EnqueueAccountFailure(string channel, Exception exception)
        {
            EnqueueAccount(channel, _ => ValueTask.FromException<BotAccount>(exception));
        }

        internal void EnqueueAccountResult(string channel, string botLogin)
        {
            EnqueueAccount(channel, _ => ValueTask.FromResult(new BotAccount(botLogin, "secret")));
        }

        internal void EnqueueCreateFailure(string channel, Exception exception)
        {
            EnqueueCreate(
                channel,
                _ => ValueTask.FromException<EventSubSubscriptionSetupOutcome>(exception)
            );
        }

        internal void EnqueueCreateOutcome(string channel, EventSubSubscriptionSetupOutcome outcome)
        {
            EnqueueCreate(channel, _ => ValueTask.FromResult(outcome));
        }

        internal int CreateCount(string channel)
        {
            return _createCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueDeleteFailure(string channel, Exception exception)
        {
            GetQueue(_deleteFailures, channel).Enqueue(exception);
        }

        internal void EnqueueBeforeDelete(string channel, Action action)
        {
            GetQueue(_beforeDelete, channel).Enqueue(action);
        }

        internal int DeleteCount(string channel)
        {
            return _deleteCounts.GetValueOrDefault(channel);
        }

        internal IReadOnlyList<ActiveEventSubSubscription> DeleteAttempts(string channel)
        {
            return _deleteAttempts.TryGetValue(channel, out var attempts) ? attempts.ToArray() : [];
        }

        internal int StartupDeliveryCount(string channel)
        {
            return _startupDeliveryCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueStartupDeliveryOutcome(
            string channel,
            EventSubStartupDeliveryOutcome outcome
        )
        {
            GetQueue(_startupDeliveryOutcomes, channel).Enqueue(outcome);
        }

        internal int ChannelStartedCount(string channel)
        {
            return _channelStartedCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueChannelStartedFailure(string channel, Exception exception)
        {
            GetQueue(_channelStartedFailures, channel).Enqueue(exception);
        }

        internal int CompleteStopCount(string channel)
        {
            return _completeStopCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueCompleteStopFailure(string channel, Exception exception)
        {
            GetQueue(_completeStopFailures, channel).Enqueue(exception);
        }

        public ValueTask<BotAccount> ResolveAccountAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            return _accountScripts.TryGetValue(channel, out var scripts) && scripts.Count > 0
                ? scripts.Dequeue()(cancellationToken)
                : ValueTask.FromResult(new BotAccount($"{channel}-bot", $"{channel}-secret"));
        }

        public ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
            string channel,
            BotAccount account,
            string sessionId,
            CancellationToken cancellationToken
        )
        {
            _createCounts[channel] = CreateCount(channel) + 1;
            if (_createScripts.TryGetValue(channel, out var scripts) && scripts.Count > 0)
            {
                return scripts.Dequeue()(cancellationToken);
            }

            return ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                new EventSubSubscriptionSetupOutcome.Created(
                    new ActiveEventSubSubscription
                    {
                        Channel = channel,
                        SubscriptionId = $"{sessionId}-{channel}",
                        BotLogin = account.Login,
                        AccessToken = account.AccessToken,
                        Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
                    }
                )
            );
        }

        public ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            _startupDeliveryCounts[channel] = StartupDeliveryCount(channel) + 1;
            EventSubStartupDeliveryOutcome outcome =
                _startupDeliveryOutcomes.TryGetValue(channel, out var outcomes)
                && outcomes.Count > 0
                    ? outcomes.Dequeue()
                    : new EventSubStartupDeliveryOutcome.Completed();
            return ValueTask.FromResult(outcome);
        }

        public ValueTask NotifyChannelStartedAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            _channelStartedCounts[channel] = ChannelStartedCount(channel) + 1;
            if (
                _channelStartedFailures.TryGetValue(channel, out var failures)
                && failures.Count > 0
            )
            {
                return ValueTask.FromException(failures.Dequeue());
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
            ActiveEventSubSubscription subscription,
            CancellationToken cancellationToken
        )
        {
            _deleteCounts[subscription.Channel] = DeleteCount(subscription.Channel) + 1;
            if (!_deleteAttempts.TryGetValue(subscription.Channel, out var attempts))
            {
                attempts = [];
                _deleteAttempts[subscription.Channel] = attempts;
            }

            attempts.Add(subscription);
            if (
                _beforeDelete.TryGetValue(subscription.Channel, out var actions)
                && actions.Count > 0
            )
            {
                actions.Dequeue()();
            }

            if (
                !_deleteFailures.TryGetValue(subscription.Channel, out var failures)
                || failures.Count == 0
            )
            {
                return ValueTask.FromResult<EventSubSubscriptionDeletionOutcome>(
                    new EventSubSubscriptionDeletionOutcome.Deleted()
                );
            }

            var exception = failures.Dequeue();
            if (
                exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested
            )
            {
                return ValueTask.FromException<EventSubSubscriptionDeletionOutcome>(exception);
            }

            return ValueTask.FromResult<EventSubSubscriptionDeletionOutcome>(
                new EventSubSubscriptionDeletionOutcome.Unresolved
                {
                    Failure = EventSubChannelFailureClassifier.Classify(
                        exception,
                        EventSubChannelPhase.SubscriptionDeletion,
                        cancellationToken
                    ),
                }
            );
        }

        public ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken)
        {
            _completeStopCounts[channel] = CompleteStopCount(channel) + 1;
            return
                _completeStopFailures.TryGetValue(channel, out var failures) && failures.Count > 0
                ? ValueTask.FromException(failures.Dequeue())
                : ValueTask.CompletedTask;
        }

        private static Queue<TValue> GetQueue<TValue>(
            Dictionary<string, Queue<TValue>> queues,
            string channel
        )
        {
            if (!queues.TryGetValue(channel, out var queue))
            {
                queue = new Queue<TValue>();
                queues[channel] = queue;
            }

            return queue;
        }

        private void EnqueueCreate(
            string channel,
            Func<CancellationToken, ValueTask<EventSubSubscriptionSetupOutcome>> operation
        )
        {
            GetQueue(_createScripts, channel).Enqueue(operation);
        }
    }

    private sealed class RecordingDiagnostics : IEventSubChannelDiagnosticReporter
    {
        private readonly object _gate = new();
        private readonly List<EventSubChannelDiagnosticReport> _reports = [];
        private readonly Queue<Exception> _failures = [];
        private readonly Channel<EventSubChannelStatus> _transitions =
            Channel.CreateUnbounded<EventSubChannelStatus>();

        internal IReadOnlyList<EventSubChannelStatus> Reports
        {
            get
            {
                lock (_gate)
                {
                    return _reports.Select(report => report.Status).ToArray();
                }
            }
        }

        internal IReadOnlyList<EventSubChannelDiagnosticReport> DiagnosticReports
        {
            get
            {
                lock (_gate)
                {
                    return _reports.ToArray();
                }
            }
        }

        public void Report(EventSubChannelDiagnosticReport report)
        {
            lock (_gate)
            {
                if (_failures.TryDequeue(out var failure))
                {
                    throw failure;
                }

                _reports.Add(report);
            }

            _transitions.Writer.TryWrite(report.Status).ShouldBeTrue();
        }

        internal void EnqueueFailure(Exception failure)
        {
            lock (_gate)
            {
                _failures.Enqueue(failure);
            }
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _reports.Clear();
            }

            while (_transitions.Reader.TryRead(out _)) { }
        }

        internal ValueTask<EventSubChannelStatus> NextAsync()
        {
            return _transitions.Reader.ReadAsync();
        }
    }

    private sealed class RecordingChatObserver : IChatMessageObserver
    {
        internal List<string> Channels { get; } = [];

        public ValueTask MessageReceivedAsync(
            ChatMessage message,
            CancellationToken cancellationToken
        )
        {
            Channels.Add(message.Channel);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedCommandResponseSender : ICommandResponseSender
    {
        public ValueTask SendAsync(
            ChatMessage sourceMessage,
            CommandResponse response,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("No command response was expected.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly HashSet<ManualTimer> _timers = [];
        private DateTimeOffset _now = initialNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override long GetTimestamp()
        {
            return GetUtcNow().UtcTicks;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _now = _now.Add(duration);
                due = _timers.Where(timer => timer.IsDue(_now)).ToArray();
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private void Add(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Add(timer);
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(
            FixedTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
            private bool _disposed;

            internal bool IsDue(DateTimeOffset current)
            {
                return !_disposed && _dueAt <= current;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _period = period;
                    _dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._now.Add(dueTime);
                    owner.Add(this);
                    return true;
                }
            }

            internal void Fire()
            {
                lock (owner._gate)
                {
                    if (!IsDue(owner._now))
                    {
                        return;
                    }

                    _dueAt =
                        _period == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._now.Add(_period);
                }

                callback(state);
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    owner.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}

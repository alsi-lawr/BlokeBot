using System.Reflection;
using System.Threading.Channels;
using BlokeBot.Commands;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelRecoveryTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        11,
        12,
        0,
        0,
        TimeSpan.Zero
    );

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
            operations.EnqueueAccountFailure("bad", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["bad", "good"], CancellationToken.None);
        var initialization = harness.Session.DrainAsync();

        initialization.IsCompleted.ShouldBeFalse();
        var healthyDuringFailure = (await harness.Diagnostics.NextAsync())
            .ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>();
        operations.CreateCount("good").ShouldBe(1);
        AssertHealthy(
            healthyDuringFailure,
            "good",
            TwitchEventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            Now
        );
        releaseFailure.Writer.TryWrite(true).ShouldBeTrue();
        await initialization;

        var states = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        AssertHealthy(
            states["good"].ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>(),
            "good",
            TwitchEventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            Now
        );
        AssertFailure(
            states["bad"].ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>(),
            "bad",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 3,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.RetryOnNextReconciliation,
            Now
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
                return new TwitchBotAccount("slow-bot", "slow-secret");
            }
        );
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        harness.Session.Start(["good", "slow"], CancellationToken.None);
        var startup = harness.Session.DrainAsync();
        await enteredAttempt.Reader.ReadAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await startup;

        var timeout = harness.Diagnostics.Reports
            .OfType<TwitchEventSubChannelStatus.Degraded>()
            .ShouldHaveSingleItem();
        AssertFailure(
            timeout,
            "slow",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Timeout,
            typeof(TimeoutRejectedException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.BeginRecoveryCycle,
            Now.AddMinutes(1)
        );
        harness.Status.Current.Channels.ShouldAllBe(state =>
            state is TwitchEventSubChannelStatus.Healthy
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

        var degraded = harness.Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            TwitchEventSubChannelPhase.SubscriptionSetup,
            TwitchEventSubChannelFailureClassification.Terminal,
            typeof(InvalidOperationException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        degraded.ToString().ShouldNotContain("raw payload");
        var diagnostic = harness.Diagnostics.DiagnosticReports
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchEventSubChannelDiagnosticReport.Degraded>();
        diagnostic.Failure.Exception.ShouldBeSameAs(failure);
        typeof(TwitchEventSubChannelFailure)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order()
            .ShouldBe(["Classification", "FailureType"]);
        typeof(TwitchEventSubChannelDiagnosticReport.Healthy)
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
        harness.Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>();
        harness.Diagnostics.DiagnosticReports
            .OfType<TwitchEventSubChannelDiagnosticReport.Degraded>()
            .ShouldHaveSingleItem()
            .Failure.Exception.ShouldBeSameAs(failure);
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
            reports[0].ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.BeginRecoveryCycle,
            Now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<TwitchEventSubChannelStatus.Recovering>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        AssertHealthy(
            reports[2].ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>(),
            "channel",
            TwitchEventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            Now
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
            exhaustedReports[0].ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.BeginRecoveryCycle,
            Now
        );
        AssertFailure(
            exhaustedReports[1].ShouldBeOfType<TwitchEventSubChannelStatus.Recovering>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        AssertFailure(
            exhaustedReports[2].ShouldBeOfType<TwitchEventSubChannelStatus.Recovering>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        AssertFailure(
            exhaustedReports[3].ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            TwitchEventSubChannelRecoveryTrigger.Startup,
            TwitchEventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        var failureReports = harness.Diagnostics.DiagnosticReports;
        failureReports[0]
            .ShouldBeOfType<TwitchEventSubChannelDiagnosticReport.Degraded>()
            .Failure.Exception.ShouldBeSameAs(initialFailure);
        failureReports[1]
            .ShouldBeOfType<TwitchEventSubChannelDiagnosticReport.Recovering>()
            .Failure.Exception.ShouldBeSameAs(initialFailure);
        failureReports[2]
            .ShouldBeOfType<TwitchEventSubChannelDiagnosticReport.Recovering>()
            .Failure.Exception.ShouldBeSameAs(firstRecoveryFailure);
        failureReports[3]
            .ShouldBeOfType<TwitchEventSubChannelDiagnosticReport.Degraded>()
            .Failure.Exception.ShouldBeSameAs(exhaustedFailure);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Diagnostics.Clear();
        harness.Session.TriggerReconciliation(
            ["channel"],
            TwitchEventSubChannelRecoveryTrigger.Explicit
        );
        await harness.Session.DrainAsync();

        var recoveredReports = harness.Diagnostics.Reports;
        AssertFailure(
            recoveredReports[0]
                .ShouldBeOfType<TwitchEventSubChannelStatus.Recovering>(),
            "channel",
            TwitchEventSubChannelPhase.AccountResolution,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            TwitchEventSubChannelRecoveryTrigger.Explicit,
            TwitchEventSubChannelNextAction.ContinueRecoveryCycle,
            Now.AddMinutes(1)
        );
        AssertHealthy(
            recoveredReports[1].ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>(),
            "channel",
            TwitchEventSubChannelRecoveryTrigger.Explicit,
            attempt: 1,
            Now.AddMinutes(1)
        );
        harness.Diagnostics.DiagnosticReports[0]
            .ShouldBeOfType<TwitchEventSubChannelDiagnosticReport.Recovering>()
            .Failure.Exception.ShouldBeSameAs(exhaustedFailure);
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
        var taskFailure = await Should.ThrowAsync<
            TwitchEventSubChannelStatusPublicationException
        >(() => harness.Session.DrainAsync());

        taskFailure.InnerException.ShouldBeSameAs(reporterFailure);
        harness.Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>();
        harness.Diagnostics.DiagnosticReports.ShouldBeEmpty();
        var cleanupFailure = await Should.ThrowAsync<
            TwitchEventSubChannelStatusPublicationException
        >(() => harness.DisposeAsync().AsTask());
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
                return new TwitchBotAccount("bad-bot", "bad-secret");
            }
        );

        harness.Session.TriggerReconciliation(
            ["bad", "good"],
            TwitchEventSubChannelRecoveryTrigger.Explicit
        );
        await enteredRecovery.Reader.ReadAsync();
        var current = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        current["good"].ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>();
        current["bad"].ShouldBeOfType<TwitchEventSubChannelStatus.Recovering>();
        harness.Session.ActiveChannels.ShouldBe(["bad", "good"]);
        operations.DeleteCount("bad").ShouldBe(0);
        operations.CompleteStopCount("bad").ShouldBe(0);

        var observer = new RecordingChatObserver();
        var connection = new TwitchEventSubConnectionSession(
            null!,
            null!,
            ChatActivityHookTests.BuildDispatcher(
                new ChatActivityHookTests.RuntimeHookRecorder()
            ),
            new UnusedCommandResponseSender(),
            new TwitchBotRuntimeStatusStore(),
            [observer],
            NullLogger<TwitchEventSubConnectionSession>.Instance
        );
        await connection.DispatchChatMessageAsync(
            new TwitchEventSubChatMessageEvent
            {
                BroadcasterUserLogin = "good",
                ChatterUserLogin = "viewer",
                Message = new TwitchEventSubChatMessage { Text = "hello" },
            },
            "{}",
            CancellationToken.None
        );

        observer.Channels.ShouldBe(["good"]);
        releaseRecovery.Writer.TryWrite(true).ShouldBeTrue();
        await harness.Session.DrainAsync();
        harness.Status.Current.Channels.ShouldAllBe(state =>
            state is TwitchEventSubChannelStatus.Healthy
        );
        operations.CompleteStopCount("bad").ShouldBe(0);
    }

    [Test]
    public async Task Reconciliation_DeleteFailure_RetainsActiveEvidenceForCs015()
    {
        var failure = new IOException("remote delete failed");
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
        harness.Session.TriggerReconciliation(
            ["channel"],
            TwitchEventSubChannelRecoveryTrigger.Explicit
        );
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(3);
        operations.CreateCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        var degraded = harness.Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchEventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            TwitchEventSubChannelPhase.Reconciliation,
            TwitchEventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            TwitchEventSubChannelRecoveryTrigger.Explicit,
            TwitchEventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
    }

    [Test]
    public async Task Reconciliation_RemovingHealthyChannel_DeletesThenReportsStopped()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        harness.Session.TriggerReconciliation(
            [],
            TwitchEventSubChannelRecoveryTrigger.Explicit
        );
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
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

        harness.Session.TriggerReconciliation(
            [],
            TwitchEventSubChannelRecoveryTrigger.Explicit
        );
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
    }

    [Test]
    public async Task ReplacementStartup_DisposingOldPendingRecovery_PreventsPostDisposalMutation()
    {
        var sharedStatus = new TwitchEventSubChannelStatusStore();
        var sharedRuntimeStatus = new TwitchBotRuntimeStatusStore();
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
                return new TwitchBotAccount("old-bot", "old-secret");
            }
        );
        old.Session.TriggerReconciliation(
            ["old"],
            TwitchEventSubChannelRecoveryTrigger.Explicit
        );
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

        var replacementState = sharedStatus.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchEventSubChannelStatus.Healthy>();
        replacementState.Channel.ShouldBe("replacement");
        sharedRuntimeStatus.Current.ConnectedChannels.ShouldBe(["replacement"]);
        oldOperations.CreateCount("old").ShouldBe(1);
        old.Session.ActiveChannels.ShouldBe(["old"]);
        replacement.Session.ActiveChannels.ShouldBe(["replacement"]);
    }

    [Test]
    public void ChannelLifecycleUnion_Inspecting_IsClosedAndHandlerComplete()
    {
        var unionType = typeof(TwitchEventSubChannelStatus);
        var directCases = unionType
            .Assembly.GetTypes()
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();
        var seal = unionType.GetMethod(
                "Seal",
                BindingFlags.Instance | BindingFlags.NonPublic
            )
            ?? throw new InvalidOperationException("The channel lifecycle seal is missing.");
        var match = unionType.GetMethod(nameof(TwitchEventSubChannelStatus.Match))
            ?? throw new InvalidOperationException("The channel lifecycle Match is missing.");
        var handledCases = match
            .GetParameters()
            .Select(parameter => parameter.ParameterType.GetGenericArguments()[0])
            .OrderBy(type => type.Name)
            .ToArray();

        unionType.IsAbstract.ShouldBeTrue();
        unionType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .ShouldBeEmpty();
        seal.IsAbstract.ShouldBeTrue();
        seal.IsFamilyAndAssembly.ShouldBeTrue();
        directCases.Select(type => type.Name).ShouldBe(
            ["Degraded", "Healthy", "Recovering"]
        );
        handledCases.ShouldBe(directCases);
        directCases.ShouldAllBe(type => type.IsSealed);
        foreach (var caseType in directCases)
        {
            var caseSeal = caseType.GetMethod(
                    "Seal",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
                ?? throw new InvalidOperationException(
                    $"{caseType.Name} does not implement the lifecycle seal."
                );
            caseSeal.GetBaseDefinition().ShouldBe(seal);
        }
    }

    [Test]
    public void ChannelFailureClassifier_ClassifyingBoundaryFailures_UsesChannelSemantics()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellation = TwitchEventSubChannelFailureClassifier.Classify(
            new OperationCanceledException(canceled.Token),
            TwitchEventSubChannelPhase.AccountResolution,
            canceled.Token
        );
        var transientSetup = TwitchEventSubChannelFailureClassifier.Classify(
            new TwitchEventSubChannelOperationException(
                TwitchEventSubChannelPhase.SubscriptionSetup,
                new HttpRequestException(
                    "service unavailable",
                    null,
                    System.Net.HttpStatusCode.ServiceUnavailable
                )
            ),
            TwitchEventSubChannelPhase.AccountResolution,
            CancellationToken.None
        );
        var unavailableAccount = TwitchEventSubChannelFailureClassifier.Classify(
            new TwitchEventSubChannelOperationException(
                TwitchEventSubChannelPhase.AccountResolution,
                new TwitchAccessTokenUnavailableException(
                    TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                    TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
                )
            ),
            TwitchEventSubChannelPhase.SubscriptionSetup,
            CancellationToken.None
        );
        var unexpected = TwitchEventSubChannelFailureClassifier.Classify(
            new ApplicationException("programmer defect"),
            TwitchEventSubChannelPhase.Reconciliation,
            CancellationToken.None
        );

        cancellation.Classification.ShouldBe(
            TwitchEventSubChannelFailureClassification.Cancellation
        );
        transientSetup.Phase.ShouldBe(TwitchEventSubChannelPhase.SubscriptionSetup);
        transientSetup.Classification.ShouldBe(
            TwitchEventSubChannelFailureClassification.Transient
        );
        unavailableAccount.Phase.ShouldBe(
            TwitchEventSubChannelPhase.AccountResolution
        );
        unavailableAccount.Classification.ShouldBe(
            TwitchEventSubChannelFailureClassification.Terminal
        );
        unexpected.Classification.ShouldBe(
            TwitchEventSubChannelFailureClassification.Unexpected
        );
    }

    private static RecoveryHarness CreateHarness(
        ScriptedChannelOperations operations,
        int attemptLimit,
        TwitchEventSubChannelStatusStore? sharedStatus = null,
        TwitchBotRuntimeStatusStore? sharedRuntimeStatus = null
    )
    {
        var clock = new FixedTimeProvider(Now);
        var attemptBuilder = new ResiliencePipelineBuilder { TimeProvider = clock };
        var recoveryBuilder = new ResiliencePipelineBuilder { TimeProvider = clock };
        var policy = new EventSubChannelRecoveryPolicy
        {
            AttemptLimit = attemptLimit,
            Delay = TimeSpan.Zero,
            MaximumDelay = TimeSpan.Zero,
            DelayBackoffType = DelayBackoffType.Constant,
            AttemptTimeout = TimeSpan.FromMinutes(1),
        };
        TwitchEventSubChannelRecoveryResilience.ConfigureAttempt(
            attemptBuilder,
            policy
        );
        TwitchEventSubChannelRecoveryResilience.Configure(
            recoveryBuilder,
            policy
        );
        var status = sharedStatus ?? new TwitchEventSubChannelStatusStore();
        var runtimeStatus = sharedRuntimeStatus ?? new TwitchBotRuntimeStatusStore();
        var diagnostics = new RecordingDiagnostics();
        return new RecoveryHarness(
            new TwitchEventSubChannelSession(
                "session-id",
                operations,
                new TwitchEventSubChannelRecoveryPipeline(
                    attemptBuilder.Build(),
                    recoveryBuilder.Build()
                ),
                status.CreateScope(),
                runtimeStatus,
                diagnostics,
                clock
            ),
            status,
            runtimeStatus,
            diagnostics,
            clock
        );
    }

    private static void AssertHealthy(
        TwitchEventSubChannelStatus.Healthy status,
        string channel,
        TwitchEventSubChannelRecoveryTrigger trigger,
        int attempt,
        DateTimeOffset changedAt
    )
    {
        status.Channel.ShouldBe(channel);
        status.Phase.ShouldBe(TwitchEventSubChannelPhase.Reconciliation);
        status.Attempt.ShouldBe(attempt);
        status.ChangedAt.ShouldBe(changedAt);
        status.Trigger.ShouldBe(trigger);
    }

    private static void AssertFailure(
        TwitchEventSubChannelStatus status,
        string channel,
        TwitchEventSubChannelPhase phase,
        TwitchEventSubChannelFailureClassification classification,
        Type failureType,
        int attempt,
        TwitchEventSubChannelRecoveryTrigger trigger,
        TwitchEventSubChannelNextAction nextAction,
        DateTimeOffset changedAt
    )
    {
        status.Channel.ShouldBe(channel);
        status.Phase.ShouldBe(phase);
        status.Attempt.ShouldBe(attempt);
        status.ChangedAt.ShouldBe(changedAt);
        status.Trigger.ShouldBe(trigger);
        status.Match(
            _ => throw new InvalidOperationException("Expected a failed channel state."),
            recovering => (recovering.Failure, recovering.NextAction),
            degraded => (degraded.Failure, degraded.NextAction)
        )
            .ShouldBe(
                (
                    new TwitchEventSubChannelFailure
                    {
                        Classification = classification,
                        FailureType = failureType.FullName!,
                    },
                    nextAction
                )
            );
    }

    private sealed class RecoveryHarness(
        TwitchEventSubChannelSession session,
        TwitchEventSubChannelStatusStore status,
        TwitchBotRuntimeStatusStore runtimeStatus,
        RecordingDiagnostics diagnostics,
        FixedTimeProvider clock
    ) : IAsyncDisposable
    {
        internal TwitchEventSubChannelSession Session { get; } = session;

        internal TwitchEventSubChannelStatusStore Status { get; } = status;

        internal TwitchBotRuntimeStatusStore RuntimeStatus { get; } = runtimeStatus;

        internal RecordingDiagnostics Diagnostics { get; } = diagnostics;

        internal FixedTimeProvider Clock { get; } = clock;

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    private sealed class ScriptedChannelOperations : ITwitchEventSubChannelOperations
    {
        private readonly Dictionary<
            string,
            Queue<Func<CancellationToken, ValueTask<TwitchBotAccount>>>
        > accountScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<Exception>> createFailures = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> createCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> deleteFailures = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> deleteCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> startupDeliveryCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> channelStartedCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> channelStartedFailures = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> completeStopCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> completeStopFailures = new(
            StringComparer.OrdinalIgnoreCase
        );

        internal void EnqueueAccount(
            string channel,
            Func<CancellationToken, ValueTask<TwitchBotAccount>> operation
        ) => GetQueue(accountScripts, channel).Enqueue(operation);

        internal void EnqueueAccountFailure(string channel, Exception exception) =>
            EnqueueAccount(channel, _ => ValueTask.FromException<TwitchBotAccount>(exception));

        internal void EnqueueAccountResult(string channel, string botLogin) =>
            EnqueueAccount(
                channel,
                _ => ValueTask.FromResult(new TwitchBotAccount(botLogin, "secret"))
            );

        internal void EnqueueCreateFailure(string channel, Exception exception) =>
            GetQueue(createFailures, channel).Enqueue(exception);

        internal int CreateCount(string channel) =>
            createCounts.GetValueOrDefault(channel);

        internal void EnqueueDeleteFailure(string channel, Exception exception) =>
            GetQueue(deleteFailures, channel).Enqueue(exception);

        internal int DeleteCount(string channel) =>
            deleteCounts.GetValueOrDefault(channel);

        internal int StartupDeliveryCount(string channel) =>
            startupDeliveryCounts.GetValueOrDefault(channel);

        internal int ChannelStartedCount(string channel) =>
            channelStartedCounts.GetValueOrDefault(channel);

        internal void EnqueueChannelStartedFailure(
            string channel,
            Exception exception
        ) => GetQueue(channelStartedFailures, channel).Enqueue(exception);

        internal int CompleteStopCount(string channel) =>
            completeStopCounts.GetValueOrDefault(channel);

        internal void EnqueueCompleteStopFailure(
            string channel,
            Exception exception
        ) => GetQueue(completeStopFailures, channel).Enqueue(exception);

        public ValueTask<TwitchBotAccount> ResolveAccountAsync(
            string channel,
            CancellationToken cancellationToken
        ) =>
            accountScripts.TryGetValue(channel, out var scripts) && scripts.Count > 0
                ? scripts.Dequeue()(cancellationToken)
                : ValueTask.FromResult(
                    new TwitchBotAccount($"{channel}-bot", $"{channel}-secret")
                );

        public ValueTask<ActiveEventSubSubscription> CreateSubscriptionAsync(
            string channel,
            TwitchBotAccount account,
            string sessionId,
            CancellationToken cancellationToken
        )
        {
            createCounts[channel] = CreateCount(channel) + 1;
            if (
                createFailures.TryGetValue(channel, out var failures)
                && failures.Count > 0
            )
            {
                return ValueTask.FromException<ActiveEventSubSubscription>(
                    failures.Dequeue()
                );
            }

            return ValueTask.FromResult(
                new ActiveEventSubSubscription
                {
                    Channel = channel,
                    SubscriptionId = $"{sessionId}-{channel}",
                    BotLogin = account.Login,
                    AccessToken = account.AccessToken,
                    Readiness = TwitchEventSubSubscriptionReadiness.PendingStartupDelivery,
                }
            );
        }

        public ValueTask DeliverStartupMessageAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            startupDeliveryCounts[channel] = StartupDeliveryCount(channel) + 1;
            return ValueTask.CompletedTask;
        }

        public ValueTask NotifyChannelStartedAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            channelStartedCounts[channel] = ChannelStartedCount(channel) + 1;
            if (
                channelStartedFailures.TryGetValue(channel, out var failures)
                && failures.Count > 0
            )
            {
                return ValueTask.FromException(failures.Dequeue());
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteSubscriptionAsync(
            ActiveEventSubSubscription subscription,
            CancellationToken cancellationToken
        )
        {
            deleteCounts[subscription.Channel] = DeleteCount(subscription.Channel) + 1;
            return deleteFailures.TryGetValue(subscription.Channel, out var failures)
                && failures.Count > 0
                ? ValueTask.FromException(failures.Dequeue())
                : ValueTask.CompletedTask;
        }

        public ValueTask CompleteStopAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            completeStopCounts[channel] = CompleteStopCount(channel) + 1;
            return completeStopFailures.TryGetValue(channel, out var failures)
                && failures.Count > 0
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
    }

    private sealed class RecordingDiagnostics : ITwitchEventSubChannelDiagnosticReporter
    {
        private readonly object gate = new();
        private readonly List<TwitchEventSubChannelDiagnosticReport> reports = [];
        private readonly Queue<Exception> failures = [];
        private readonly Channel<TwitchEventSubChannelStatus> transitions =
            Channel.CreateUnbounded<TwitchEventSubChannelStatus>();

        internal IReadOnlyList<TwitchEventSubChannelStatus> Reports
        {
            get
            {
                lock (gate)
                    return reports.Select(report => report.Status).ToArray();
            }
        }

        internal IReadOnlyList<TwitchEventSubChannelDiagnosticReport> DiagnosticReports
        {
            get
            {
                lock (gate)
                    return reports.ToArray();
            }
        }

        public void Report(TwitchEventSubChannelDiagnosticReport report)
        {
            lock (gate)
            {
                if (failures.TryDequeue(out var failure))
                    throw failure;

                reports.Add(report);
            }

            transitions.Writer.TryWrite(report.Status).ShouldBeTrue();
        }

        internal void EnqueueFailure(Exception failure)
        {
            lock (gate)
                failures.Enqueue(failure);
        }

        internal void Clear()
        {
            lock (gate)
                reports.Clear();
            while (transitions.Reader.TryRead(out _)) { }
        }

        internal ValueTask<TwitchEventSubChannelStatus> NextAsync() =>
            transitions.Reader.ReadAsync();
    }

    private sealed class RecordingChatObserver : ITwitchChatMessageObserver
    {
        internal List<string> Channels { get; } = [];

        public ValueTask MessageReceivedAsync(
            TwitchChatMessage message,
            CancellationToken cancellationToken
        )
        {
            Channels.Add(message.Channel);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedCommandResponseSender : ITwitchCommandResponseSender
    {
        public ValueTask SendAsync(
            TwitchChatMessage sourceMessage,
            TwitchCommandResponse response,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("No command response was expected.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object gate = new();
        private readonly HashSet<ManualTimer> timers = [];
        private DateTimeOffset now = initialNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
                return now;
        }

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

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
            lock (gate)
            {
                now = now.Add(duration);
                due = timers.Where(timer => timer.IsDue(now)).ToArray();
            }

            foreach (var timer in due)
                timer.Fire();
        }

        private void Add(ManualTimer timer)
        {
            lock (gate)
                timers.Add(timer);
        }

        private void Remove(ManualTimer timer)
        {
            lock (gate)
                timers.Remove(timer);
        }

        private sealed class ManualTimer(
            FixedTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan period = Timeout.InfiniteTimeSpan;
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;

            internal bool IsDue(DateTimeOffset current) =>
                !disposed && dueAt <= current;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (disposed)
                        return false;

                    this.period = period;
                    dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner.now.Add(dueTime);
                    owner.Add(this);
                    return true;
                }
            }

            internal void Fire()
            {
                lock (owner.gate)
                {
                    if (!IsDue(owner.now))
                        return;

                    dueAt =
                        period == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner.now.Add(period);
                }

                callback(state);
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    if (disposed)
                        return;

                    disposed = true;
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

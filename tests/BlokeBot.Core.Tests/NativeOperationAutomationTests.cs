using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class NativeOperationAutomationTests
{
    private static readonly DateTimeOffset _start = new(2026, 8, 7, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Readiness_ListsNativeSourcesWithSubscriptionTypesAndBackingScopes()
    {
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain("channel:read:polls");
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain(
            "channel:read:predictions"
        );

        await using var fixture = await NativeFixture.CreateAsync();
        var tokens = new FixedBroadcasterTokens(
            new TokenStatus.MissingScopes(
                "token",
                new("streamer-id", "streamer", OAuthScopeSet.Empty),
                [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                [],
                ["channel:read:polls"]
            )
        );
        var readiness = new TwitchEventSourceReadinessService(
            fixture.Database,
            fixture.Catalog,
            fixture.FlowRuntime,
            tokens,
            AutomationRuntimeTests.ObservationRuntimeFor(
                fixture.Database,
                fixture.FlowRuntime,
                fixture.Catalog,
                fixture.Clock,
                tokens
            )
        );

        var outcome = await readiness.LoadAsync(new(fixture.HostId), CancellationToken.None);

        var available = outcome.ShouldBeOfType<TwitchEventSourceReadinessOutcome.Available>();
        Source(available, "shoutout-sent").SubscriptionTypes.ShouldBe("channel.shoutout.create");
        Source(available, "shoutout-received")
            .SubscriptionTypes.ShouldBe("channel.shoutout.receive");
        Source(available, "shoutout-sent").RequiredBroadcasterScopes.ShouldBeEmpty();
        _ = Source(available, "shoutout-sent")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        Source(available, "poll-started").SubscriptionTypes.ShouldBe("channel.poll.begin");
        Source(available, "poll-started")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.MissingScopes>()
            .Scopes.ShouldBe(["channel:read:polls"]);
        Source(available, "prediction-locked")
            .SubscriptionTypes.ShouldBe("channel.prediction.lock");
        Source(available, "prediction-locked")
            .RequiredBroadcasterScopes.ShouldBe(["channel:read:predictions"]);
        _ = Source(available, "prediction-locked")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();

        await fixture.SetFeatureAsync(HostFeatureFlags.Polls, enabled: false);
        var withoutPolls = (TwitchEventSourceReadinessOutcome.Available)
            await readiness.LoadAsync(new(fixture.HostId), CancellationToken.None);
        withoutPolls
            .Sources.Select(static source => source.DefinitionId.Value)
            .ShouldNotContain("poll-started");
        withoutPolls
            .Sources.Select(static source => source.DefinitionId.Value)
            .ShouldContain("shoutout-sent");
    }

    [Test]
    public async Task ShoutoutSources_RouteByDirectionAndDeduplicateByReceipt()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var sentSource = Node("shoutout-sent", "{}");
        var sentAction = Node("send-chat", """{"message":"Sent shoutout"}""");
        var receivedSource = Node("shoutout-received", "{}");
        var receivedAction = Node("send-chat", """{"message":"Received shoutout"}""");
        var sentFlowId = await fixture.SaveAsync(
            [sentSource, sentAction],
            [Edge(sentSource, "flow", sentAction)]
        );
        var receivedFlowId = await fixture.SaveAsync(
            [receivedSource, receivedAction],
            [Edge(receivedSource, "flow", receivedAction)]
        );

        await fixture.Runtime.ShoutoutOccurredAsync(
            fixture.Shoutout("message-1", EventSubShoutoutDirection.Sent),
            CancellationToken.None
        );
        await fixture.Runtime.ShoutoutOccurredAsync(
            fixture.Shoutout("message-1", EventSubShoutoutDirection.Sent),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["Sent shoutout"]);
        var sentRun = await CompletedRunAsync(fixture, sentFlowId, sentSource, sentAction);
        var sentContext = RestoredContext(sentRun);
        sentContext.Actor.ShouldNotBeNull().Login.ShouldBe("partner");
        sentContext.Actor.TwitchUserId.ShouldBe("partner-id");
        SafeVariable(sentContext, "viewer_count").ShouldBe(new AutomationValue.Number(42));
        await AssertReceiptAsync(fixture, sentSource, "message-1");

        await fixture.Runtime.ShoutoutOccurredAsync(
            fixture.Shoutout("message-2", EventSubShoutoutDirection.Received),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(2);
        fixture.Chat.Messages.ShouldBe(["Sent shoutout", "Received shoutout"]);
        var receivedRun = await CompletedRunAsync(
            fixture,
            receivedFlowId,
            receivedSource,
            receivedAction
        );
        receivedRun.Id.ShouldNotBe(sentRun.Id);
        receivedRun.SourceOccurrenceId.ShouldNotBe(sentRun.SourceOccurrenceId);
        var receivedContext = RestoredContext(receivedRun);
        receivedContext.Actor.ShouldNotBeNull().Login.ShouldBe("partner");
        receivedContext.Actor.TwitchUserId.ShouldBe("partner-id");
        SafeVariable(receivedContext, "viewer_count").ShouldBe(new AutomationValue.Number(42));
        await AssertReceiptAsync(fixture, receivedSource, "message-2");
        (await fixture.ReceiptCountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task PollSources_RouteByStageWithBoundedContext()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var startedSource = Node("poll-started", "{}");
        var startedAction = Node("send-chat", """{"message":"Poll started"}""");
        var endedSource = Node("poll-ended", "{}");
        var endedAction = Node("send-chat", """{"message":"Poll ended"}""");
        var startedFlowId = await fixture.SaveAsync(
            [startedSource, startedAction],
            [Edge(startedSource, "flow", startedAction)]
        );
        var endedFlowId = await fixture.SaveAsync(
            [endedSource, endedAction],
            [Edge(endedSource, "flow", endedAction)]
        );

        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("message-1", EventSubPollStage.Begin),
            CancellationToken.None
        );
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("message-2", EventSubPollStage.Progress, votes: 3),
            CancellationToken.None
        );
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("message-3", EventSubPollStage.End, votes: 7, status: "completed"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(2);
        fixture.Chat.Messages.ShouldBe(["Poll started", "Poll ended"]);
        var startedRun = await CompletedRunAsync(
            fixture,
            startedFlowId,
            startedSource,
            startedAction
        );
        var endedRun = await CompletedRunAsync(fixture, endedFlowId, endedSource, endedAction);
        endedRun.SourceOccurrenceId.ShouldNotBe(startedRun.SourceOccurrenceId);
        var startedContext = RestoredContext(startedRun);
        var endedContext = RestoredContext(endedRun);
        SafeVariable(startedContext, "poll_title")
            .ShouldBe(new AutomationValue.Text("Favourite game?"));
        SafeVariable(startedContext, "poll_status").ShouldBe(new AutomationValue.Text("active"));
        SafeVariable(endedContext, "poll_title")
            .ShouldBe(new AutomationValue.Text("Favourite game?"));
        SafeVariable(endedContext, "poll_status").ShouldBe(new AutomationValue.Text("completed"));
        SafeVariable(endedContext, "total_votes").ShouldBe(new AutomationValue.Number(7));
        await AssertReceiptAsync(fixture, startedSource, "message-1");
        await AssertReceiptAsync(fixture, endedSource, "message-3");
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("message-3", EventSubPollStage.End, votes: 7, status: "completed"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(2);
        fixture.Chat.Messages.ShouldBe(["Poll started", "Poll ended"]);
    }

    [Test]
    public async Task PredictionSources_RouteByStageWithWinningOutcomeContext()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var lockedSource = Node("prediction-locked", "{}");
        var lockedAction = Node("send-chat", """{"message":"Prediction locked"}""");
        var endedSource = Node("prediction-ended", "{}");
        var endedAction = Node("send-chat", """{"message":"Prediction ended"}""");
        var lockedFlowId = await fixture.SaveAsync(
            [lockedSource, lockedAction],
            [Edge(lockedSource, "flow", lockedAction)]
        );
        var endedFlowId = await fixture.SaveAsync(
            [endedSource, endedAction],
            [Edge(endedSource, "flow", endedAction)]
        );

        await fixture.Runtime.PredictionChangedAsync(
            fixture.Prediction("message-1", EventSubPredictionStage.Lock, "locked"),
            CancellationToken.None
        );
        await fixture.Runtime.PredictionChangedAsync(
            fixture.Prediction(
                "message-2",
                EventSubPredictionStage.End,
                "resolved",
                winningOutcomeId: "yes"
            ),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(2);
        fixture.Chat.Messages.ShouldBe(["Prediction locked", "Prediction ended"]);
        var lockedRun = await CompletedRunAsync(fixture, lockedFlowId, lockedSource, lockedAction);
        var endedRun = await CompletedRunAsync(fixture, endedFlowId, endedSource, endedAction);
        endedRun.SourceOccurrenceId.ShouldNotBe(lockedRun.SourceOccurrenceId);
        var lockedContext = RestoredContext(lockedRun);
        var endedContext = RestoredContext(endedRun);
        SafeVariable(lockedContext, "prediction_title")
            .ShouldBe(new AutomationValue.Text("Will we win?"));
        SafeVariable(lockedContext, "prediction_status")
            .ShouldBe(new AutomationValue.Text("locked"));
        lockedContext.Variables.ForExecution().ShouldNotContainKey(new("winning_outcome_id"));
        lockedContext.Variables.ForExecution().ShouldNotContainKey(new("winning_outcome_title"));
        SafeVariable(endedContext, "prediction_title")
            .ShouldBe(new AutomationValue.Text("Will we win?"));
        SafeVariable(endedContext, "prediction_status")
            .ShouldBe(new AutomationValue.Text("resolved"));
        SafeVariable(endedContext, "winning_outcome_id").ShouldBe(new AutomationValue.Text("yes"));
        SafeVariable(endedContext, "winning_outcome_title")
            .ShouldBe(new AutomationValue.Text("Yes"));
        await AssertReceiptAsync(fixture, lockedSource, "message-1");
        await AssertReceiptAsync(fixture, endedSource, "message-2");
        await fixture.Runtime.PredictionChangedAsync(
            fixture.Prediction(
                "message-2",
                EventSubPredictionStage.End,
                "resolved",
                winningOutcomeId: "yes"
            ),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(2);
        (await fixture.ReceiptCountAsync()).ShouldBe(2);
        fixture.Chat.Messages.ShouldBe(["Prediction locked", "Prediction ended"]);
    }

    [Test]
    public async Task DualGates_BlockDeliveriesBeforeReceiptsAndNeverReplay()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("poll-started", "{}");
        var action = Node("send-chat", """{"message":"Poll!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.SetFeatureAsync(HostFeatureFlags.Automations, enabled: false);
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("suppressed-1", EventSubPollStage.Begin),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);

        await fixture.SetFeatureAsync(HostFeatureFlags.Automations, enabled: true);
        await fixture.SetFeatureAsync(HostFeatureFlags.Polls, enabled: false);
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("suppressed-2", EventSubPollStage.Begin),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);

        await fixture.SetFeatureAsync(HostFeatureFlags.Polls, enabled: true);
        (await fixture.RunCountAsync()).ShouldBe(0);
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("fresh-1", EventSubPollStage.Begin),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);

        await fixture.SetFeatureAsync(HostFeatureFlags.RaidCollaboration, enabled: false);
        await fixture.Runtime.ShoutoutOccurredAsync(
            fixture.Shoutout("suppressed-3", EventSubShoutoutDirection.Sent),
            CancellationToken.None
        );
        await fixture.SetFeatureAsync(HostFeatureFlags.Predictions, enabled: false);
        await fixture.Runtime.PredictionChangedAsync(
            fixture.Prediction("suppressed-4", EventSubPredictionStage.Begin, "active"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task DisabledBackingFeature_ListsSavedFlowAndReturnsActionableValidation()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("prediction-started", "{}");
        var action = Node("send-chat", """{"message":"Prediction!"}""");
        var edge = Edge(source, "flow", action);
        var draft = Draft(fixture.HostId, [source, action], [edge]) with
        {
            Name = "Prediction alerts",
            IsEnabled = false,
        };
        var saved = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        draft = draft with { Id = saved.FlowId };
        var original = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                saved.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();

        await fixture.SetFeatureAsync(HostFeatureFlags.Predictions, enabled: false);
        var listed = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        listed.Flows.ShouldBeEmpty();
        var repair = listed.AuthoringEntries.ShouldHaveSingleItem();
        repair.Id.ShouldBe(saved.FlowId);
        repair.Name.ShouldBe(draft.Name);
        repair.IsEnabled.ShouldBeFalse();
        repair.RequiresJson.ShouldBeFalse();
        repair.Reason.ShouldNotBeNullOrWhiteSpace();
        var authoring = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                saved.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        authoring.Snapshot.Draft.Id.ShouldBe(saved.FlowId);
        AssertSameGraph(original.Original, authoring.Original);
        authoring.Errors.ShouldContain(error =>
            error.NodeId == source.Id && error.Code == "capability-unavailable"
        );
        var validation = (
            await fixture.Flows.ValidateDraftAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
        validation.Errors.ShouldContain(static error => error.Code == "capability-unavailable");
        var save = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        save.Errors.ShouldContain(static error => error.Code == "capability-unavailable");
        var enable = (
            await fixture.Flows.SetEnabledAsync(
                new(fixture.HostId),
                saved.FlowId,
                enabled: true,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Invalid>();
        enable.Errors.ShouldContain(static error => error.Code == "capability-unavailable");
        var after = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                saved.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        after.Original.Nodes.ShouldBe(authoring.Original.Nodes, ignoreOrder: true);
        after.Original.Edges.ShouldBe(authoring.Original.Edges, ignoreOrder: true);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var retained = await db.AutomationFlows.SingleAsync();
        retained.Id.ShouldBe(saved.FlowId.Value);
        retained.IsEnabled.ShouldBeFalse();

        await fixture.SetFeatureAsync(HostFeatureFlags.Predictions, enabled: true);
        var restored = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        restored.AuthoringEntries.ShouldBeEmpty();
        var normal = restored.Flows.ShouldHaveSingleItem().Draft;
        normal.Id.ShouldBe(saved.FlowId);
        normal.Name.ShouldBe(draft.Name);
        normal.IsEnabled.ShouldBeFalse();
        var reopened = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                saved.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        reopened.Errors.ShouldBeEmpty();
        AssertSameGraph(original.Original, reopened.Original);
    }

    [Test]
    public async Task Deliveries_AreHostIsolated()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var otherHostId = await fixture.SeedHostAsync("other", NativeFixture.AllFeatures);
        var source = Node("shoutout-received", "{}");
        var action = Node("send-chat", """{"message":"Mine"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var otherSource = Node("shoutout-received", "{}");
        var otherAction = Node("send-chat", """{"message":"Other"}""");
        _ = await fixture.SaveAsync(
            [otherSource, otherAction],
            [Edge(otherSource, "flow", otherAction)],
            otherHostId
        );

        await fixture.Runtime.ShoutoutOccurredAsync(
            fixture.Shoutout("message-1", EventSubShoutoutDirection.Received, hostId: otherHostId),
            CancellationToken.None
        );

        (await fixture.RunCountAsync(otherHostId)).ShouldBe(1);
        (await fixture.RunCountAsync()).ShouldBe(0);
        fixture.Chat.Messages.ShouldBe(["Other"]);
    }

    [Test]
    public async Task SendShoutout_TargetsTheTriggeringActorAndPreservesTypedFailures()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("incoming-raid", """{"minimum-viewers":0}""");
        var action = Node("send-shoutout", "{}");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-1"),
            CancellationToken.None
        );
        fixture.Shoutouts.Sent.ShouldBe([(fixture.HostId, "raider")]);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Completed]);

        fixture.Shoutouts.NextOutcome = new ShoutoutOperationOutcome.TargetOffline("raider");
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-2"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("shoutout-target-offline");

        fixture.Shoutouts.NextOutcome = new ShoutoutOperationOutcome.CooldownActive(
            _start.UtcDateTime
        );
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-3"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("shoutout-cooldown-active");

        fixture.Shoutouts.NextOutcome = new ShoutoutOperationOutcome.NotReady(
            "Connect the bot account with shoutout permissions."
        );
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-4"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("shoutout-not-ready");
    }

    [Test]
    public async Task StartPoll_InterpolatesTitleAndChoicesIntoTheValidatedDraft()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("incoming-raid", """{"minimum-viewers":0}""");
        var action = Node(
            "start-poll",
            """
            {"title":"Welcome ${actor.login}?","choices":"Yes\nNo","duration-seconds":60,"channel-points-per-vote":500}
            """
        );
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-1"),
            CancellationToken.None
        );

        var (hostId, draft) = fixture.Polls.Started.ShouldHaveSingleItem();
        hostId.ShouldBe(fixture.HostId);
        draft.Title.ShouldBe("Welcome raider?");
        draft.Choices.ShouldBe(["Yes", "No"]);
        draft.DurationSeconds.ShouldBe(60);
        draft.ChannelPointsVotingEnabled.ShouldBeTrue();
        draft.ChannelPointsPerVote.ShouldBe(500);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Completed]);

        fixture.Polls.NextStartOutcome = new PollOperationOutcome.ActivePollExists();
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-2"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("poll-already-active");

        fixture.Polls.NextStartOutcome = new PollOperationOutcome.InvalidTemplate(
            "Poll titles must be 1–60 characters."
        );
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-3"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("poll-invalid");
    }

    [Test]
    public async Task EndPoll_NeverConfirmsExternallyStartedPolls()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("stream-offline", "{}");
        var action = Node("end-poll", "{}");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.StreamOfflineAsync(
            fixture.StreamOffline("offline-1"),
            CancellationToken.None
        );
        fixture.Polls.Ended.ShouldBe([(fixture.HostId, false)]);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Completed]);

        fixture.Polls.NextEndOutcome = new PollOperationOutcome.ConfirmationRequired();
        await fixture.Runtime.StreamOfflineAsync(
            fixture.StreamOffline("offline-2"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("poll-externally-started");
    }

    [Test]
    public async Task CreateClip_MapsDelayModeAndPreservesTypedFailures()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("incoming-raid", """{"minimum-viewers":0}""");
        var action = Node("create-clip", """{"delay-mode":"stream-delay"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-1"),
            CancellationToken.None
        );
        fixture.ClipsMarkers.Clips.ShouldBe([(fixture.HostId, true)]);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Completed]);

        fixture.ClipsMarkers.NextClipOutcome = new ClipMarkerOperationOutcome.Offline();
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-2"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("channel-offline");

        fixture.ClipsMarkers.NextClipOutcome = new ClipMarkerOperationOutcome.NotReady(
            "Reconnect the selected channel's Twitch integration."
        );
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-3"),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("broadcaster-authorization-missing");
    }

    [Test]
    public async Task CreateMarker_InterpolatesTheDescriptionAndBlocksSensitiveValues()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("incoming-raid", """{"minimum-viewers":0}""");
        var action = Node("create-marker", """{"description":"Raid from ${actor.login}"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-1"),
            CancellationToken.None
        );
        fixture.ClipsMarkers.Markers.ShouldBe([(fixture.HostId, "Raid from raider")]);

        var cheerSource = Node("cheer", """{"minimum-bits":1}""");
        var sensitiveAction = Node("create-marker", """{"description":"${cheer_message}"}""");
        _ = await fixture.SaveAsync(
            [cheerSource, sensitiveAction],
            [Edge(cheerSource, "flow", sensitiveAction)]
        );
        await fixture.Runtime.CheerReceivedAsync(
            fixture.Cheer("cheer-1", bits: 100),
            CancellationToken.None
        );

        fixture.ClipsMarkers.Markers.Count.ShouldBe(1);
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("sensitive-output-blocked");
    }

    [Test]
    public async Task PredictionActions_InvokeTheLifecycleOperationsWithTypedOutcomes()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var startSource = Node("incoming-raid", """{"minimum-viewers":0}""");
        var startAction = Node(
            "start-prediction",
            """{"title":"Win the next game?","outcomes":"Yes\nNo","window-seconds":120}"""
        );
        _ = await fixture.SaveAsync(
            [startSource, startAction],
            [Edge(startSource, "flow", startAction)]
        );
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-1"),
            CancellationToken.None
        );
        var (startHostId, draft) = fixture.Predictions.Started.ShouldHaveSingleItem();
        startHostId.ShouldBe(fixture.HostId);
        draft.Title.ShouldBe("Win the next game?");
        draft.Outcomes.ShouldBe(["Yes", "No"]);
        draft.PredictionWindowSeconds.ShouldBe(120);

        var lockSource = Node("stream-offline", "{}");
        var lockAction = Node("lock-prediction", "{}");
        _ = await fixture.SaveAsync(
            [lockSource, lockAction],
            [Edge(lockSource, "flow", lockAction)]
        );
        await fixture.Runtime.StreamOfflineAsync(
            fixture.StreamOffline("offline-1"),
            CancellationToken.None
        );
        fixture.Predictions.Locked.ShouldBe([(fixture.HostId, true)]);

        var cancelSource = Node("poll-started", "{}");
        var cancelAction = Node("cancel-prediction", "{}");
        _ = await fixture.SaveAsync(
            [cancelSource, cancelAction],
            [Edge(cancelSource, "flow", cancelAction)]
        );
        await fixture.Runtime.PollChangedAsync(
            fixture.Poll("message-1", EventSubPollStage.Begin),
            CancellationToken.None
        );
        fixture.Predictions.Cancelled.ShouldBe([(fixture.HostId, true)]);

        var resolveSource = Node("prediction-locked", "{}");
        var resolveAction = Node(
            "resolve-prediction",
            """{"winning-outcome-id":"${prediction_id}-yes"}"""
        );
        _ = await fixture.SaveAsync(
            [resolveSource, resolveAction],
            [Edge(resolveSource, "flow", resolveAction)]
        );
        await fixture.Runtime.PredictionChangedAsync(
            fixture.Prediction("message-2", EventSubPredictionStage.Lock, "locked"),
            CancellationToken.None
        );
        fixture.Predictions.Resolved.ShouldBe([(fixture.HostId, "prediction-1-yes", true)]);

        fixture.Predictions.NextOutcome = new PredictionOperationOutcome.InvalidOutcome();
        await fixture.Runtime.PredictionChangedAsync(
            fixture.Prediction(
                "message-3",
                EventSubPredictionStage.Lock,
                "locked",
                predictionId: "prediction-2"
            ),
            CancellationToken.None
        );
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("prediction-outcome-invalid");
    }

    [Test]
    public async Task Actions_AreBlockedWithZeroProviderCallsWhileTheBackingFeatureIsOff()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        var source = Node("stream-online", "{}");
        var action = Node("create-clip", """{"delay-mode":"immediate"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.SetFeatureAsync(HostFeatureFlags.ClipsAndMarkers, enabled: false);
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("stream-1"),
            CancellationToken.None
        );

        // The stream-online delivery itself is admitted, but the flow requires Clips & markers,
        // so dispatch blocks it before any run row or provider call.
        fixture.ClipsMarkers.Clips.ShouldBeEmpty();
        (await fixture.RunCountAsync()).ShouldBe(0);

        await fixture.SetFeatureAsync(HostFeatureFlags.ClipsAndMarkers, enabled: true);
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("stream-2"),
            CancellationToken.None
        );
        fixture.ClipsMarkers.Clips.ShouldBe([(fixture.HostId, false)]);
    }

    [Test]
    public async Task Executor_EnforcesTheBackingGateEvenWhenARunIsAlreadyInFlight()
    {
        await using var fixture = await NativeFixture.CreateAsync();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var host = await db.Hosts.AsNoTracking().SingleAsync(h => h.Id == fixture.HostId);
        var context = NativeOperationAutomationContext.Poll(
            host,
            AutomationDefinitionIds.PollStartedSource,
            fixture.Poll("message-1", EventSubPollStage.Begin),
            _start
        );
        await fixture.SetFeatureAsync(HostFeatureFlags.RaidCollaboration, enabled: false);

        var outcome = await fixture.Executor.ExecuteAsync(
            new(fixture.HostId),
            new SendShoutoutActionConfiguration(),
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource>.Empty,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue>.Empty,
            context,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<AutomationActionOutcome.Failed>().Code.ShouldBe("feature-disabled");
        fixture.Shoutouts.Sent.ShouldBeEmpty();
    }

    private static async Task<AutomationFlowRun> CompletedRunAsync(
        NativeFixture fixture,
        AutomationFlowId flowId,
        AutomationFlowDraftNode source,
        AutomationFlowDraftNode action
    )
    {
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db
            .AutomationFlowRuns.AsNoTracking()
            .Include(run => run.NodeRuns)
            .SingleAsync(run => run.HostId == fixture.HostId && run.FlowId == flowId.Value);
        run.SourceNodeId.ShouldBe(source.Id.Value);
        run.SourceDefinitionId.ShouldBe(source.Definition.TypeId);
        run.Status.ShouldBe(AutomationFlowRunStatus.Completed);
        _ = run.CompletedAtUtc.ShouldNotBeNull();
        run.ExecutionLeaseId.ShouldBeNull();
        var effect = run
            .NodeRuns.Where(node => node.NodeId == action.Id.Value)
            .ShouldHaveSingleItem();
        effect.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
        effect.OutcomeCode.ShouldBe("action-succeeded");
        return run;
    }

    private static AutomationContext RestoredContext(AutomationFlowRun run)
    {
        var context = AutomationRuntimeSerialization
            .RestoreContext(run.ContextSchemaVersion, run.ContextJson)
            .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
            .Context;
        context.Event.SourceDefinitionId.Value.ShouldBe(run.SourceDefinitionId);
        context.Event.OccurrenceId.ShouldBe(run.SourceOccurrenceId);
        return context;
    }

    private static AutomationValue SafeVariable(AutomationContext context, string name)
    {
        var variable = context.Variables.ForExecution()[new(name)];
        variable.Sensitivity.ShouldBe(AutomationDataSensitivity.Safe);
        context.Variables.SafeForExternalUse()[new(name)].ShouldBe(variable.Value);
        return variable.Value;
    }

    private static async Task AssertReceiptAsync(
        NativeFixture fixture,
        AutomationFlowDraftNode source,
        string messageId
    )
    {
        await using var db = await fixture.Database.CreateDbContextAsync();
        var sourceId = source.Definition.TypeId;
        var receipt = await db
            .AutomationEventReceipts.AsNoTracking()
            .SingleAsync(receipt =>
                receipt.HostId == fixture.HostId
                && receipt.SourceDefinitionId == sourceId
                && receipt.ProviderMessageId == messageId
            );
        receipt.ClaimedAtUtc.ShouldBe(fixture.Clock.GetUtcNow().UtcDateTime);
        receipt.ExpiresAtUtc.ShouldBeGreaterThan(receipt.ClaimedAtUtc);
    }

    private static void AssertSameGraph(
        AutomationAuthoredFlowSource expected,
        AutomationAuthoredFlowSource actual
    )
    {
        actual.SchemaVersion.ShouldBe(expected.SchemaVersion);
        actual.Canvas.ShouldBe(expected.Canvas);
        actual.Nodes.OrderBy(node => node.Id).ShouldBe(expected.Nodes.OrderBy(node => node.Id));
        actual.Edges.OrderBy(edge => edge.Id).ShouldBe(expected.Edges.OrderBy(edge => edge.Id));
    }

    private static TwitchEventSourceReadiness Source(
        TwitchEventSourceReadinessOutcome.Available available,
        string definitionId
    ) => available.Sources.Single(source => source.DefinitionId.Value == definitionId);

    private static AutomationFlowDraft Draft(
        int hostId,
        ImmutableArray<AutomationFlowDraftNode> nodes,
        ImmutableArray<AutomationFlowDraftEdge> edges
    ) => new(null, new(hostId), "Flow", 1, true, nodes, edges);

    private static AutomationFlowDraftNode Node(
        string type,
        string json,
        AutomationInputBindingMode messageMode = AutomationInputBindingMode.Fixed
    )
    {
        using var document = JsonDocument.Parse(json);
        return new(
            new(Guid.NewGuid()),
            new(type, 1, document.RootElement.Clone()),
            AutomationExpressionLanguage.CurrentVersion,
            AutomationNodeFailurePolicy.Stop,
            type == "send-chat"
                ? ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationInputBinding
                >.Empty.Add(new("message"), new(AutomationInputBindingMode.Fixed, Expression: null))
                : ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
        );
    }

    private static AutomationFlowDraftEdge Edge(
        AutomationFlowDraftNode source,
        string sourcePort,
        AutomationFlowDraftNode target
    ) =>
        new(
            Guid.NewGuid(),
            AutomationEdgeKind.Flow,
            source.Id,
            new(sourcePort),
            target.Id,
            new("flow")
        );

    private sealed class FixedBroadcasterTokens(TokenStatus status)
        : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) => Task.FromResult(status);

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(static _ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
    }

    private sealed class RecordingShoutoutOperations : IShoutoutDashboardOperations
    {
        internal List<(int HostId, string TargetLogin)> Sent { get; } = [];

        internal ShoutoutOperationOutcome NextOutcome { get; set; } =
            new ShoutoutOperationOutcome.Sent("raider");

        public Task<ShoutoutOperationOutcome> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            Sent.Add((hostId, targetLogin));
            return Task.FromResult(NextOutcome);
        }

        public Task<ShoutoutDashboardState> LoadAsync(
            int hostId,
            string? targetLogin,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingClipMarkerOperations : IClipMarkerDashboardOperations
    {
        internal List<(int HostId, bool HasDelay)> Clips { get; } = [];

        internal List<(int HostId, string Description)> Markers { get; } = [];

        internal ClipMarkerOperationOutcome NextClipOutcome { get; set; } =
            new ClipMarkerOperationOutcome.ClipPending(
                new(new(1), "Pending", null, null, null, null, null, null, DateTime.UtcNow, null)
            );

        internal ClipMarkerOperationOutcome NextMarkerOutcome { get; set; } =
            new ClipMarkerOperationOutcome.MarkerCreated(
                new(
                    new(1),
                    "Succeeded",
                    "marker-1",
                    "marker",
                    10,
                    null,
                    null,
                    null,
                    DateTime.UtcNow
                )
            );

        public Task<ClipMarkerOperationOutcome> CreateClipAsync(
            int hostId,
            bool hasDelay,
            CancellationToken cancellationToken
        )
        {
            Clips.Add((hostId, hasDelay));
            return Task.FromResult(NextClipOutcome);
        }

        public Task<ClipMarkerOperationOutcome> CreateMarkerAsync(
            int hostId,
            string description,
            CancellationToken cancellationToken
        )
        {
            Markers.Add((hostId, description));
            return Task.FromResult(NextMarkerOutcome);
        }

        public Task<ClipMarkerDashboardState> LoadAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ClipMarkerOperationOutcome> RetryClipAsync(
            int hostId,
            ClipAttemptReference attempt,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ClipMarkerOperationOutcome> RetryMarkerAsync(
            int hostId,
            StreamMarkerAttemptReference attempt,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingPollOperations : IPollAutomationOperations
    {
        internal List<(int HostId, PollTemplateDraft Draft)> Started { get; } = [];

        internal List<(int HostId, bool ConfirmedExternal)> Ended { get; } = [];

        internal PollOperationOutcome NextStartOutcome { get; set; } =
            new PollOperationOutcome.Started(
                new("poll-1", "Question", [], "Active", false, DateTime.UtcNow, null, null)
            );

        internal PollOperationOutcome NextEndOutcome { get; set; } =
            new PollOperationOutcome.Ended(
                new("poll-1", "Question", [], "Terminated", false, DateTime.UtcNow, null, null)
            );

        public Task<PollOperationOutcome> StartAsync(
            int hostId,
            PollTemplateDraft draft,
            CancellationToken cancellationToken
        )
        {
            Started.Add((hostId, draft));
            return Task.FromResult(NextStartOutcome);
        }

        public Task<PollOperationOutcome> EndAsync(
            int hostId,
            bool confirmedExternal,
            CancellationToken cancellationToken
        )
        {
            Ended.Add((hostId, confirmedExternal));
            return Task.FromResult(NextEndOutcome);
        }
    }

    private sealed class RecordingPredictionOperations : IPredictionAutomationOperations
    {
        internal List<(int HostId, PredictionTemplateDraft Draft)> Started { get; } = [];

        internal List<(int HostId, bool Confirmed)> Locked { get; } = [];

        internal List<(int HostId, bool Confirmed)> Cancelled { get; } = [];

        internal List<(int HostId, string WinningOutcomeId, bool Confirmed)> Resolved { get; } = [];

        internal PredictionOperationOutcome NextOutcome { get; set; } =
            new PredictionOperationOutcome.Updated(
                new("prediction-1", "Question", [], "Locked", false, DateTime.UtcNow, null, null)
            );

        public Task<PredictionOperationOutcome> StartAsync(
            int hostId,
            PredictionTemplateDraft draft,
            CancellationToken cancellationToken
        )
        {
            Started.Add((hostId, draft));
            return Task.FromResult<PredictionOperationOutcome>(
                new PredictionOperationOutcome.Started(
                    new(
                        "prediction-1",
                        draft.Title,
                        [],
                        "Active",
                        false,
                        DateTime.UtcNow,
                        null,
                        null
                    )
                )
            );
        }

        public Task<PredictionOperationOutcome> LockAsync(
            int hostId,
            bool confirmed,
            CancellationToken cancellationToken
        )
        {
            Locked.Add((hostId, confirmed));
            return Task.FromResult(NextOutcome);
        }

        public Task<PredictionOperationOutcome> CancelAsync(
            int hostId,
            bool confirmed,
            CancellationToken cancellationToken
        )
        {
            Cancelled.Add((hostId, confirmed));
            return Task.FromResult(NextOutcome);
        }

        public Task<PredictionOperationOutcome> ResolveAsync(
            int hostId,
            string winningOutcomeId,
            bool confirmed,
            CancellationToken cancellationToken
        )
        {
            Resolved.Add((hostId, winningOutcomeId, confirmed));
            return Task.FromResult(NextOutcome);
        }
    }

    private sealed class NativeFixture : IAsyncDisposable
    {
        internal const HostFeatureFlags AllFeatures =
            HostFeatureFlags.Automations
            | HostFeatureFlags.CustomCommands
            | HostFeatureFlags.RaidCollaboration
            | HostFeatureFlags.Polls
            | HostFeatureFlags.ClipsAndMarkers
            | HostFeatureFlags.Predictions;

        private NativeFixture(
            SqliteBlokeBotDbFactory database,
            MutableTimeProvider clock,
            RecordingChatSender chat,
            RecordingShoutoutOperations shoutouts,
            RecordingClipMarkerOperations clipsMarkers,
            RecordingPollOperations polls,
            RecordingPredictionOperations predictions,
            AutomationCatalogService catalog,
            AutomationActionExecutor executor,
            AutomationRuntimeService flowRuntime,
            AutomationFlowService flows,
            TwitchEventAutomationRuntime runtime
        )
        {
            Database = database;
            Clock = clock;
            Chat = chat;
            Shoutouts = shoutouts;
            ClipsMarkers = clipsMarkers;
            Polls = polls;
            Predictions = predictions;
            Catalog = catalog;
            Executor = executor;
            FlowRuntime = flowRuntime;
            Flows = flows;
            Runtime = runtime;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal MutableTimeProvider Clock { get; }
        internal RecordingChatSender Chat { get; }
        internal RecordingShoutoutOperations Shoutouts { get; }
        internal RecordingClipMarkerOperations ClipsMarkers { get; }
        internal RecordingPollOperations Polls { get; }
        internal RecordingPredictionOperations Predictions { get; }
        internal AutomationCatalogService Catalog { get; }
        internal AutomationActionExecutor Executor { get; }
        internal AutomationRuntimeService FlowRuntime { get; }
        internal AutomationFlowService Flows { get; }
        internal TwitchEventAutomationRuntime Runtime { get; }
        internal int HostId { get; private set; }

        internal static async Task<NativeFixture> CreateAsync(
            HostFeatureFlags hostFeatures = AllFeatures,
            Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]? interceptors = null
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync(interceptors ?? []);
            var clock = new MutableTimeProvider(_start);
            var chat = new RecordingChatSender();
            var shoutouts = new RecordingShoutoutOperations();
            var clipsMarkers = new RecordingClipMarkerOperations();
            var polls = new RecordingPollOperations();
            var predictions = new RecordingPredictionOperations();
            var features = TestHostFeatureServices.Create(
                database,
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                []
            );
            var expressions = new AutomationExpressionService();
            var catalog = new AutomationCatalogService(
                new([
                    new CoreAutomationCatalogModule(),
                    new TwitchEventAutomationCatalogModule(),
                    new NativeOperationAutomationCatalogModule(),
                ]),
                features,
                expressions,
                [new AutomationCelTransformHandler(), new AutomationRandomNumberHandler()]
            );
            var overlays = new NoOverlayCues();
            AutomationRuntimeService flowRuntime = null!;
            var countdowns = new AutomationCountdownService(
                database,
                clock,
                () => flowRuntime,
                TestAutomationUiChanges.Create()
            );
            var executor = new AutomationActionExecutor(
                features,
                chat,
                overlays,
                expressions,
                countdowns,
                database,
                channelPoints: null,
                shoutouts,
                clipsMarkers,
                polls,
                predictions
            );
            var flows = new AutomationFlowService(database, catalog, expressions, overlays, clock);
            flowRuntime = new AutomationRuntimeService(
                database,
                catalog,
                flows,
                executor,
                clock,
                TestAutomationUiChanges.Create(),
                Microsoft
                    .Extensions
                    .Logging
                    .Abstractions
                    .NullLogger<AutomationRuntimeService>
                    .Instance
            );
            var runtime = new TwitchEventAutomationRuntime(
                database,
                flowRuntime,
                clock,
                NullLogger<TwitchEventAutomationRuntime>.Instance
            );
            var fixture = new NativeFixture(
                database,
                clock,
                chat,
                shoutouts,
                clipsMarkers,
                polls,
                predictions,
                catalog,
                executor,
                flowRuntime,
                flows,
                runtime
            );
            fixture.HostId = await fixture.SeedHostAsync("streamer", hostFeatures);
            return fixture;
        }

        internal async Task<int> SeedHostAsync(string login, HostFeatureFlags enabledFeatures)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = login == "streamer" ? "streamer-id" : "other-id",
                Login = login,
                DisplayName = login,
                EnabledFeatures = enabledFeatures,
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            return host.Id;
        }

        internal async Task SetFeatureAsync(HostFeatureFlags feature, bool enabled)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = await db.Hosts.SingleAsync(value => value.Id == HostId);
            host.EnabledFeatures = enabled
                ? host.EnabledFeatures | feature
                : host.EnabledFeatures & ~feature;
            _ = await db.SaveChangesAsync();
        }

        internal async Task<AutomationFlowId> SaveAsync(
            ImmutableArray<AutomationFlowDraftNode> nodes,
            ImmutableArray<AutomationFlowDraftEdge> edges,
            int? hostId = null
        ) =>
            (await Flows.SaveAsync(Draft(hostId ?? HostId, nodes, edges), CancellationToken.None))
                .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
                .FlowId;

        internal Task<AutomationFlowId> SaveConnectedChatAsync(
            AutomationFlowDraftNode source,
            string message
        )
        {
            var transform = Node(
                AutomationDefinitionIds.CelTransform.Value,
                JsonSerializer.Serialize(
                    new Dictionary<string, object?>
                    {
                        ["inputs"] = Array.Empty<object>(),
                        ["outputs"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["port-id"] = "message",
                                ["display-name"] = "Message",
                                ["type"] = nameof(AutomationPortValueType.Text),
                                ["nullability"] = nameof(AutomationPortNullability.NonNullable),
                                ["cel"] = JsonSerializer.Serialize(message),
                            },
                        },
                    }
                )
            );
            var action = Node(
                AutomationDefinitionIds.SendChatAction.Value,
                """{"message":""}""",
                AutomationInputBindingMode.Connected
            );
            return SaveAsync(
                [source, transform, action],
                [
                    Edge(source, "flow", action),
                    new(
                        Guid.NewGuid(),
                        AutomationEdgeKind.Data,
                        transform.Id,
                        new("message"),
                        action.Id,
                        new("message")
                    ),
                ]
            );
        }

        internal async Task<AutomationContext> RunContextAsync(AutomationDefinitionId source)
        {
            await using var db = await Database.CreateDbContextAsync();
            var runs = await db
                .AutomationFlowRuns.AsNoTracking()
                .Where(run => run.HostId == HostId)
                .ToArrayAsync();
            return runs.Select(run =>
                    AutomationRuntimeSerialization
                        .RestoreContext(run.ContextSchemaVersion, run.ContextJson)
                        .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
                        .Context
                )
                .Single(context => context.Event.SourceDefinitionId == source);
        }

        internal async Task<int> RunCountAsync(int? hostId = null)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = hostId ?? HostId;
            return await db.AutomationFlowRuns.CountAsync(run => run.HostId == host);
        }

        internal async Task<int> ReceiptCountAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            return await db.AutomationEventReceipts.CountAsync();
        }

        internal async Task<AutomationFlowRunStatus[]> RunStatusesAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            return await db
                .AutomationFlowRuns.AsNoTracking()
                .Where(run => run.HostId == HostId)
                .Select(static run => run.Status)
                .ToArrayAsync();
        }

        internal async Task<string[]> NodeOutcomeCodesAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            return await db
                .AutomationNodeRuns.AsNoTracking()
                .Where(static node => node.OutcomeCode != null)
                .Select(static node => node.OutcomeCode!)
                .ToArrayAsync();
        }

        internal EventSubShoutoutEvent Shoutout(
            string messageId,
            EventSubShoutoutDirection direction,
            int? hostId = null
        )
        {
            var isOther = hostId is { } value && value != HostId;
            return new(
                isOther ? "other-id" : "streamer-id",
                isOther ? "other" : "streamer",
                direction == EventSubShoutoutDirection.Sent
                    ? isOther
                        ? "other-id"
                        : "streamer-id"
                    : "partner-id",
                direction == EventSubShoutoutDirection.Sent
                    ? isOther
                        ? "other"
                        : "streamer"
                    : "partner",
                direction == EventSubShoutoutDirection.Sent ? "partner-id"
                    : isOther ? "other-id"
                    : "streamer-id",
                direction == EventSubShoutoutDirection.Sent ? "partner"
                    : isOther ? "other"
                    : "streamer",
                42,
                Clock.GetUtcNow(),
                null,
                null,
                direction,
                messageId
            );
        }

        internal EventSubPollEvent Poll(
            string messageId,
            EventSubPollStage stage,
            int votes = 0,
            string status = ""
        ) =>
            new(
                "streamer-id",
                "streamer",
                "poll-1",
                "Favourite game?",
                [new("yes", "Yes", votes, 0)],
                status,
                Clock.GetUtcNow(),
                null,
                messageId,
                stage
            );

        internal EventSubPredictionEvent Prediction(
            string messageId,
            EventSubPredictionStage stage,
            string status,
            string? winningOutcomeId = null,
            string predictionId = "prediction-1"
        ) =>
            new(
                "streamer-id",
                "streamer",
                predictionId,
                "Will we win?",
                [new("yes", "Yes", "BLUE", 1, 100, []), new("no", "No", "PINK", 1, 50, [])],
                status,
                Clock.GetUtcNow(),
                null,
                stage == EventSubPredictionStage.End ? Clock.GetUtcNow() : null,
                winningOutcomeId,
                messageId,
                stage
            );

        internal EventSubIncomingRaidEvent IncomingRaid(string messageId) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                "raider-id",
                "raider",
                "Raider",
                "streamer-id",
                "streamer",
                "Streamer",
                25
            );

        internal EventSubStreamOnlineEvent StreamOnline(string messageId) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                "streamer-id",
                "streamer",
                "Streamer",
                "stream-1",
                "live",
                Clock.GetUtcNow()
            );

        internal EventSubStreamOfflineEvent StreamOffline(string messageId) =>
            new(messageId, Clock.GetUtcNow(), "streamer-id", "streamer", "Streamer");

        internal EventSubCheerEvent Cheer(string messageId, int bits) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                "viewer-id",
                "viewer",
                "Viewer",
                "streamer-id",
                "streamer",
                "Streamer",
                bits,
                "cheer100 hello",
                false
            );

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private sealed class RecordingChatSender : IPublicChatMessageSender
    {
        internal ConcurrentQueue<string> Messages { get; } = [];
        internal ConcurrentQueue<PublicChatDeliveryDeadline> Deadlines { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Enqueue(message);
            Deadlines.Enqueue(deadline);
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
        }
    }

    private sealed class NoOverlayCues : IOverlayCueAdmissionService
    {
        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken,
            BlokeBotDbContext? preparationDb = null
        ) =>
            Task.FromResult<OverlayCueReferenceOutcome>(
                new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue)
            );

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan duration) => now += duration;
    }
}

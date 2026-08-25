using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class RedemptionAutomationTests
{
    private static readonly DateTimeOffset _start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SourceConfiguration_ParsesReferenceFilterAndRejectsFreeTextlessInvalidShapes()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();

        (await fixture.CheckAsync("""{"completion-policy":"manual"}"""))
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
            .Configuration.ShouldBeOfType<RewardRedemptionSourceConfiguration>()
            .ShouldSatisfyAllConditions(
                static configuration => configuration.RewardId.ShouldBeNull(),
                static configuration =>
                    configuration.CompletionPolicy.ShouldBe(RedemptionCompletionPolicy.Manual)
            );
        (
            await fixture.CheckAsync(
                """{"reward-id":"reward-a","completion-policy":"fulfil-on-success"}"""
            )
        )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
            .Configuration.ShouldBeOfType<RewardRedemptionSourceConfiguration>()
            .ShouldSatisfyAllConditions(
                static configuration => configuration.RewardId.ShouldBe("reward-a"),
                static configuration =>
                    configuration.CompletionPolicy.ShouldBe(
                        RedemptionCompletionPolicy.FulfilOnSuccess
                    )
            );
        (await fixture.CheckAsync("""{"reward-id":null,"completion-policy":"cancel-on-failure"}"""))
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
            .Configuration.ShouldBeOfType<RewardRedemptionSourceConfiguration>()
            .RewardId.ShouldBeNull();

        _ = (
            await fixture.CheckAsync("""{}""")
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        _ = (
            await fixture.CheckAsync("""{"completion-policy":"whenever"}""")
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        _ = (
            await fixture.CheckAsync("""{"reward-id":"","completion-policy":"manual"}""")
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        _ = (
            await fixture.CheckAsync("""{"reward-id":42,"completion-policy":"manual"}""")
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
    }

    [Test]
    public async Task RewardFilter_SaveResolvesTheReferenceAgainstKnownRewardsOnly()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var source = Node(
            "reward-redemption",
            """{"reward-id":"reward-a","completion-policy":"manual"}"""
        );
        var action = Node("send-chat", """{"message":"Redeemed!"}""");

        var unresolved = await fixture.Flows.SaveAsync(
            Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
            CancellationToken.None
        );

        unresolved
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(static error => error.Code == "reward-reference-unavailable");

        // Externally created rewards resolve too; they are valid read-only triggers.
        await fixture.SeedRewardAsync("reward-a", manageable: false);
        _ = (
            await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
    }

    [Test]
    public async Task Redemption_StartsTheConfiguredFlowOnceAndReceiptsAbsorbRetries()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.Count.ShouldBe(1);
    }

    [Test]
    public async Task RewardFilter_PreventsUnrelatedRedemptionsFromStartingTheFlow()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node(
            "reward-redemption",
            """{"reward-id":"reward-a","completion-policy":"manual"}"""
        );
        var action = Node("send-chat", """{"message":"Filtered"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1", rewardId: "reward-b"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(0);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-2", rewardId: "reward-a"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task StatusUpdateDeliveries_NeverStartFlowsOrClaimReceipts()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1", isNew: false),
            CancellationToken.None
        );

        // The redemption identity triggers only once, on the .add delivery; a later status
        // update is never a fresh trigger.
        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task BothSwitches_GateDispatchBeforeMutationAndReEnableNeverReplays()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.SetFeatureAsync(HostFeatureFlags.Automations, enabled: false);
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("suppressed-1"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);

        await fixture.SetFeatureAsync(HostFeatureFlags.Automations, enabled: true);
        await fixture.SetFeatureAsync(HostFeatureFlags.RewardsAndRedemptions, enabled: false);
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("suppressed-2"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);

        await fixture.SetFeatureAsync(HostFeatureFlags.RewardsAndRedemptions, enabled: true);

        // Re-enabling replays nothing; only a fresh delivery starts a run.
        (await fixture.RunCountAsync()).ShouldBe(0);
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("fresh-1"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["Redeemed!"]);
    }

    [Test]
    public async Task SaveAndEnable_RequireTheRewardsAndRedemptionsParent()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SetFeatureAsync(HostFeatureFlags.RewardsAndRedemptions, enabled: false);
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");

        (
            await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(static error => error.Code == "capability-unavailable");

        AutomationRequiredFeatures
            .ForDefinitions(["reward-redemption", "fulfil-redemption", "cancel-redemption"])
            .ShouldBe(HostFeatureFlags.Automations | HostFeatureFlags.RewardsAndRedemptions);
    }

    [Test]
    public async Task ContextDependentAction_RequiresAConnectedCompatibleSource()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("fulfil-redemption", "{}");

        var disconnected = await fixture.Flows.SaveAsync(
            Draft(fixture.HostId, [source, action], []),
            CancellationToken.None
        );

        disconnected
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error =>
                error.NodeId == action.Id && error.Code == "trigger-context-incompatible"
            );
        _ = (
            await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();

        await fixture.SetFeatureAsync(HostFeatureFlags.RaidCollaboration, enabled: true);
        var actorlessSource = Node("stream-online", "{}");
        var actorlessAction = Node("send-shoutout", "{}");
        var actorless = await fixture.Flows.SaveAsync(
            Draft(
                fixture.HostId,
                [actorlessSource, actorlessAction],
                [Edge(actorlessSource, "flow", actorlessAction)]
            ),
            CancellationToken.None
        );

        actorless
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error =>
                error.NodeId == actorlessAction.Id && error.Code == "trigger-context-incompatible"
            );
        var knownActorSource = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var knownActorAction = Node("send-shoutout", "{}");
        _ = (
            await fixture.Flows.SaveAsync(
                Draft(
                    fixture.HostId,
                    [knownActorSource, knownActorAction],
                    [Edge(knownActorSource, "flow", knownActorAction)]
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
    }

    [Test]
    public async Task Redemptions_AreHostIsolated()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var otherHostId = await fixture.SeedHostAsync(
            "other",
            HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.RewardsAndRedemptions
        );
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Mine"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var otherSource = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var otherAction = Node("send-chat", """{"message":"Other"}""");
        _ = await fixture.SaveAsync(
            [otherSource, otherAction],
            [Edge(otherSource, "flow", otherAction)],
            otherHostId
        );

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1", hostId: otherHostId),
            CancellationToken.None
        );

        (await fixture.RunCountAsync(otherHostId)).ShouldBe(1);
        (await fixture.RunCountAsync()).ShouldBe(0);
        fixture.Chat.Messages.ShouldBe(["Other"]);
    }

    [Test]
    public async Task Context_MapsDocumentedFieldsAndBoundsUntrustedUserInput()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var host = await db.Hosts.AsNoTracking().SingleAsync(h => h.Id == fixture.HostId);

        var context = RedemptionAutomationContext.Create(
            host,
            fixture.Redemption("message-1", userInput: new string('x', 800)),
            _start
        );

        context.Event.SourceDefinitionId.ShouldBe(AutomationDefinitionIds.RewardRedemptionSource);
        var actor = context.Actor.ShouldNotBeNull();
        actor.Login.ShouldBe("viewer");
        actor.TwitchUserId.ShouldBe("viewer-id");
        var safeValues = context.Variables.SafeForExternalUse();
        safeValues[new("redemption_id")]
            .ShouldBeOfType<AutomationValue.Text>()
            .Value.ShouldBe("redemption-1");
        safeValues[new("reward_id")]
            .ShouldBeOfType<AutomationValue.Text>()
            .Value.ShouldBe("reward-a");
        safeValues[new("reward_title")]
            .ShouldBeOfType<AutomationValue.Text>()
            .Value.ShouldBe("Hydrate");
        safeValues[new("reward_cost")].ShouldBeOfType<AutomationValue.Number>().Value.ShouldBe(250);
        safeValues[new("status")]
            .ShouldBeOfType<AutomationValue.Text>()
            .Value.ShouldBe("unfulfilled");
        safeValues[new("redeemed_at")]
            .ShouldBeOfType<AutomationValue.Timestamp>()
            .Value.ShouldBe(_start);
        safeValues.Keys.ShouldNotContain(new AutomationVariableName("user_input"));
        context
            .Variables.ForExecution()[new("user_input")]
            .ShouldSatisfyAllConditions(
                static variable =>
                    variable.Sensitivity.ShouldBe(AutomationDataSensitivity.Sensitive),
                static variable =>
                    variable.Value.ShouldBeOfType<AutomationValue.Text>().Value.Length.ShouldBe(500)
            );
    }

    [Test]
    public async Task FulfilAction_UpdatesAManageableRedemptionThroughChannelPoints()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("fulfil-redemption", "{}");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );

        fixture.Redemptions.Calls.ShouldBe([(fixture.HostId, "redemption-1", true)]);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Completed]);
    }

    [Test]
    public async Task CancelAction_UpdatesAManageableRedemptionThroughChannelPoints()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("cancel-redemption", "{}");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );

        fixture.Redemptions.Calls.ShouldBe([(fixture.HostId, "redemption-1", false)]);
    }

    [Test]
    public async Task Actions_RejectExternalRewardsBeforeAnyTwitchCall()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: false);
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("fulfil-redemption", "{}");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );

        fixture.Redemptions.Calls.ShouldBeEmpty();
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Failed]);
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("reward-not-manageable");
    }

    [Test]
    public async Task Actions_MissingManageScopeFailsWithAnActionableOutcome()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        fixture.Redemptions.NextOutcome = new ChannelPointsOperationOutcome.NotReady(
            "Reconnect the selected broadcaster with Twitch Channel Points permissions."
        );
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("fulfil-redemption", "{}");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );

        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Failed]);
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("broadcaster-authorization-missing");

        // channel:manage:redemptions is already part of the milestone-wide broadcaster grant.
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain(
            "channel:read:redemptions"
        );
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain(
            "channel:manage:redemptions"
        );
    }

    [Test]
    public async Task MalformedPersistedTrigger_IsRejectedBeforeRedemptionAction()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("fulfil-redemption", "{}");
        var flowId = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var persistedSource = await db.AutomationFlowNodes.SingleAsync(node =>
                node.FlowId == flowId.Value && node.Id == source.Id.Value
            );
            persistedSource.DefinitionId = "stream-online";
            persistedSource.ConfigurationJson = "{}";
            _ = await db.SaveChangesAsync();
        }

        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("stream-1"),
            CancellationToken.None
        );

        fixture.Redemptions.Calls.ShouldBeEmpty();
        (await fixture.NodeOutcomeCodesAsync()).ShouldBeEmpty();
        (await fixture.RunCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task CompletionPolicy_FulfilsOnSuccessAndOnlyOnSuccess()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"fulfil-on-success"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );
        fixture.Redemptions.Calls.ShouldBe([(fixture.HostId, "redemption-1", true)]);

        // A failed flow never fulfils under fulfil-on-success.
        fixture.Chat.RejectNext = true;
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-2", redemptionId: "redemption-2"),
            CancellationToken.None
        );
        fixture.Redemptions.Calls.Count.ShouldBe(1);
        (await fixture.RunStatusesAsync()).ShouldContain(AutomationFlowRunStatus.Failed);
    }

    [Test]
    public async Task MultipleRedemptionTriggers_ApplyThePolicyOfEachIndependentRun()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var fulfil = Node("reward-redemption", """{"completion-policy":"fulfil-on-success"}""");
        var manual = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync(
            [fulfil, manual, action],
            [Edge(fulfil, "flow", action), Edge(manual, "flow", action)]
        );

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(2);
        fixture.Redemptions.Calls.ShouldBe([(fixture.HostId, "redemption-1", true)]);
    }

    [Test]
    public async Task CompletionPolicy_CancelsOnFailureAndOnlyOnFailure()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"cancel-on-failure"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );
        fixture.Redemptions.Calls.ShouldBeEmpty();

        fixture.Chat.RejectNext = true;
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-2", redemptionId: "redemption-2"),
            CancellationToken.None
        );
        fixture.Redemptions.Calls.ShouldBe([(fixture.HostId, "redemption-2", false)]);
    }

    [Test]
    public async Task MalformedFrozenDefinition_CancelsRedemptionWithoutExecutingPendingNodeAction()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"cancel-on-failure"}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );
        var runId = (await fixture.RunIdsAsync()).ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db
                .AutomationFlowRuns.Include(static candidate => candidate.NodeRuns)
                .SingleAsync(candidate => candidate.Id == runId);
            run.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
            run.NodeRuns.ShouldContain(candidate =>
                candidate.NodeId == action.Id.Value
                && candidate.Status == AutomationNodeRunStatus.Pending
            );
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(run.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            var mutated = frozen.Flow with
            {
                Edges = frozen
                    .Flow.Edges.Select(edge =>
                        edge.TargetNodeId == action.Id.Value
                            ? edge with
                            {
                                SourcePortId = "unknown-output",
                            }
                            : edge
                    )
                    .ToImmutableArray(),
            };
            var mutatedJson = JsonSerializer.Serialize(mutated, JsonSerializerOptions.Web);
            _ = AutomationRuntimeSerialization
                .RestoreDefinition(mutatedJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            run.DefinitionJson = mutatedJson;
            _ = await db.SaveChangesAsync();
        }

        fixture.Chat.Messages.ShouldBeEmpty();
        fixture.Redemptions.Calls.ShouldBeEmpty();
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await fixture.FlowRuntime.ResumeAsync(new(runId), CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        fixture.Chat.Messages.ShouldBeEmpty();
        fixture.Redemptions.Calls.ShouldBe([(fixture.HostId, "redemption-1", false)]);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Failed]);
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("definition-invalid");
    }

    [Test]
    public async Task CompletionPolicy_ManualNeverTouchesTheRedemption()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );
        fixture.Chat.RejectNext = true;
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-2", redemptionId: "redemption-2"),
            CancellationToken.None
        );

        fixture.Redemptions.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task CompletionPolicy_CausesZeroMutationWhileASwitchIsOffOrRewardIsExternal()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SeedRewardAsync("reward-a", manageable: true);
        var source = Node("reward-redemption", """{"completion-policy":"fulfil-on-success"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await fixture.Runtime.RewardRedemptionReceivedAsync(
            fixture.Redemption("message-1"),
            CancellationToken.None
        );
        fixture.Redemptions.Calls.Count.ShouldBe(1);
        var runId = (await fixture.RunIdsAsync()).Single();

        // Re-reporting the same terminal run while a required switch is off mutates nothing.
        await fixture.SetFeatureAsync(HostFeatureFlags.RewardsAndRedemptions, enabled: false);
        await fixture.PolicyObserver.RunFinishedAsync(
            new(runId),
            AutomationResumeStatus.Completed,
            CancellationToken.None
        );
        fixture.Redemptions.Calls.Count.ShouldBe(1);

        await fixture.SetFeatureAsync(HostFeatureFlags.RewardsAndRedemptions, enabled: true);
        await fixture.MakeRewardExternalAsync("reward-a");
        await fixture.PolicyObserver.RunFinishedAsync(
            new(runId),
            AutomationResumeStatus.Completed,
            CancellationToken.None
        );
        fixture.Redemptions.Calls.Count.ShouldBe(1);
    }

    [Test]
    public async Task Readiness_ListsTheRedemptionSourceWithBothScopesAndFlowUsage()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("send-chat", """{"message":"Redeemed!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var readiness = new TwitchEventSourceReadinessService(
            fixture.Database,
            fixture.Catalog,
            fixture.FlowRuntime,
            new FixedBroadcasterTokens(
                new TokenStatus.Ready(
                    "token",
                    new("streamer-id", "streamer", OAuthScopeSet.Empty),
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            )
        );

        var outcome = await readiness.LoadAsync(new(fixture.HostId), CancellationToken.None);

        var available = outcome.ShouldBeOfType<TwitchEventSourceReadinessOutcome.Available>();
        var redemption = available.Sources.Single(static value =>
            value.DefinitionId.Value == "reward-redemption"
        );
        redemption.RequiredBroadcasterScopes.ShouldBe([
            "channel:read:redemptions",
            "channel:manage:redemptions",
        ]);
        redemption.SubscriptionTypes.ShouldBe(
            "channel.channel_points_custom_reward_redemption.add"
        );
        redemption.UsedByEnabledFlow.ShouldBeTrue();
        _ = redemption.State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
    }

    private static AutomationFlowDraft Draft(
        int hostId,
        ImmutableArray<AutomationFlowDraftNode> nodes,
        ImmutableArray<AutomationFlowDraftEdge> edges
    ) => new(null, new(hostId), "Flow", 1, true, nodes, edges);

    private static AutomationFlowDraftNode Node(string type, string json)
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

    private sealed class RecordingRedemptionOperations : IChannelPointsDashboardOperations
    {
        internal List<(int HostId, string RedemptionId, bool Fulfill)> Calls { get; } = [];

        internal ChannelPointsOperationOutcome NextOutcome { get; set; } =
            new ChannelPointsOperationOutcome.RedemptionUpdated();

        public Task<ChannelPointsOperationOutcome> UpdateRedemptionAsync(
            int hostId,
            string redemptionId,
            bool fulfill,
            CancellationToken cancellationToken
        )
        {
            Calls.Add((hostId, redemptionId, fulfill));
            return Task.FromResult(NextOutcome);
        }

        public Task<ChannelPointsDashboardState> LoadAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ChannelPointsOperationOutcome> CreateRewardAsync(
            int hostId,
            ChannelPointsRewardDraft draft,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ChannelPointsOperationOutcome> UpdateRewardAsync(
            int hostId,
            string rewardId,
            ChannelPointsRewardDraft draft,
            bool isEnabled,
            bool paused,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ChannelPointsOperationOutcome> DeleteRewardAsync(
            int hostId,
            string rewardId,
            bool confirmed,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class RedemptionFixture : IAsyncDisposable
    {
        private RedemptionFixture(
            SqliteBlokeBotDbFactory database,
            MutableTimeProvider clock,
            RejectableChatSender chat,
            RecordingRedemptionOperations redemptions,
            AutomationCatalogService catalog,
            AutomationRuntimeService flowRuntime,
            AutomationFlowService flows,
            TwitchEventAutomationRuntime runtime,
            RedemptionCompletionPolicyObserver policyObserver
        )
        {
            Database = database;
            Clock = clock;
            Chat = chat;
            Redemptions = redemptions;
            Catalog = catalog;
            FlowRuntime = flowRuntime;
            Flows = flows;
            Runtime = runtime;
            PolicyObserver = policyObserver;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal MutableTimeProvider Clock { get; }
        internal RejectableChatSender Chat { get; }
        internal RecordingRedemptionOperations Redemptions { get; }
        internal AutomationCatalogService Catalog { get; }
        internal AutomationRuntimeService FlowRuntime { get; }
        internal AutomationFlowService Flows { get; }
        internal TwitchEventAutomationRuntime Runtime { get; }
        internal RedemptionCompletionPolicyObserver PolicyObserver { get; }
        internal int HostId { get; private set; }

        internal static async Task<RedemptionFixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var clock = new MutableTimeProvider(_start);
            var chat = new RejectableChatSender();
            var redemptions = new RecordingRedemptionOperations();
            var features = TestHostFeatureServices.Create(
                database,
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                []
            );
            var catalog = new AutomationCatalogService(
                new([
                    new CoreAutomationCatalogModule(),
                    new TwitchEventAutomationCatalogModule(),
                    new NativeOperationAutomationCatalogModule(),
                ]),
                features
            );
            var expressions = new AutomationExpressionService();
            var overlays = new NoOverlayCues();
            var actions = new AutomationActionExecutor(
                features,
                chat,
                overlays,
                expressions,
                database,
                redemptions
            );
            var policyObserver = new RedemptionCompletionPolicyObserver(
                database,
                features,
                redemptions,
                NullLogger<RedemptionCompletionPolicyObserver>.Instance
            );
            var flows = new AutomationFlowService(database, catalog, expressions, overlays, clock);
            var flowRuntime = new AutomationRuntimeService(
                database,
                catalog,
                flows,
                actions,
                clock,
                [policyObserver]
            );
            var runtime = new TwitchEventAutomationRuntime(
                database,
                flowRuntime,
                clock,
                NullLogger<TwitchEventAutomationRuntime>.Instance
            );
            var fixture = new RedemptionFixture(
                database,
                clock,
                chat,
                redemptions,
                catalog,
                flowRuntime,
                flows,
                runtime,
                policyObserver
            );
            fixture.HostId = await fixture.SeedHostAsync(
                "streamer",
                HostFeatureFlags.Automations
                    | HostFeatureFlags.CustomCommands
                    | HostFeatureFlags.RewardsAndRedemptions
            );
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

        internal async Task SeedRewardAsync(string rewardId, bool manageable)
        {
            await using var db = await Database.CreateDbContextAsync();
            _ = db.TwitchCustomRewards.Add(
                new TwitchCustomReward
                {
                    HostId = HostId,
                    ProviderRewardId = rewardId,
                    Title = "Hydrate",
                    Cost = 250,
                    IsManageable = manageable,
                    IsEnabled = true,
                    UpdatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        internal async Task MakeRewardExternalAsync(string rewardId)
        {
            await using var db = await Database.CreateDbContextAsync();
            var reward = await db.TwitchCustomRewards.SingleAsync(value =>
                value.HostId == HostId && value.ProviderRewardId == rewardId
            );
            reward.IsManageable = false;
            _ = await db.SaveChangesAsync();
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

        internal async Task<AutomationConfigurationCheck> CheckAsync(string json)
        {
            using var document = JsonDocument.Parse(json);
            return await Catalog.ValidatePersistedForSaveAsync(
                new(HostId),
                new("reward-redemption", 1, document.RootElement.Clone()),
                CancellationToken.None
            );
        }

        internal async Task<AutomationFlowId> SaveAsync(
            ImmutableArray<AutomationFlowDraftNode> nodes,
            ImmutableArray<AutomationFlowDraftEdge> edges,
            int? hostId = null
        ) =>
            (await Flows.SaveAsync(Draft(hostId ?? HostId, nodes, edges), CancellationToken.None))
                .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
                .FlowId;

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

        internal async Task<Guid[]> RunIdsAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            return await db
                .AutomationFlowRuns.AsNoTracking()
                .Where(run => run.HostId == HostId)
                .Select(static run => run.Id)
                .ToArrayAsync();
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

        internal EventSubRewardRedemptionEvent Redemption(
            string messageId,
            string redemptionId = "redemption-1",
            string rewardId = "reward-a",
            string userInput = "please",
            bool isNew = true,
            int? hostId = null
        ) =>
            new(
                hostId is { } value && value != HostId ? "other-id" : "streamer-id",
                hostId is { } other && other != HostId ? "other" : "streamer",
                redemptionId,
                rewardId,
                "Hydrate",
                250,
                "viewer-id",
                "viewer",
                "Viewer",
                userInput,
                HelixRewardRedemptionStatus.Unfulfilled,
                _start,
                messageId,
                isNew
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

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private sealed class RejectableChatSender : IPublicChatMessageSender
    {
        internal ConcurrentQueue<string> Messages { get; } = [];

        internal bool RejectNext { get; set; }

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            if (RejectNext)
            {
                RejectNext = false;
                return ValueTask.FromResult<PublicChatSendOutcome>(
                    new PublicChatSendOutcome.Rejected()
                );
            }

            Messages.Enqueue(message);
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
        }
    }

    private sealed class NoOverlayCues : IOverlayCueAdmissionService
    {
        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
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

using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Effects_PublicationComparesWholeParentUnionBeyondPagingAndPreservesFrozenRunsAndStoredPause()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var service = Subflows(f);
        var leaf = Subflow(f.HostId);
        var delayed = WithEffects(leaf, Node("delay", """{"duration-milliseconds":1000}"""));
        var first = await Publish(service, delayed);
        var nested = await Publish(service, Nesting(Subflow(f.HostId), first));
        var parents = new List<AutomationFlowId>();
        AutomationFlowDraft? staleDraft = null;
        for (var i = 0; i < AutomationSubflowService.LibraryPageSize + 1; i++)
        {
            var draft = Caller(f.HostId, nested) with { Name = $"Parent {i}" };
            var saved = (
                await f.Flows.SaveAsync(draft, default)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            parents.Add(saved.FlowId);
            staleDraft = draft with { Id = saved.FlowId };
        }
        var covered = Caller(f.HostId, nested);
        var send = Node("send-chat", """{"message":"already-covered"}""");
        covered = covered with
        {
            Nodes = covered.Nodes.Add(send),
            Edges = covered.Edges.Add(Edge(covered.Nodes[1], "complete", send)),
        };
        var coveredId = (await f.Flows.SaveAsync(covered, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var disabled = Caller(f.HostId, first) with { IsEnabled = false };
        var disabledId = (await f.Flows.SaveAsync(disabled, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var admitted = await f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        admitted.RunIds.Length.ShouldBe(parents.Count + 1);
        var proposed = WithEffects(leaf, Node("send-chat", """{"message":"new-effect"}"""));
        var review = await ReviewSubflowAsync(service, proposed);
        review
            .EffectParents.Where(parent => parent.WillDisable)
            .Select(parent => parent.FlowId)
            .ShouldBe(parents, ignoreOrder: true);
        var outcome = (
            await service.PublishAsync(proposed, review, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Published>();
        outcome
            .DisabledParents.Select(parent => parent.FlowId)
            .ShouldBe(parents, ignoreOrder: true);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var enabled = await db
                .AutomationFlows.Where(flow => flow.IsEnabled)
                .Select(flow => flow.Id)
                .ToArrayAsync();
            enabled.ShouldBe([coveredId.Value]);
            (
                await db.AutomationFlows.SingleAsync(flow => flow.Id == disabledId.Value)
            ).IsEnabled.ShouldBeFalse();
        }
        _ = (
            await f.Flows.SaveAsync(staleDraft!, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (
                await db.AutomationFlows.SingleAsync(flow => flow.Id == staleDraft!.Id!.Value.Value)
            ).IsEnabled.ShouldBeFalse();
        }
        var next = await f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        _ = next.RunIds.ShouldHaveSingleItem();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        foreach (var run in admitted.RunIds)
        {
            (await f.NewRuntime().ResumeAsync(run, default)).Status.ShouldBe(
                AutomationResumeStatus.Completed
            );
        }
        var freshReview = (await f.Flows.SetEnabledAsync(new(f.HostId), parents[0], true, default))
            .ShouldBeOfType<AutomationFlowEnableOutcome.Review>()
            .Reviewed;
        freshReview.Effects.Capabilities.ShouldBe(AutomationActionCapabilities.SendsChat);
        _ = (
            await f.Flows.SetEnabledAsync(new(f.HostId), parents[0], true, default, freshReview)
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        _ = await Publish(service, leaf);
        await using var verify = await f.Database.CreateDbContextAsync();
        (
            await verify.AutomationFlows.SingleAsync(flow => flow.Id == parents[0].Value)
        ).IsEnabled.ShouldBeTrue();
    }

    [Test]
    public async Task Effects_StaleParentReviewAndEnableRefuseBeforeWritesAndFreshReviewUsesCurrentDependencies()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var service = Subflows(f);
        var leaf = Subflow(f.HostId);
        var first = await Publish(service, leaf);
        var caller = Caller(f.HostId, first) with { IsEnabled = false };
        var id = (await f.Flows.SaveAsync(caller, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var approval = (await f.Flows.SetEnabledAsync(new(f.HostId), id, true, default))
            .ShouldBeOfType<AutomationFlowEnableOutcome.Review>()
            .Reviewed;
        approval.Effects.IsEmpty.ShouldBeTrue();
        var changed = WithEffects(leaf, Node("send-chat", """{"message":"current"}"""));
        var publicationReview = await ReviewSubflowAsync(service, changed);
        _ = (
            await f.Flows.SetEnabledAsync(new(f.HostId), id, true, default, approval)
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        var before = await PublicationStateAsync(f);
        _ = (
            await service.PublishAsync(changed, publicationReview, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Stale>();
        (await PublicationStateAsync(f)).ShouldBe(before);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (
                await db.AutomationFlows.SingleAsync(flow => flow.Id == id.Value)
            ).IsEnabled.ShouldBeTrue();
        }
        _ = await Publish(service, changed);
        var refreshed = (await f.Flows.SetEnabledAsync(new(f.HostId), id, true, default, approval))
            .ShouldBeOfType<AutomationFlowEnableOutcome.Review>()
            .Reviewed;
        refreshed.Effects.Capabilities.ShouldBe(AutomationActionCapabilities.SendsChat);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (
                await db.AutomationFlows.SingleAsync(flow => flow.Id == id.Value)
            ).IsEnabled.ShouldBeFalse();
        }
        _ = (
            await f.Flows.SetEnabledAsync(new(f.HostId), id, true, default, refreshed)
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        var other = await f.SeedHostAsync(
            "other-effects",
            BlokeBot.Persistence.Models.HostFeatureFlags.Automations
        );
        _ = (
            await f.Flows.SetEnabledAsync(new(other), id, true, default, refreshed)
        ).ShouldBeOfType<AutomationFlowEnableOutcome.FlowNotFound>();
    }

    [Test]
    public async Task Effects_UsedPurePluginAndActionDiscloseScriptsWithoutInventedMutationAndUnavailableIsNotNone()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var plugin = await SubflowPlugin(f);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        await using var db = await f.Database.CreateDbContextAsync();
        var empty = await plugin.Flows.EffectsAsync(db, Draft(f.HostId, [source], []), default);
        empty.Summary!.IsEmpty.ShouldBeTrue();
        var action = await plugin.Flows.EffectsAsync(
            db,
            Draft(f.HostId, [source, plugin.Node], [Edge(source, "flow", plugin.Node)]),
            default
        );
        action.Summary!.RunsPluginScripts.ShouldBeTrue();
        (
            action.Summary.Capabilities
            & (
                AutomationActionCapabilities.SendsChat
                | AutomationActionCapabilities.ChangesPoints
                | AutomationActionCapabilities.CallsTwitchApi
            )
        ).ShouldBe(AutomationActionCapabilities.None);
        var descriptor = (
            await plugin.Catalog.DiscoverAsync(new(f.HostId), default)
        ).Definitions.Single(value =>
            value.Id.Value.EndsWith(".queued-link", StringComparison.Ordinal)
        );
        var producer = Node(descriptor.Id.Value, "{}") with
        {
            Definition = new(
                descriptor.Id.Value,
                descriptor.Schema.Current.Value,
                System.Text.Json.JsonSerializer.SerializeToElement(new { }),
                descriptor.PluginProvenance
            ),
        };
        var send = Node(
            "send-chat",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var pure = await plugin.Flows.EffectsAsync(
            db,
            Draft(
                f.HostId,
                [source, producer, send],
                [
                    Edge(source, "flow", send),
                    Edge(
                        producer,
                        descriptor.Outputs[0].Id.Value,
                        send,
                        "message",
                        AutomationEdgeKind.Data
                    ),
                ]
            ),
            default
        );
        pure.Summary!.RunsPluginScripts.ShouldBeTrue();
        pure.Summary.Capabilities.ShouldBe(AutomationActionCapabilities.SendsChat);
        plugin.Invoker.Calls.ShouldBe(0);
        var service = Subflows(f);
        var otherHost = await f.SeedHostAsync(
            "effects-other",
            BlokeBot.Persistence.Models.HostFeatureFlags.Automations
        );
        var other = await Publish(service, Subflow(otherHost));
        var unavailable = await f.Flows.EffectsAsync(db, Caller(f.HostId, other), default);
        unavailable.Summary.ShouldBeNull();
        unavailable.Errors.ShouldNotBeEmpty();
    }

    private static AutomationSubflowDraft WithEffects(
        AutomationSubflowDraft draft,
        params AutomationFlowDraftNode[] nodes
    )
    {
        var all = new[] { draft.Graph.Nodes[0] }
            .Concat(nodes)
            .Append(draft.Graph.Nodes[^1])
            .ToImmutableArray();
        return draft with
        {
            Graph = draft.Graph with
            {
                Nodes = all,
                Edges = all.Zip(all.Skip(1))
                    .Select(pair => Edge(pair.First, "complete", pair.Second))
                    .ToImmutableArray(),
            },
        };
    }
}

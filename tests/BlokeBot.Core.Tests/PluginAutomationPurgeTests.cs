using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginAutomationPurgeTests
{
    [Test]
    public async Task CompatiblePluginPurge_DisablesFlowAndProjectsUnavailableGraphWithHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var pluginId = PluginContractFixtures.PluginId("community.link-queue");
        var key = Key(pluginId, hostId);
        var fence = Fence();
        var state = State(key, fence);
        var store = new EfPluginFeatureStore(database, new());
        _ = (
            await store.EnableAsync(
                new(
                    null,
                    state,
                    PluginConfigurationRevision.Initial,
                    PluginConfigurationRevision.Initial,
                    Plan(pluginId, state)
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginFeatureEnableStoreOutcome.Enabled>();
        var flowId = await SeedCompletedRunAsync(database, hostId);

        await store.PurgeAsync(pluginId, CancellationToken.None);

        var reason =
            "Plugin community.link-queue was purged. Reinstall and enable a compatible version to restore this flow.";
        var features = TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var catalog = new AutomationCatalogService(new AutomationDefinitionCatalog([]), features);
        var overlays = new UnavailableOverlayCueAdmissionService();
        var flows = new AutomationFlowService(
            database,
            catalog,
            new(),
            overlays,
            TimeProvider.System
        );
        var projected = (await flows.ListAsync(new(hostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.ShouldHaveSingleItem();
        projected.Draft.Id.ShouldBe(new AutomationFlowId(flowId));
        projected.Draft.IsEnabled.ShouldBeFalse();
        projected.UnavailableReason.ShouldBe(reason);
        projected.Draft.Nodes.Length.ShouldBe(1);

        var history = (
            await new AutomationRunQueryService(database, features, catalog).ListAsync(
                new(hostId),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        history.FlowId.ShouldBe(new(flowId));

        using var editor = new BunitContext();
        var rail = editor.Render<AutomationFlowRail>(parameters =>
            parameters
                .Add(component => component.Flows, [projected])
                .Add(component => component.CurrentFlowId, projected.Draft.Id)
                .Add(component => component.CurrentName, projected.Draft.Name)
                .Add(component => component.Visible, true)
        );
        var item = rail.Find(".automation-flow-item");
        item.TextContent.ShouldContain("Unavailable");
        item.TextContent.ShouldContain("1 nodes");
        item.QuerySelector("strong")!.GetAttribute("title").ShouldBe(reason);

        await using var verify = await database.CreateDbContextAsync();
        (await verify.PluginFeatureStates.CountAsync()).ShouldBe(0);
        (await verify.PluginAutomationInstantiations.CountAsync()).ShouldBe(0);
        (await verify.AutomationFlowNodes.CountAsync()).ShouldBe(1);
        (await verify.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "plugin-purge-host",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Automations,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<Guid> SeedCompletedRunAsync(
        SqliteBlokeBotDbFactory database,
        int hostId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var flow = await db.AutomationFlows.Include(static value => value.Nodes).SingleAsync();
        var now = DateTime.UtcNow;
        _ = db.AutomationFlowRuns.Add(
            new()
            {
                Id = Guid.NewGuid(),
                FlowId = flow.Id,
                HostId = hostId,
                AutomationGeneration = 1,
                RequiredFeatures = HostFeatureFlags.Automations,
                ContextSchemaVersion = AutomationContextSchema.CurrentVersion,
                SourceDefinitionId = flow.Nodes.ShouldHaveSingleItem().DefinitionId,
                SourceNodeId = flow.Nodes[0].Id,
                SourceOccurrenceId = Guid.NewGuid(),
                ContextJson = "{}",
                DefinitionJson = "{}",
                Status = AutomationFlowRunStatus.Completed,
                StartedAtUtc = now,
                CompletedAtUtc = now,
            }
        );
        _ = await db.SaveChangesAsync();
        return flow.Id;
    }

    private static PluginAutomationEnableStorePlan Plan(PluginId pluginId, PluginFeatureState state)
    {
        const string Version = "1.2.0";
        const string Tag = "community-link-queue";
        const string Feature = "publishing";
        const string Template = "publish-links";
        const string Hash = "template-hash-a";
        var nodeId = Guid.NewGuid();
        var provenance = new AutomationPluginProvenance(
            pluginId.Value,
            Version,
            Tag,
            1,
            Feature,
            "queued-link",
            "definition-hash-a",
            state.Fence.OperationId.Value,
            checked((long)state.Fence.Generation.Value),
            checked((long)state.Generation.Value),
            Template,
            Hash
        );
        return new(
            Guid.NewGuid(),
            pluginId.Value,
            Version,
            Tag,
            1,
            Feature,
            [
                new(
                    "Publish approved links",
                    new(pluginId.Value, Version, Tag, 1, Feature, Template, Hash),
                    [
                        new(
                            nodeId,
                            PluginAutomationCatalogRegistry
                                .DefinitionId(pluginId, Definition("queued-link"))
                                .Value,
                            1,
                            "{}",
                            AutomationRuntimeSerialization.SerializeInputBindings(
                                ImmutableDictionary<
                                    AutomationConfigurationFieldId,
                                    AutomationInputBinding
                                >.Empty
                            ),
                            PluginAutomationCatalogRegistry.SerializeProvenance(provenance),
                            false,
                            48,
                            72
                        ),
                    ],
                    []
                ),
            ]
        );
    }

    private static PluginFeatureState State(PluginFeatureKey key, PluginLifecycleFence fence)
    {
        PluginFeatureGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(1, out var revision).ShouldBeTrue();
        return new(key, fence, generation, new PluginFeatureReadiness.Ready(), revision);
    }

    private static PluginFeatureKey Key(PluginId pluginId, int hostId)
    {
        PluginFeatureId.TryCreate("publishing", out var feature).ShouldBeTrue();
        PluginHostId.TryCreate(hostId, out var host).ShouldBeTrue();
        return new(pluginId, feature, host);
    }

    private static PluginLifecycleFence Fence()
    {
        PluginWorkerGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), generation);
    }

    private static PluginAutomationDefinitionId Definition(string value) =>
        PluginAutomationDefinitionId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid definition test ID.");
}

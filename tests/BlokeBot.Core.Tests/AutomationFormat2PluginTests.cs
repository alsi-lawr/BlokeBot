using System.Text;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Format2_PluginReferencesResolveCurrentLocalFencesWithoutExecutingOrImportingPluginState(
        bool required
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var plugin = await SubflowPlugin(fixture, required);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var draft = Draft(
            fixture.HostId,
            [source, plugin.Node],
            [Edge(source, "flow", plugin.Node)]
        );
        var saved = (
            await plugin.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var scenarios = new AutomationScenarioService(
            fixture.Database,
            plugin.Catalog,
            plugin.Flows,
            fixture.Clock
        );
        var export = await new ConfigurationDocumentExporter(
            fixture.Database,
            new(),
            plugin.Catalog,
            plugin.Flows,
            fixture.Clock,
            scenarios
        ).ExportAsync(
            fixture.HostId,
            new(
                new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                new(false, false, false)
            )
            {
                AutomationFlowIds = new HashSet<Guid> { saved.FlowId.Value },
            },
            CancellationToken.None
        );
        var document = export.ShouldBeOfType<ConfigurationExportOutcome.Success>().Document;
        var json = Encoding.UTF8.GetString(
            export.ShouldBeOfType<ConfigurationExportOutcome.Success>().Json
        );
        json.ShouldNotContain(plugin.State.Fence.OperationId.ToString());
        json.ShouldNotContain("workerGeneration");
        PluginFeatureGeneration.TryCreate(2, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(2, out var revision).ShouldBeTrue();
        var updated = plugin.State with { Generation = generation, Revision = revision };
        plugin.Snapshots.Publish(updated);
        var adapter = new AutomationConfigurationTransferAdapter(
            plugin.Flows,
            plugin.Catalog,
            fixture.Clock,
            scenarios
        );
        var selection = new ConfigurationImportSelection(
            fixture.HostId,
            [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
            new HashSet<BlokeBot.Persistence.Models.HostFeatureFlags>()
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var references = await ConfigurationImportReferencePlan.BuildAsync(
                db,
                fixture.HostId,
                document,
                selection,
                CancellationToken.None
            );
            var host = await db.Hosts.SingleAsync(host => host.Id == fixture.HostId);
            var staged = await adapter.StageAsync(
                db,
                host,
                document.Sections.Automations!,
                selection.Sections[0],
                references,
                CancellationToken.None
            );
            staged.Issues.ShouldBeEmpty();
            await transaction.CommitAsync();
        }
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db
                .AutomationFlows.Include(flow => flow.Nodes)
                .Include(flow => flow.Edges)
                .SingleAsync();
            var imported = AutomationFlowService
                .RestoreDraft(row)
                .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>()
                .Draft;
            imported
                .Nodes.Single(node => node.Definition.PluginProvenance is not null)
                .Definition.PluginProvenance!.FeatureGeneration.ShouldBe(2);
            (await db.PluginFeatureConfigurations.CountAsync()).ShouldBe(0);
            (await db.PluginInstallationConfigurations.CountAsync()).ShouldBe(0);
            (await db.PluginInstallationSecrets.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureSecrets.CountAsync()).ShouldBe(0);
        }
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.MissingScopes,
                PluginRecoveryAction.ReconnectTwitch,
                "fixture",
                out var reason
            )
            .ShouldBeTrue();
        PluginFeatureRevision.TryCreate(3, out var degradedRevision).ShouldBeTrue();
        plugin.Snapshots.Publish(
            updated with
            {
                Revision = degradedRevision,
                Readiness = new PluginFeatureReadiness.EnabledDegraded(reason),
            }
        );
        var degraded = await new ConfigurationImportPreviewService(
            fixture.Database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            adapter
        ).PreviewAsync(document, selection, CancellationToken.None);
        degraded
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.CanApply.ShouldBe(!required);
        PluginFeatureRevision.TryCreate(4, out var disabledRevision).ShouldBeTrue();
        plugin.Snapshots.Publish(
            updated with
            {
                Revision = disabledRevision,
                Readiness = new PluginFeatureReadiness.Disabled(),
            }
        );
        var preview = await new ConfigurationImportPreviewService(
            fixture.Database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            adapter
        ).PreviewAsync(document, selection, CancellationToken.None);
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.CanApply.ShouldBeFalse();
        plugin.Invoker.Calls.ShouldBe(0);
    }
}

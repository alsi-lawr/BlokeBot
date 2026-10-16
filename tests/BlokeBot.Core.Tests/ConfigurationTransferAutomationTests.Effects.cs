using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    public async Task EffectsImport_CurrentApprovalAndStoredEnableChangesAreComparedBeforeAnyAuditOrGraphWrites()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "effects-import");
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Hosts.ExecuteUpdateAsync(set =>
                set.SetProperty(row => row.EnabledFeatures, HostFeatureFlags.Automations)
            );
        }
        var document = NestedDocument();
        var section = document.Sections.Automations!;
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = section with { Flows = [section.Flows[0] with { Enabled = true }] },
            },
        };
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        var selection = await ReviewImportEffectsAsync(
            database,
            document,
            AutomationSelection(host)
        );
        var effects = selection.Sections[0].AutomationReview!;
        effects.RequiresApproval.ShouldBeTrue();
        effects
            .Flows.Single()
            .Effects.Summary!.Capabilities.ShouldBe(AutomationActionCapabilities.SendsChat);
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection with
                {
                    Sections = [selection.Sections[0] with { AutomationEffectsApproved = false }],
                },
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.StaleAutomationReview>();
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
            (await db.AutomationFlows.CountAsync()).ShouldBe(0);
        }
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        selection = await ReviewImportEffectsAsync(database, document, AutomationSelection(host));
        var transfer = AutomationTransfer(database);
        var flowId = effects.Flows.Single().FlowId;
        _ = (
            await transfer.FlowService.SetEnabledAsync(new(host), flowId, false, default)
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        var before = await ImportEffectsStateAsync(database);
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.StaleAutomationReview>();
        (await ImportEffectsStateAsync(database)).ShouldBe(before);
        selection = await ReviewImportEffectsAsync(database, document, AutomationSelection(host));
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.SingleAsync()).IsEnabled.ShouldBeTrue();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task EffectsImport_SameDocumentRestoresChildPointerAndPausesOnlyUnapprovedFinalParentsWithRollback()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "effects-child-import");
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Hosts.ExecuteUpdateAsync(set =>
                set.SetProperty(row => row.EnabledFeatures, HostFeatureFlags.Automations)
            );
        }
        var document = NestedDocument();
        var section = document.Sections.Automations!;
        var leaf = section.Subflows[0];
        var send = Node(
            "child-send",
            AutomationDefinitionIds.SendChatAction.Value,
            JsonSerializer.SerializeToElement(new { message = "file-child" }),
            [new("message", AutomationInputBindingMode.Fixed)]
        );
        leaf = leaf with
        {
            Graph = leaf.Graph with
            {
                Nodes = [leaf.Graph.Nodes[0], send, leaf.Graph.Nodes[1]],
                Edges =
                [
                    new(
                        "entry-send",
                        AutomationEdgeKind.Flow,
                        "entry",
                        "complete",
                        "child-send",
                        "flow"
                    ),
                    new(
                        "send-exit",
                        AutomationEdgeKind.Flow,
                        "child-send",
                        "complete",
                        "exit",
                        "flow"
                    ),
                ],
            },
        };
        var caller = section.Flows[0];
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = section with
                {
                    Subflows = [leaf, section.Subflows[1]],
                    Flows = [caller with { Enabled = true }],
                },
            },
        };
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        var selection = await ReviewImportEffectsAsync(
            database,
            document,
            AutomationSelection(host)
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var transfer = AutomationTransfer(database);
        var service = new AutomationSubflowService(
            database,
            transfer.FlowService,
            TimeProvider.System
        );
        var summary = (await service.ListAsync(new(host), new("Leaf"), default)).Subflows.Single();
        var current = (await service.LoadCurrentAsync(new(host), summary.Id, default))!;
        var entry = current.Graph.Nodes[0];
        var exit = current.Graph.Nodes[^1];
        var local = new AutomationSubflowDraft(
            current.SubflowId,
            "Local no-effect child",
            current.Interface,
            current.Graph with
            {
                Nodes = [entry, exit],
                Edges =
                [
                    new(
                        Guid.NewGuid(),
                        AutomationEdgeKind.Flow,
                        entry.Id,
                        new("complete"),
                        exit.Id,
                        new("flow")
                    ),
                ],
            }
        );
        var ready = (
            await service.PreviewAsync(local, default)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        _ = (
            await service.PublishAsync(local, ready.Reviewed, default)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Published>();
        AutomationFlowDraft existing;
        await using (var db = await database.CreateDbContextAsync())
        {
            existing = AutomationFlowService
                .RestoreDraft(
                    await db
                        .AutomationFlows.Include(flow => flow.Nodes)
                        .Include(flow => flow.Edges)
                        .SingleAsync()
                )
                .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>()
                .Draft;
        }
        var extra = existing with
        {
            Id = null,
            Name = "Unapproved outside parent",
            Nodes =
            [
                existing.Nodes[0] with
                {
                    Id = new(Guid.NewGuid()),
                },
                existing.Nodes[1] with
                {
                    Id = new(Guid.NewGuid()),
                },
            ],
        };
        extra = extra with
        {
            Edges =
            [
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    extra.Nodes[0].Id,
                    new("flow"),
                    extra.Nodes[1].Id,
                    new("flow")
                ),
            ],
        };
        var extraId = (await transfer.FlowService.SaveAsync(extra, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        selection = await ReviewImportEffectsAsync(database, document, AutomationSelection(host));
        selection
            .Sections[0]
            .AutomationReview!.DisabledParents.Select(parent => parent.FlowId)
            .ShouldBe([extraId]);
        var skipped = await ReviewImportEffectsAsync(
            database,
            document,
            AutomationSelection(host) with
            {
                Sections =
                [
                    AutomationSelection(host).Sections[0] with
                    {
                        Strategy = ImportConflictStrategy.AddMissing,
                    },
                ],
            }
        );
        skipped.Sections[0].AutomationReview!.Flows.ShouldBeEmpty();
        skipped.Sections[0].AutomationReview!.DisabledParents.ShouldBeEmpty();
        var before = await ImportEffectsStateAsync(database);
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_effect_audit BEFORE INSERT ON configuration_import_audits BEGIN SELECT RAISE(ABORT, 'late effect audit'); END;"
            );
        }
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        (await ImportEffectsStateAsync(database)).ShouldBe(before);
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_effect_audit;");
        }
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.AutomationFlows.SingleAsync(flow => flow.Id == extraId.Value)
        ).IsEnabled.ShouldBeFalse();
        (
            await verify.AutomationFlows.SingleAsync(flow => flow.Id != extraId.Value)
        ).IsEnabled.ShouldBeTrue();
        (await service.LoadCurrentAsync(new(host), current.SubflowId, default))!.Revision.ShouldBe(
            current.Revision + 2
        );
    }

    private static async Task<ConfigurationImportSelection> ReviewImportEffectsAsync(
        SqliteBlokeBotDbFactory database,
        ConfigurationDocumentV2 document,
        ConfigurationImportSelection selection,
        IAutomationConfigurationTransferAdapter? adapter = null
    )
    {
        var preview = (
            await new ConfigurationImportPreviewService(
                database,
                UnavailableOverlayConfigurationTransferAdapter.Instance,
                adapter ?? AutomationTransfer(database).Adapter
            ).PreviewAsync(document, selection, default)
        )
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview;
        return selection with
        {
            Sections = selection
                .Sections.Select(section =>
                    section.Section == ConfigurationSectionId.Automations
                        ? section with
                        {
                            AutomationReview = preview
                                .Sections.Single(value => value.Section == section.Section)
                                .AutomationReview,
                            AutomationEffectsApproved = true,
                        }
                        : section
                )
                .ToArray(),
        };
    }

    private static async Task<string> ImportEffectsStateAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        return JsonSerializer.Serialize(
            new
            {
                Flows = await db
                    .AutomationFlows.OrderBy(flow => flow.Id)
                    .Select(flow => new
                    {
                        flow.Id,
                        flow.Name,
                        flow.IsEnabled,
                    })
                    .ToArrayAsync(),
                Nodes = await db
                    .AutomationFlowNodes.OrderBy(node => node.Id)
                    .Select(node => new { node.Id, node.ConfigurationJson })
                    .ToArrayAsync(),
                Revisions = await db
                    .AutomationSubflowRevisions.OrderBy(row => row.Id)
                    .Select(row => row.SnapshotJson)
                    .ToArrayAsync(),
                Audits = await db.ConfigurationImportAudits.CountAsync(),
                Activations = await db.ConfigurationActivations.CountAsync(),
            }
        );
    }
}

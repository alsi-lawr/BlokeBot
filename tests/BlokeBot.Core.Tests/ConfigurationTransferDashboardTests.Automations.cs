using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferDashboardTests
{
    [Test]
    public async Task ExportCodecImport_ActualAppliedGraphMapKeepsSelectedIdenticalManualSources()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var source = await HostAsync(database, "source");
        var destination = await HostAsync(database, "destination");
        var flow = await FlowAsync(database, source, "Source", null, null);
        var sourceWidget = new DashboardWidget(
            new(Guid.NewGuid()),
            "Exact source",
            new DashboardWidgetConfiguration.Automation(
                new DashboardBinding.Configured(
                    new DashboardTarget.Automation(new(flow.Flow), new(flow.Nodes[1]))
                ),
                new DashboardTextOverride.Replace("")
            )
        );
        var values = new CustomStoredValueService(database, StoredValueTestChanges.Create());
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            source,
            "secret",
            CustomValueScope.User,
            CustomValueKind.Text,
            "default"
        );
        var original = (
            await values.ValueAsync(source, new(definition.Id, "private-viewer", ""), default)
        )!;
        (
            await values.SaveValueAsync(source, original, CustomValueKind.Text, "private", default)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = source,
                    Name = "Operator",
                    Revision = 8,
                    DocumentJson = DashboardDocuments.Serialize(
                        DashboardDocument.Create(12, (sourceWidget, new(0, 0, 4, 3, 0)))
                    ),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var exported = (
            await Exporter(database)
                .ExportAsync(
                    source,
                    new(
                        new HashSet<ConfigurationSectionId>
                        {
                            ConfigurationSectionId.Dashboards,
                            ConfigurationSectionId.Automations,
                        },
                        new(false, false, false)
                    )
                    {
                        AutomationFlowIds = new HashSet<Guid> { flow.Flow },
                    },
                    default
                )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var document = new ConfigurationDocumentCodec()
            .Parse(exported.Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        var portable = document
            .Sections.Dashboards!.Layouts.Single()
            .Widgets.Single()
            .Configuration.Binding.ShouldBeOfType<DashboardReferenceV1.Automation>();
        var importedSource = document
            .Sections.Automations!.Flows.Single()
            .Nodes.Single(node => node.Id == portable.DocumentSource!.NodeId);
        var apply = await Coordinator(database)
            .ApplyAsync(
                Session(destination),
                document,
                await ReviewedAutomationsAsync(
                    database,
                    document,
                    Selection(
                        destination,
                        ImportConflictStrategy.Merge,
                        ConfigurationSectionId.Dashboards,
                        ConfigurationSectionId.Automations
                    )
                ),
                new("owner", "streamer"),
                default
            );
        _ = apply.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var selected = DashboardDocuments
            .Deserialize((await DashboardsAsync(database, destination)).Single().DocumentJson)
            .Widgets.Single()
            .Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>()
            .Target.ShouldBeOfType<DashboardTarget.Automation>();
        await using var read = await database.CreateDbContextAsync();
        var mapped = await read.AutomationFlowNodes.SingleAsync(node =>
            node.Id == selected.SourceNodeId.Value
        );
        mapped.FlowId.ShouldBe(selected.FlowId.Value);
        (await read.AutomationFlows.SingleAsync(row => row.Id == mapped.FlowId)).HostId.ShouldBe(
            destination
        );
        // Distinguish identical-config sources by the actual imported node's graph position, not a configuration match.
        mapped.CanvasX.ShouldBe(importedSource.CanvasX);
        mapped.CanvasY.ShouldBe(importedSource.CanvasY);
        var destNodes = await read
            .AutomationFlowNodes.Where(node => node.FlowId == selected.FlowId.Value)
            .OrderBy(node => node.CanvasX)
            .ToArrayAsync();
        selected.SourceNodeId.Value.ShouldNotBe(flow.Nodes[1]);
        destNodes.Length.ShouldBe(2);
        (await read.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await read.AutomationCountdowns.CountAsync()).ShouldBe(0);
        (await read.CustomStoredValues.AnyAsync(row => row.HostId == destination)).ShouldBeFalse();
    }

    [Test]
    public async Task AddMissingSkippedOrUnselectedGraphUsesExistingAlias_NotUnstoredMap()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var source = await HostAsync(database, "source");
        var destination = await HostAsync(database, "destination");
        var flow = await FlowAsync(database, source, "Source", "First", "Chosen");
        var existing = await FlowAsync(database, destination, "Source", "Chosen", "Other");
        var config = new DashboardWidgetConfiguration.Automation(
            new DashboardBinding.Configured(
                new DashboardTarget.Automation(new(flow.Flow), new(flow.Nodes[1]))
            ),
            new DashboardTextOverride.Inherit()
        );
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = source,
                    Name = "Operator",
                    Revision = 1,
                    DocumentJson = DashboardDocuments.Serialize(
                        DashboardDocument.Create(
                            12,
                            (new(new(Guid.NewGuid()), "Chosen", config), new(0, 0, 4, 3, 0))
                        )
                    ),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var doc = (
            await Exporter(database)
                .ExportAsync(
                    source,
                    new(
                        new HashSet<ConfigurationSectionId>
                        {
                            ConfigurationSectionId.Dashboards,
                            ConfigurationSectionId.Automations,
                        },
                        new(false, false, false)
                    )
                    {
                        AutomationFlowIds = new HashSet<Guid> { flow.Flow },
                    },
                    default
                )
        )
            .ShouldBeOfType<ConfigurationExportOutcome.Success>()
            .Document;
        foreach (
            var sections in new[]
            {
                (ConfigurationSectionId[])
                    [ConfigurationSectionId.Dashboards, ConfigurationSectionId.Automations],
                [ConfigurationSectionId.Dashboards],
            }
        )
        {
            _ = (
                await Coordinator(database)
                    .ApplyAsync(
                        Session(destination),
                        doc,
                        await ReviewedAutomationsAsync(
                            database,
                            doc,
                            Selection(destination, ImportConflictStrategy.AddMissing, sections)
                        ),
                        new("owner", "streamer"),
                        default
                    )
            ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        }
        foreach (var row in await DashboardsAsync(database, destination))
        {
            var binding = DashboardDocuments
                .Deserialize(row.DocumentJson)
                .Widgets.Single()
                .Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>()
                .Target.ShouldBeOfType<DashboardTarget.Automation>();
            binding.FlowId.Value.ShouldBe(existing.Flow);
            binding.SourceNodeId.Value.ShouldBe(existing.Nodes[0]);
        }
        await using var read = await database.CreateDbContextAsync();
        (
            await read
                .AutomationFlowNodes.Where(node => node.FlowId == existing.Flow)
                .Select(node => node.Id)
                .ToArrayAsync()
        )
            .Order()
            .ShouldBe(existing.Nodes.Order());
    }
}

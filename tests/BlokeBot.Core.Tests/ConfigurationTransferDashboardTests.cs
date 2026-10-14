using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferDashboardTests
{
    [Test]
    public async Task Sqlite_CopyGeneratedBindingsAndRollbackShareImportTransaction() =>
        await ProviderAsync(false);

    [Test, Explicit]
    public async Task PostgreSql_CopyGeneratedBindingsAndRollbackShareImportTransaction() =>
        await ProviderAsync(true);

    private static async Task ProviderAsync(bool postgres)
    {
        var boundary = new AuditBoundary();
        await using var database = postgres
            ? await PointProviderFixture.PostgreSqlAsync(boundary)
            : await PointProviderFixture.SqliteFileAsync(boundary);
        var host = await HostAsync(database, "destination");
        var source = await HostAsync(database, "source");
        var coordinator = Coordinator(database);
        var commands = new CustomCommandsSectionV1(
            "UTC",
            [],
            [],
            [],
            [new("score", CustomValueScope.Global, CustomValueKind.Number, "17")]
        );
        var layout = Layout(
            new DashboardConfigurationV1.Value(
                new DashboardReferenceV1.Value(
                    "score",
                    CustomValueScope.Global,
                    CustomValueKind.Number
                )
            ),
            new DashboardConfigurationV1.Countdown(new DashboardReferenceV1.Countdown("tea"), 60),
            new DashboardConfigurationV1.Guessing(
                new DashboardReferenceV1.Guessing("prediction", "profile")
            )
        );
        layout = layout with
        {
            Desktop = layout.Desktop with { Columns = 16, CellHeight = 72 },
            Phone = new(
                3,
                96,
                [
                    .. layout.Widgets.Select(
                        (w, index) =>
                            new DashboardWidgetPlacementV1(
                                w.Id,
                                new(0, (index * 6) + 1, 3, 4, index)
                            )
                    ),
                ]
            ),
        };
        var profile = new GuessingProfileV1(
            "profile",
            "Prediction",
            "prediction",
            true,
            "0",
            [],
            new("", "", "", "", "", "", "", "", "", "", "", "", ""),
            [new("answer", "answer", ReplyDeliveryTarget.Chat)]
        );
        var document = Document(new([layout]), commands: commands, guessing: new([profile]));
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                Selection(
                    host,
                    ImportConflictStrategy.Merge,
                    ConfigurationSectionId.Dashboards,
                    ConfigurationSectionId.CustomCommands,
                    ConfigurationSectionId.Guessing
                ),
                new("owner", "streamer"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var original = (await DashboardsAsync(database, host)).Single();
        var stored = DashboardDocuments.Deserialize(original.DocumentJson);
        stored.Desktop.Columns.ShouldBe(16);
        stored.Desktop.CellHeight.ShouldBe(72);
        stored.Phone!.Columns.ShouldBe(3);
        stored.Phone.CellHeight.ShouldBe(96);
        stored
            .Widgets.Select(w => w.Id)
            .ShouldBe(stored.Desktop.Placements.Select(p => p.WidgetId));
        stored.Widgets.Select(w => w.Id).ShouldBe(stored.Phone.Placements.Select(p => p.WidgetId));
        stored
            .Phone.Placements.Select(p => p.Placement)
            .ShouldBe([new(0, 1, 3, 4, 0), new(0, 7, 3, 4, 1), new(0, 13, 3, 4, 2)]);
        await using (var read = await database.CreateDbContextAsync())
        {
            var value = stored
                .Widgets[0]
                .Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>()
                .Target.ShouldBeOfType<DashboardTarget.Value>();
            var guessing = stored
                .Widgets[2]
                .Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>()
                .Target.ShouldBeOfType<DashboardTarget.Guessing>();
            (
                await read.CustomValueDefinitions.SingleAsync(row => row.Id == value.DefinitionId)
            ).HostId.ShouldBe(host);
            (await read.Profiles.SingleAsync(row => row.Id == guessing.ProfileId)).HostId.ShouldBe(
                host
            );
            (await read.AutomationCountdowns.CountAsync()).ShouldBe(0);
            (await read.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await read.Rounds.CountAsync()).ShouldBe(0);
            (await read.CustomStoredValues.CountAsync()).ShouldBe(0);
        }
        var copyLayout = layout with
        {
            Widgets = layout
                .Widgets.Select(widget =>
                    widget.Configuration is DashboardConfigurationV1.Guessing
                        ? widget with
                        {
                            Configuration = new DashboardConfigurationV1.Guessing(
                                new DashboardReferenceV1.Guessing("prediction")
                            ),
                        }
                        : widget
                )
                .ToArray(),
        };
        foreach (
            var strategy in new[]
            {
                ImportConflictStrategy.AddMissing,
                ImportConflictStrategy.Merge,
                ImportConflictStrategy.ReplaceSection,
            }
        )
        {
            _ = (
                await coordinator.ApplyAsync(
                    Session(host),
                    Document(new([copyLayout, copyLayout with { Id = "second" }])),
                    Selection(host, strategy, ConfigurationSectionId.Dashboards),
                    new("owner", "streamer"),
                    default
                )
            ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        }
        var copies = await DashboardsAsync(database, host);
        copies.Length.ShouldBe(7);
        copies.Select(row => row.Name).Distinct().Count().ShouldBe(7);
        copies.Select(row => row.Id).Distinct().Count().ShouldBe(7);
        var preserved = copies.Single(row => row.Id == original.Id);
        preserved.Revision.ShouldBe(original.Revision);
        preserved.DocumentJson.ShouldBe(original.DocumentJson);
        (await DashboardsAsync(database, source)).ShouldBeEmpty();

        foreach (var copy in copies)
        {
            var copyDocument = DashboardDocuments.Deserialize(copy.DocumentJson);
            copyDocument.Desktop.Columns.ShouldBe(stored.Desktop.Columns);
            copyDocument.Desktop.CellHeight.ShouldBe(stored.Desktop.CellHeight);
            copyDocument.Phone!.Columns.ShouldBe(stored.Phone.Columns);
            copyDocument.Phone.CellHeight.ShouldBe(stored.Phone.CellHeight);
            copyDocument
                .Phone.Placements.Select(p => p.Placement)
                .ShouldBe(stored.Phone.Placements.Select(p => p.Placement));
            copyDocument
                .Desktop.Placements.Select(p => p.WidgetId)
                .ShouldBe(copyDocument.Widgets.Select(w => w.Id));
            copyDocument
                .Phone.Placements.Select(p => p.WidgetId)
                .ShouldBe(copyDocument.Widgets.Select(w => w.Id));
        }
        boundary.Active = true;
        var failed = await coordinator.ApplyAsync(
            Session(host),
            Document(
                new([copyLayout]),
                commands: commands with
                {
                    StoredDefinitions =
                    [
                        new("new-score", CustomValueScope.Global, CustomValueKind.Number, "99"),
                    ],
                }
            ),
            Selection(
                host,
                ImportConflictStrategy.Merge,
                ConfigurationSectionId.Dashboards,
                ConfigurationSectionId.CustomCommands
            ),
            new("owner", "streamer"),
            default
        );
        _ = failed.ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        (await DashboardsAsync(database, host)).Length.ShouldBe(7);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.CustomValueDefinitions.AnyAsync(row => row.Name == "new-score")
        ).ShouldBeFalse();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(4);
        boundary.Active = false;
        using var cancellation = new CancellationTokenSource();
        boundary.CancelAtAudit = cancellation;
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            coordinator.ApplyAsync(
                Session(host),
                Document(
                    new([copyLayout]),
                    commands: commands with
                    {
                        StoredDefinitions =
                        [
                            new(
                                "cancelled-score",
                                CustomValueScope.Global,
                                CustomValueKind.Number,
                                "99"
                            ),
                        ],
                    }
                ),
                Selection(
                    host,
                    ImportConflictStrategy.Merge,
                    ConfigurationSectionId.Dashboards,
                    ConfigurationSectionId.CustomCommands
                ),
                new("owner", "streamer"),
                cancellation.Token
            )
        );
        (await DashboardsAsync(database, host)).Length.ShouldBe(7);
        (
            await verify.CustomValueDefinitions.AnyAsync(row => row.Name == "cancelled-score")
        ).ShouldBeFalse();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(4);
        var rejected = await coordinator.ApplyAsync(
            Session(source),
            Document(new([copyLayout])),
            Selection(host, ImportConflictStrategy.Merge, ConfigurationSectionId.Dashboards),
            new("actor", "other"),
            default
        );
        _ = rejected.ShouldBeOfType<ConfigurationImportApplyOutcome.Rejected>();
        (await DashboardsAsync(database, host)).Length.ShouldBe(7);
    }

    [Test]
    public async Task ExistingPortableSourcesResolveUniquelyWithinHost_NotByConfigurationOrFirstNode()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await HostAsync(database, "destination");
        var other = await HostAsync(database, "other");
        var unique = await FlowAsync(database, host, "Unique", "First", "Chosen");
        _ = await FlowAsync(database, host, "Unnamed", null, null);
        _ = await FlowAsync(database, host, "Duplicate", "Chosen", "Chosen");
        _ = await FlowAsync(database, other, "Other only", "Chosen");
        var cases = new[]
        {
            new DashboardReferenceV1.Automation("Unique", "Chosen"),
            new("Unnamed", null),
            new("Duplicate", "Chosen"),
            new("Other only", "Chosen"),
        };
        var document = Document(
            new([
                Layout(
                    cases
                        .Select(binding => new DashboardConfigurationV1.Automation(binding, ""))
                        .ToArray()
                ),
            ])
        );
        _ = (
            await Coordinator(database)
                .ApplyAsync(
                    Session(host),
                    document,
                    Selection(
                        host,
                        ImportConflictStrategy.Merge,
                        ConfigurationSectionId.Dashboards
                    ),
                    new("owner", "streamer"),
                    default
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var widgets = DashboardDocuments
            .Deserialize((await DashboardsAsync(database, host)).Single().DocumentJson)
            .Widgets;
        var selected = widgets[0]
            .Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>()
            .Target.ShouldBeOfType<DashboardTarget.Automation>();
        selected.FlowId.Value.ShouldBe(unique.Flow);
        selected.SourceNodeId.Value.ShouldBe(unique.Nodes[1]);
        foreach (var index in new[] { 1, 2 })
        {
            widgets[index]
                .Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>()
                .Issue.ShouldBe(DashboardBindingIssue.Ambiguous);
        }
        widgets[3]
            .Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>()
            .Issue.ShouldBe(DashboardBindingIssue.Missing);
        widgets
            .Select(widget =>
                widget
                    .Configuration.ShouldBeOfType<DashboardWidgetConfiguration.Automation>()
                    .Data.ShouldBeOfType<DashboardTextOverride.Replace>()
                    .Text
            )
            .ShouldAllBe(text => text == "");
    }
}

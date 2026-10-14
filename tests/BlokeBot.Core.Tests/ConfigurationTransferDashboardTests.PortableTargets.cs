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
    public async Task PortableSettingsRoundTrip_UsesDestinationOwnedTargets_AndRetainsUnresolvedWidgets()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var source = await HostAsync(database, "source");
        var destination = await HostAsync(database, "destination");
        var values = new CustomStoredValueService(database, StoredValueTestChanges.Create());
        var sourceValue = await CustomCommandExecutionTests.DeclareAsync(
            values,
            source,
            "scores",
            CustomValueScope.Global,
            CustomValueKind.Dictionary,
            "{}"
        );
        var targetValue = await CustomCommandExecutionTests.DeclareAsync(
            values,
            destination,
            "scores",
            CustomValueScope.Global,
            CustomValueKind.Dictionary,
            "{}"
        );
        _ = await CustomCommandExecutionTests.DeclareAsync(
            values,
            destination,
            "wrong-kind",
            CustomValueScope.Global,
            CustomValueKind.Text,
            "text"
        );
        TwitchPollTemplate sourcePoll;
        TwitchPollTemplate destinationPoll;
        TwitchPredictionTemplate sourcePrediction;
        GuessRoundProfile sourceProfile;
        GuessRoundProfile targetProfile;
        await using (var db = await database.CreateDbContextAsync())
        {
            sourcePoll = new()
            {
                HostId = source,
                Title = "Choose a tea",
                DurationSeconds = 30,
            };
            destinationPoll = new()
            {
                HostId = destination,
                Title = "Choose a tea",
                DurationSeconds = 60,
            };
            sourcePrediction = new()
            {
                HostId = source,
                Title = "Winner",
                PredictionWindowSeconds = 30,
            };
            sourceProfile = new()
            {
                HostId = source,
                Name = "Guess",
                Slug = "guess",
                IsDefault = true,
                Revision = 1,
            };
            targetProfile = new()
            {
                HostId = destination,
                Name = "Guess",
                Slug = "guess",
                IsDefault = true,
                Revision = 1,
            };
            db.TwitchPollTemplates.AddRange(sourcePoll, destinationPoll);
            db.TwitchPredictionTemplates.AddRange(
                sourcePrediction,
                new()
                {
                    HostId = destination,
                    Title = "Winner",
                    PredictionWindowSeconds = 30,
                },
                new()
                {
                    HostId = destination,
                    Title = "Winner",
                    PredictionWindowSeconds = 60,
                }
            );
            db.Profiles.AddRange(sourceProfile, targetProfile);
            _ = await db.SaveChangesAsync();
            var configurations = new DashboardWidgetConfiguration[]
            {
                new DashboardWidgetConfiguration.Value(
                    new DashboardBinding.Configured(new DashboardTarget.Value(sourceValue.Id)),
                    new DashboardValueSelection.DictionaryEntry("tea")
                ),
                new DashboardWidgetConfiguration.Countdown(
                    new DashboardBinding.Configured(new DashboardTarget.Countdown("brew")),
                    TimeSpan.FromSeconds(73)
                ),
                new DashboardWidgetConfiguration.Poll(
                    new DashboardBinding.Configured(new DashboardTarget.Poll(sourcePoll.Id))
                ),
                new DashboardWidgetConfiguration.Prediction(
                    new DashboardBinding.Configured(
                        new DashboardTarget.Prediction(sourcePrediction.Id)
                    )
                ),
                new DashboardWidgetConfiguration.Guessing(
                    new DashboardBinding.Configured(new DashboardTarget.Guessing(sourceProfile.Id))
                ),
                new DashboardWidgetConfiguration.Giveaway(
                    new DashboardBinding.Configured(new DashboardTarget.Giveaway())
                ),
                new DashboardWidgetConfiguration.Value(
                    new DashboardBinding.Unconfigured(
                        DashboardBindingIssue.Missing,
                        "Stored value 'wrong-kind' was removed"
                    ),
                    new DashboardValueSelection.Scalar()
                ),
                new DashboardWidgetConfiguration.Poll(
                    new DashboardBinding.Configured(new DashboardTarget.Poll(int.MaxValue))
                ),
            };
            var originalDocument = DashboardDocument.Create(
                12,
                configurations
                    .Select(
                        (configuration, index) =>
                            (
                                new DashboardWidget(
                                    new(Guid.NewGuid()),
                                    $"Control {index}",
                                    configuration
                                ),
                                new DashboardPlacement(index % 3 * 4, index / 3 * 3, 4, 3, index)
                            )
                    )
                    .ToArray()
            );
            originalDocument = originalDocument with
            {
                Desktop = originalDocument.Desktop with { Columns = 16, CellHeight = 80 },
                Phone = originalDocument.Phone! with
                {
                    Columns = 5,
                    CellHeight = 96,
                    Placements =
                    [
                        .. originalDocument
                            .Phone.Placements.Reverse()
                            .Select(p =>
                                p with
                                {
                                    Placement = p.Placement with
                                    {
                                        Column = 1,
                                        Row = (p.Placement.Row * 2) + 1,
                                        Height = 4,
                                    },
                                }
                            ),
                    ],
                },
            };
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = source,
                    Name = "Operator",
                    Revision = 19,
                    DocumentJson = DashboardDocuments.Serialize(originalDocument),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var export = (
            await Exporter(database)
                .ExportAsync(
                    source,
                    new(
                        new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Dashboards },
                        new(false, false, false)
                    ),
                    default
                )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var document = new ConfigurationDocumentCodec()
            .Parse(export.Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        var selection = Selection(
            destination,
            ImportConflictStrategy.Merge,
            ConfigurationSectionId.Dashboards
        );
        var preview = (
            await new ConfigurationImportPreviewService(database).PreviewAsync(
                document,
                selection,
                default
            )
        )
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview;
        preview.CanApply.ShouldBeTrue();
        preview.Sections.Single().Counts.ShouldBe(new(1, 0, 0, 0));
        _ = (
            await Coordinator(database)
                .ApplyAsync(
                    Session(destination),
                    document,
                    selection,
                    new("owner", "streamer"),
                    default
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var original = DashboardDocuments.Deserialize(
            (await DashboardsAsync(database, source)).Single().DocumentJson
        );
        var widgets = DashboardDocuments
            .Deserialize((await DashboardsAsync(database, destination)).Single().DocumentJson)
            .Widgets;
        widgets
            .Select(widget => widget.Label)
            .ShouldBe(original.Widgets.Select(widget => widget.Label));
        var importedGeometry = DashboardDocuments.Deserialize(
            (await DashboardsAsync(database, destination)).Single().DocumentJson
        );
        importedGeometry
            .Desktop.Placements.Select(p => p.Placement)
            .ShouldBe(original.Desktop.Placements.Select(p => p.Placement));
        importedGeometry
            .Phone!.Placements.Select(p => p.Placement)
            .ShouldBe(original.Phone!.Placements.Select(p => p.Placement));
        importedGeometry.Desktop.Columns.ShouldBe(original.Desktop.Columns);
        importedGeometry.Desktop.CellHeight.ShouldBe(original.Desktop.CellHeight);
        importedGeometry.Phone.Columns.ShouldBe(original.Phone.Columns);
        importedGeometry.Phone.CellHeight.ShouldBe(original.Phone.CellHeight);
        widgets.Select(w => w.Id).Intersect(original.Widgets.Select(w => w.Id)).ShouldBeEmpty();
        foreach (var widget in widgets)
        {
            var sourceWidget = original.Widgets.Single(w => w.Label == widget.Label);
            importedGeometry
                .Desktop.Placements.Single(p => p.WidgetId == widget.Id)
                .Placement.ShouldBe(
                    original.Desktop.Placements.Single(p => p.WidgetId == sourceWidget.Id).Placement
                );
            importedGeometry
                .Phone.Placements.Single(p => p.WidgetId == widget.Id)
                .Placement.ShouldBe(
                    original.Phone.Placements.Single(p => p.WidgetId == sourceWidget.Id).Placement
                );
        }
        var value = widgets[0].Configuration.ShouldBeOfType<DashboardWidgetConfiguration.Value>();
        value.Selection.ShouldBe(new DashboardValueSelection.DictionaryEntry("tea"));
        value.Binding.ShouldBe(
            new DashboardBinding.Configured(new DashboardTarget.Value(targetValue.Id))
        );
        var countdown = widgets[1]
            .Configuration.ShouldBeOfType<DashboardWidgetConfiguration.Countdown>();
        countdown.DefaultDuration.ShouldBe(TimeSpan.FromSeconds(73));
        countdown.Binding.ShouldBe(
            new DashboardBinding.Configured(new DashboardTarget.Countdown("brew"))
        );
        widgets[2]
            .Configuration.Binding.ShouldBe(
                new DashboardBinding.Configured(new DashboardTarget.Poll(destinationPoll.Id))
            );
        widgets[3]
            .Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>()
            .Issue.ShouldBe(DashboardBindingIssue.Ambiguous);
        widgets[4]
            .Configuration.Binding.ShouldBe(
                new DashboardBinding.Configured(new DashboardTarget.Guessing(targetProfile.Id))
            );
        widgets[5]
            .Configuration.Binding.ShouldBe(
                new DashboardBinding.Configured(new DashboardTarget.Giveaway())
            );
        widgets[6].Configuration.Binding.ShouldBe(original.Widgets[6].Configuration.Binding);
        widgets[7]
            .Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>()
            .Issue.ShouldBe(DashboardBindingIssue.Missing);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationCountdowns.CountAsync()).ShouldBe(0);
        (await verify.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await verify.Rounds.CountAsync()).ShouldBe(0);
        (await verify.CustomStoredValues.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ExplicitProfileMapping_AddMissingSkipKeepsTheConfirmedDestinationIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await HostAsync(database, "destination");
        GuessRoundProfile target;
        await using (var db = await database.CreateDbContextAsync())
        {
            target = new()
            {
                HostId = host,
                Name = "Different",
                Slug = "different",
                IsDefault = true,
                Revision = 8,
            };
            _ = db.Profiles.Add(target);
            _ = await db.SaveChangesAsync();
        }
        var imported = new GuessingProfileV1(
            "profile",
            "Imported",
            "imported",
            true,
            "0",
            [],
            new("", "", "", "", "", "", "", "", "", "", "", "", ""),
            [new("answer", "answer", ReplyDeliveryTarget.Chat)]
        );
        var document = Document(
            new([
                Layout(
                    new DashboardConfigurationV1.Guessing(
                        new DashboardReferenceV1.Guessing("imported", "profile")
                    )
                ),
            ]),
            guessing: new([imported])
        );
        var selection = new ConfigurationImportSelection(
            host,
            [
                new(ConfigurationSectionId.Dashboards, ImportConflictStrategy.Merge, []),
                new(
                    ConfigurationSectionId.Guessing,
                    ImportConflictStrategy.AddMissing,
                    [new("profile", ImportConflictResolution.Replace, TargetId: target.Id)]
                ),
            ],
            new HashSet<HostFeatureFlags>()
        );
        var preview = (
            await new ConfigurationImportPreviewService(database).PreviewAsync(
                document,
                selection,
                default
            )
        )
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview;
        preview
            .Sections.Single(section => section.Section == ConfigurationSectionId.Guessing)
            .Counts.ShouldBe(new(0, 0, 1, 0));
        var applied = (
            await Coordinator(database)
                .ApplyAsync(Session(host), document, selection, new("owner", "streamer"), default)
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        applied.Result.ChangedSections.ShouldBe([ConfigurationSectionId.Dashboards]);
        DashboardDocuments
            .Deserialize((await DashboardsAsync(database, host)).Single().DocumentJson)
            .Widgets.Single()
            .Configuration.Binding.ShouldBe(
                new DashboardBinding.Configured(new DashboardTarget.Guessing(target.Id))
            );
        await using var verify = await database.CreateDbContextAsync();
        var unchanged = await verify.Profiles.SingleAsync();
        unchanged.Revision.ShouldBe(8);
        unchanged.Slug.ShouldBe("different");
        (await verify.Rounds.CountAsync()).ShouldBe(0);
    }
}

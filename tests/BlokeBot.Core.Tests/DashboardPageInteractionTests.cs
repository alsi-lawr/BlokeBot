using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Dashboards.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DashboardPageInteractionTests
{
    [Test]
    [Arguments("Cancel changes")]
    [Arguments("Undo")]
    [Arguments("Redo")]
    [Arguments("Delete First")]
    [Arguments("Arrangement")]
    [Arguments("Columns")]
    [Arguments("Cell height")]
    public async Task QueuedPlacementsCannotReverseLocalBoundary_NewOriginStillApplies(
        string boundary
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var first = new DashboardWidget(
            new(Guid.NewGuid()),
            "First",
            new DashboardWidgetConfiguration.Giveaway(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected, null)
            )
        );
        var second = first with { Id = new(Guid.NewGuid()), Label = "Second" };
        var dashboard = Guid.NewGuid();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost { Login = "streamer", DisplayName = "Streamer" };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.ChannelDashboards.Add(
                new ChannelDashboard
                {
                    Id = dashboard,
                    HostId = hostId,
                    Name = "Baseline",
                    Revision = 1,
                    DocumentJson = DashboardDocuments.Serialize(
                        DashboardDocument.Create(
                            12,
                            (first, new(0, 0, 4, 3, 0)),
                            (second, new(4, 0, 4, 3, 1))
                        )
                    ),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        await using var ui = UiTestContextFactory.Create(database, hostId);
        _ = ui.Services.AddSingleton<DashboardAuthority>();
        _ = ui.Services.AddSingleton<DashboardConfigurationService>();
        _ = ui.Services.AddSingleton(
            TestEventBus.CreateContinueAndRecord<DashboardRecentViewersChanged>().Events
        );
        _ = ui.Services.AddSingleton<DashboardRecentViewers>();
        _ = ui.ComponentFactories.AddStub<DashboardWidgetControl>();
        _ = ui.ComponentFactories.AddStub<DashboardWidgetSettings>();
        var module = ui.JSInterop.SetupModule("./Features/Dashboards/Page/DashboardPage.razor.js");
        var grid = module.SetupModule("create", _ => true);
        grid.Mode = JSRuntimeMode.Loose;
        var flush = grid.Setup<DashboardPendingPlacement[]>("flush");
        _ = flush.SetResult([]);
        var navigation = ui.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/dashboards/editor/{dashboard}");
        var page = ui.Render<DashboardPage>(p => p.Add(x => x.Dashboard, dashboard));
        page.WaitForAssertion(() => grid.Invocations["sync"].ShouldNotBeEmpty());
        page.Find("#dashboard-name").Input("Renamed");
        if (boundary == "Redo")
        {
            await page.Find("button[aria-label='Undo']").ClickAsync(new());
        }
        var create = module.Invocations["create"].Single();
        var route = (long)create.Arguments[2]!;
        long Origin() => (long)grid.Invocations["sync"].Last().Arguments[3]!;
        var oldOrigin = Origin();
        switch (boundary)
        {
            case "Arrangement":
                page.Find("#dashboard-arrangement").Change("Phone");
                break;
            case "Columns":
                page.Find("#dashboard-columns").Change("16");
                break;
            case "Cell height":
                page.Find("#dashboard-cell-height").Change("96");
                break;
            default:
                await page.Find($"button[aria-label='{boundary}']").ClickAsync(new());
                break;
        }
        page.WaitForAssertion(() => Origin().ShouldBeGreaterThan(oldOrigin));
        await page.InvokeAsync(() =>
            page.Instance.PlacementChanged(
                route,
                oldOrigin,
                first.Id.Value.ToString(),
                new(0, 10, 4, 3),
                false,
                1
            )
        );
        await page.InvokeAsync(() =>
            page.Instance.PlacementChanged(
                route,
                oldOrigin,
                second.Id.Value.ToString(),
                new(2, 12, 4, 3),
                false,
                2
            )
        );
        page.Find("#dashboard-name")
            .GetAttribute("value")
            .ShouldBe(boundary is not ("Cancel changes" or "Undo") ? "Renamed" : "Baseline");
        page.Find("button[aria-label='Save dashboard']").Click();
        page.WaitForAssertion(() =>
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue()
        );
        await using (var db = await database.CreateDbContextAsync())
        {
            var saved = await db.ChannelDashboards.FindAsync(dashboard);
            var document = DashboardDocuments.Deserialize(saved!.DocumentJson);
            DashboardWidget[] expected = boundary == "Delete First" ? [second] : [first, second];
            document.Widgets.ShouldBe(expected);
            document
                .Desktop.Placements.Single(p => p.WidgetId == second.Id)
                .Placement.ShouldBe(new(4, 0, 4, 3, 1));
            document
                .Phone!.Placements.Single(p => p.WidgetId == second.Id)
                .Placement.ShouldBe(new(0, 3, 4, 3, 1));
            document.Desktop.Columns.ShouldBe(boundary == "Columns" ? 16 : 12);
            document.Desktop.CellHeight.ShouldBe(boundary == "Cell height" ? 96 : 56);
        }

        var newOrigin = Origin();
        await page.InvokeAsync(() =>
            page.Instance.PlacementChanged(
                route,
                newOrigin,
                second.Id.Value.ToString(),
                new(0, 15, 4, 3),
                false,
                3
            )
        );
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeFalse();
        await page.Find("button[aria-label='Undo']").ClickAsync(new());
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();

        _ = flush.SetResult([
            new(second.Id.Value.ToString(), new(0, 15, 4, 3), false, 4, Origin()),
        ]);
        await page.Find("button[aria-label='Save dashboard']").ClickAsync(new());
        page.WaitForAssertion(() =>
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue()
        );
        await using var persisted = await database.CreateDbContextAsync();
        var row = await persisted.ChannelDashboards.FindAsync(dashboard);
        DashboardDocuments
            .Deserialize(row!.DocumentJson)
            .Arrangement(
                boundary == "Arrangement"
                    ? DashboardArrangementKind.Phone
                    : DashboardArrangementKind.Desktop
            )
            .Placements.Single(w => w.WidgetId == second.Id)
            .Placement.ShouldBe(new(0, 15, 4, 3, 1));
    }
}

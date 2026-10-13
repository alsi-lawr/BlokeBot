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
    public async Task WithheldAndQueuedPlacementsCannotReverseLocalBoundary_NewOriginStillApplies(
        string boundary
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var first = new DashboardWidget(
            new(Guid.NewGuid()),
            "First",
            new(0, 0, 4, 3, 0),
            new DashboardWidgetConfiguration.Giveaway(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected, null)
            )
        );
        var second = first with
        {
            Id = new(Guid.NewGuid()),
            Label = "Second",
            Placement = new(4, 0, 4, 3, 1),
        };
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
                    DocumentJson = DashboardDocuments.Serialize(new(12, [first, second])),
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
        var confirmation = ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task withheld = Task.CompletedTask;
        Task queued = Task.CompletedTask;
        await page.InvokeAsync(() =>
        {
            withheld = page.Instance.PlacementChanged(
                route,
                oldOrigin,
                first.Id.Value.ToString(),
                new(0, 0, 8, 3),
                true,
                1
            );
        });
        page.WaitForAssertion(() => confirmation.Invocations.ShouldNotBeEmpty());
        await page.InvokeAsync(() =>
        {
            queued = page.Instance.PlacementChanged(
                route,
                oldOrigin,
                first.Id.Value.ToString(),
                new(2, 1, 4, 3),
                false,
                2
            );
        });
        withheld.IsCompleted.ShouldBeFalse();
        queued.IsCompleted.ShouldBeFalse();

        await page.Find($"button[aria-label='{boundary}']").ClickAsync(new());
        page.WaitForAssertion(() => Origin().ShouldBeGreaterThan(oldOrigin));
        _ = confirmation.SetResult(true);
        await Task.WhenAll(withheld, queued).WaitAsync(TimeSpan.FromSeconds(5));
        page.Find("#dashboard-name")
            .GetAttribute("value")
            .ShouldBe(boundary is "Redo" or "Delete First" ? "Renamed" : "Baseline");
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
        }

        var newOrigin = Origin();
        await page.InvokeAsync(() =>
            page.Instance.PlacementChanged(
                route,
                newOrigin,
                second.Id.Value.ToString(),
                new(8, 4, 4, 3),
                false,
                3
            )
        );
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeFalse();
        await page.Find("button[aria-label='Undo']").ClickAsync(new());
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();

        _ = flush.SetResult([new(second.Id.Value.ToString(), new(8, 4, 4, 3), false, 4, Origin())]);
        await page.Find("button[aria-label='Save dashboard']").ClickAsync(new());
        page.WaitForAssertion(() =>
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue()
        );
        await using var persisted = await database.CreateDbContextAsync();
        var row = await persisted.ChannelDashboards.FindAsync(dashboard);
        DashboardDocuments
            .Deserialize(row!.DocumentJson)
            .Widgets.Single(w => w.Id == second.Id)
            .Placement.ShouldBe(new(8, 4, 4, 3, 1));
    }
}

using System.Text.Json;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Dashboards.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DashboardPageEditorActionsTests
{
    [Test]
    [Arguments("Gesture", false)]
    [Arguments("Gesture", true)]
    [Arguments("Arrangement", false)]
    [Arguments("Arrangement", true)]
    [Arguments("Reload", false)]
    [Arguments("Reload", true)]
    public async Task DelayedToolboxCompletionPreservesNewerPendingEdit(
        string boundary,
        bool accept
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        await page.Find("button[aria-label='Configure First']").ClickAsync(new());
        var confirm = fixture.Ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task? dropping = null;
        await page.InvokeAsync(async () =>
        {
            fixture.Gesture = await page.Instance.GestureStarted(fixture.Route, fixture.Origin);
            await page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, fixture.Gesture);
            dropping = page.Instance.ToolboxDropped(
                fixture.Route,
                fixture.Origin,
                "Countdown",
                0,
                0
            );
        });
        page.WaitForAssertion(() => confirm.Invocations.ShouldHaveSingleItem());
        if (boundary == "Arrangement")
        {
            page.Find("#dashboard-arrangement").Change("Phone");
        }
        else if (boundary == "Reload")
        {
            await page.InvokeAsync(() =>
                page.Instance.SetParametersAsync(
                    ParameterView.FromDictionary(
                        new Dictionary<string, object?>
                        {
                            [nameof(DashboardPage.Dashboard)] = fixture.Dashboard,
                        }
                    )
                )
            );
        }
        await page.Find("button[aria-label='Configure Second']").ClickAsync(new());
        page.Find("#dashboard-columns").Change("3");
        var feedback = page.Find(".dashboard-feedback").TextContent;
        await page.InvokeAsync(async () =>
        {
            fixture.Gesture = await page.Instance.GestureStarted(fixture.Route, fixture.Origin);
            await page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, fixture.Gesture);
        });
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Unsaved");
        var geometry = fixture.Geometry;
        var origin = fixture.Origin;
        var focusRequests = fixture.Grid.Invocations.Count(i => i.Identifier == "focusPanel");
        var cancellations = fixture.Grid.Invocations.Count(i => i.Identifier == "cancel");

        _ = confirm.SetResult(accept);
        await dropping!;

        page.Find(".dashboard-save-state").TextContent.ShouldBe("Unsaved");
        page.Find(".dashboard-feedback").TextContent.ShouldBe(feedback);
        fixture.Geometry.ShouldBe(geometry);
        fixture.Origin.ShouldBe(origin);
        fixture.Grid.Invocations.Count(i => i.Identifier == "focusPanel").ShouldBe(focusRequests);
        fixture.Grid.Invocations.Count(i => i.Identifier == "cancel").ShouldBe(cancellations);
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("button[aria-label='Redo']").HasAttribute("disabled").ShouldBeTrue();
        var menu = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(fixture.Context, false, null)
        );
        menu!.Target.ShouldBe("Second");
        await page.InvokeAsync(() =>
            page.Instance.PlacementChanged(
                fixture.Route,
                origin,
                fixture.First.ToString(),
                new(0, 6, 4, 3),
                false,
                1,
                fixture.Gesture
            )
        );
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeFalse();
        await page.Find("button[aria-label='Undo']").ClickAsync(new());
        fixture.Geometry.ShouldBe(geometry);
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Test]
    [Arguments("Accept")]
    [Arguments("Cancel")]
    [Arguments("Bounds")]
    public async Task CurrentToolboxDropFinalizesOnlyItsOwnPendingEdit(string outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        var geometry = fixture.Geometry;
        var confirm = fixture.Ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task? dropping = null;
        await page.InvokeAsync(async () =>
        {
            fixture.Gesture = await page.Instance.GestureStarted(fixture.Route, fixture.Origin);
            await page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, fixture.Gesture);
            dropping = page.Instance.ToolboxDropped(
                fixture.Route,
                fixture.Origin,
                "Countdown",
                outcome == "Bounds" ? 99 : 0,
                0
            );
        });
        if (outcome != "Bounds")
        {
            page.WaitForAssertion(() => confirm.Invocations.ShouldHaveSingleItem());
            _ = confirm.SetResult(outcome == "Accept");
        }
        await dropping!;
        if (outcome == "Accept")
        {
            var menu = await page.InvokeAsync(() =>
                page.Instance.PrepareEditorMenuAsync(fixture.Context, false, null)
            );
            menu!.Target.ShouldBe("Countdown");
            fixture.CardCount.ShouldBe(1);
            page.Find("#dashboard-arrangement").Change("Phone");
            fixture.CardCount.ShouldBe(1);
            await page.Find("button[aria-label='Undo']").ClickAsync(new());
            fixture.CardCount.ShouldBe(2);
            page.Find("#dashboard-arrangement").Change("Desktop");
            fixture.Geometry.ShouldBe(geometry);
            page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
            await page.Find("button[aria-label='Redo']").ClickAsync(new());
            fixture.CardCount.ShouldBe(1);
        }
        else
        {
            fixture.Geometry.ShouldBe(geometry);
            page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
            page.Find("button[aria-label='Redo']").HasAttribute("disabled").ShouldBeTrue();
        }
    }

    [Test]
    [Arguments("Accept")]
    [Arguments("Cancel")]
    [Arguments("Arrangement")]
    [Arguments("Rename")]
    [Arguments("Selection")]
    [Arguments("Gesture")]
    [Arguments("Cancel changes")]
    [Arguments("Reload")]
    public async Task AwaitedConfirmationCannotApproveAnotherEditorContext(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        var confirm = fixture.Ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task? pending = null;
        await page.InvokeAsync(() =>
        {
            pending = page.Instance.PlacementChanged(
                fixture.Route,
                fixture.Origin,
                fixture.First.ToString(),
                new(0, 0, 8, 3),
                true,
                1,
                fixture.Gesture
            );
        });
        page.WaitForAssertion(() => confirm.Invocations.ShouldHaveSingleItem());
        confirm.Invocations.Single().Arguments[0]!.ToString()!.ShouldContain("Second");
        confirm.Invocations.Single().Arguments[0]!
            .ToString()!
            .ShouldContain("both Desktop and Phone");
        fixture.CardCount.ShouldBe(2);
        switch (boundary)
        {
            case "Arrangement":
                page.Find("#dashboard-arrangement").Change("Phone");
                break;
            case "Rename":
                page.Find("#dashboard-name").Input("New name");
                break;
            case "Selection":
                await page.Find("button[aria-label='Configure Second']").ClickAsync(new());
                break;
            case "Gesture":
                fixture.Gesture = await page.InvokeAsync(() =>
                    page.Instance.GestureStarted(fixture.Route, fixture.Origin)
                );
                break;
            case "Cancel changes":
                await page.Find("button[aria-label='Cancel changes']").ClickAsync(new());
                break;
            case "Reload":
                await page.InvokeAsync(() =>
                    page.Instance.SetParametersAsync(
                        ParameterView.FromDictionary(
                            new Dictionary<string, object?>
                            {
                                [nameof(DashboardPage.Dashboard)] = fixture.Dashboard,
                            }
                        )
                    )
                );
                break;
        }
        _ = confirm.SetResult(boundary != "Cancel");
        await pending!;
        if (boundary == "Accept")
        {
            fixture.CardCount.ShouldBe(1);
            await page.Find("button[aria-label='Undo']").ClickAsync(new());
            fixture.CardCount.ShouldBe(2);
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
            await page.Find("button[aria-label='Redo']").ClickAsync(new());
            fixture.CardCount.ShouldBe(1);
            page.Find("#dashboard-arrangement").Change("Phone");
            fixture.CardCount.ShouldBe(1);
        }
        else
        {
            fixture.CardCount.ShouldBe(2);
            if (boundary != "Rename")
            {
                page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
                page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
            }
        }
    }

    [Test]
    [Arguments("Arrangement")]
    [Arguments("Selection")]
    [Arguments("Rename")]
    [Arguments("Reload")]
    [Arguments("Gesture")]
    [Arguments("Controls")]
    public async Task MenuPreparationAndExecutionRejectStaleOwnership(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        var original = fixture.Context;
        var prepared = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(original, true, fixture.Second)
        );
        _ = prepared.ShouldNotBeNull();
        prepared.Target.ShouldBe("Second");
        prepared.Delete.ShouldBeTrue();
        prepared.Undo.ShouldBeFalse();
        prepared.Redo.ShouldBeFalse();
        switch (boundary)
        {
            case "Arrangement":
                page.Find("#dashboard-arrangement").Change("Phone");
                break;
            case "Selection":
                await page.Find("button[aria-label='Configure First']").ClickAsync(new());
                break;
            case "Rename":
                page.Find("#dashboard-name").Input("New name");
                break;
            case "Reload":
                await page.InvokeAsync(() =>
                    page.Instance.SetParametersAsync(
                        ParameterView.FromDictionary(
                            new Dictionary<string, object?>
                            {
                                [nameof(DashboardPage.Dashboard)] = fixture.Dashboard,
                            }
                        )
                    )
                );
                break;
            case "Gesture":
                fixture.Gesture = await page.InvokeAsync(() =>
                    page.Instance.GestureStarted(fixture.Route, fixture.Origin)
                );
                break;
            case "Controls":
                fixture.Navigation.NavigateTo($"/dashboards/control/{fixture.Dashboard}");
                break;
        }
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Delete, prepared.Context)
        );
        fixture.CardCount.ShouldBe(2);
        var stalePrepare = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(original, true, fixture.First)
        );
        stalePrepare.ShouldBeNull();
    }

    [Test]
    public async Task EmptyGridDoesNotDeletePriorSelection_OpenerUsesSelectionAndSameAggregateHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        _ = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(fixture.Context, true, fixture.Second)
        );
        var empty = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(fixture.Context, true, null)
        );
        empty!.Delete.ShouldBeFalse();
        empty.Target.ShouldBe("No selection");
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Delete, empty.Context)
        );
        fixture.CardCount.ShouldBe(2);
        await page.Find("button[aria-label='Configure First']").ClickAsync(new());
        var selected = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(fixture.Context, false, null)
        );
        selected!.Target.ShouldBe("First");
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Delete, selected.Context)
        );
        fixture.CardCount.ShouldBe(1);
        page.Find("#dashboard-arrangement").Change("Phone");
        fixture.CardCount.ShouldBe(1);
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Undo, fixture.Context)
        );
        fixture.CardCount.ShouldBe(2);
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Redo, fixture.Context)
        );
        fixture.CardCount.ShouldBe(1);
    }

    [Test]
    public async Task BusySaveRejectsPreparedMenuAndLatePreparationWithoutTouchingSharedMembership()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        var prepared = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(fixture.Context, true, fixture.Second)
        );
        var pendingFlush = fixture.Grid.Setup<DashboardPendingPlacement[]>("flush");
        Task? saving = null;
        await page.InvokeAsync(() =>
        {
            saving = page.Find("button[aria-label='Save dashboard']").ClickAsync(new());
        });
        page.WaitForAssertion(() =>
            page.Find(".dashboard-edit-boundary").HasAttribute("disabled").ShouldBeTrue()
        );
        var duringSave = await page.InvokeAsync(() =>
            page.Instance.PrepareEditorMenuAsync(fixture.Context, true, fixture.First)
        );
        duringSave.ShouldBeNull();
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Delete, prepared!.Context)
        );
        fixture.CardCount.ShouldBe(2);
        _ = pendingFlush.SetResult([]);
        await saving!;
        fixture.CardCount.ShouldBe(2);
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveFlushConfirmationCancelPreservesHistory_ApprovalPersistsBothArrangements(
        bool accept
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        page.Find("#dashboard-name").Input("Temporary");
        await page.Find("button[aria-label='Undo']").ClickAsync(new());
        _ = fixture
            .Grid.Setup<DashboardPendingPlacement[]>("flush")
            .SetResult([
                new(
                    fixture.First.ToString(),
                    new(0, 0, 8, 3),
                    true,
                    1,
                    fixture.Origin,
                    fixture.Gesture
                ),
            ]);
        var confirm = fixture.Ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task? saving = null;
        await page.InvokeAsync(() =>
        {
            saving = page.Find("button[aria-label='Save dashboard']").ClickAsync(new());
        });
        page.WaitForAssertion(() => confirm.Invocations.ShouldHaveSingleItem());
        _ = confirm.SetResult(accept);
        await saving!;
        fixture.CardCount.ShouldBe(accept ? 1 : 2);
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("button[aria-label='Redo']").HasAttribute("disabled").ShouldBe(accept);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var row = await db.ChannelDashboards.FindAsync(fixture.Dashboard);
        row!.Revision.ShouldBe(accept ? 2 : 1);
        var document = DashboardDocuments.Deserialize(row.DocumentJson);
        document.Widgets.Length.ShouldBe(accept ? 1 : 2);
        document.Desktop.Placements.Length.ShouldBe(accept ? 1 : 2);
        document.Phone!.Placements.Length.ShouldBe(accept ? 1 : 2);
    }

    [Test]
    public async Task CancelledReleasedCollisionRendersSavedWithoutConsumingExistingRedo()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        page.Find("#dashboard-name").Input("Temporary");
        await page.Find("button[aria-label='Undo']").ClickAsync(new());
        var geometry = fixture.Geometry;
        fixture.Gesture = await page.InvokeAsync(() =>
            page.Instance.GestureStarted(fixture.Route, fixture.Origin)
        );
        await page.InvokeAsync(() =>
            page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, fixture.Gesture)
        );
        var confirm = fixture.Ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task? pending = null;
        await page.InvokeAsync(() =>
        {
            pending = page.Instance.PlacementChanged(
                fixture.Route,
                fixture.Origin,
                fixture.First.ToString(),
                new(0, 0, 8, 3),
                true,
                1,
                fixture.Gesture
            );
        });
        page.WaitForAssertion(() => confirm.Invocations.ShouldHaveSingleItem());
        _ = confirm.SetResult(false);
        await pending!;
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
        fixture.Geometry.ShouldBe(geometry);
        fixture.CardCount.ShouldBe(2);
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("button[aria-label='Redo']").HasAttribute("disabled").ShouldBeFalse();
    }

    [Test]
    public async Task CancelledAndOlderGestureCallbacksCannotDirtyOrFinalizeTheCurrentPreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        var geometry = fixture.Geometry;
        var first = await page.InvokeAsync(() =>
            page.Instance.GestureStarted(fixture.Route, fixture.Origin)
        );
        var current = await page.InvokeAsync(() =>
            page.Instance.GestureStarted(fixture.Route, fixture.Origin)
        );
        await page.InvokeAsync(() =>
            page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, current)
        );
        await page.InvokeAsync(() =>
            page.Instance.GestureCancelled(fixture.Route, fixture.Origin, first, false)
        );
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Unsaved");
        await page.InvokeAsync(() =>
            page.Instance.GestureCancelled(fixture.Route, fixture.Origin, current, false)
        );
        await page.InvokeAsync(() =>
            page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, current)
        );
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Saved");
        fixture.Geometry.ShouldBe(geometry);
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ReleasedPlacementCompletionPreservesNewerPreviewAndFinalizesCancelledPending(
        bool accept,
        bool cancelNewer
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page;
        var geometry = fixture.Geometry;
        var released = await page.InvokeAsync(() =>
            page.Instance.GestureStarted(fixture.Route, fixture.Origin)
        );
        await page.InvokeAsync(() =>
            page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, released)
        );
        var confirm = fixture.Ui.JSInterop.Setup<bool>("confirm", _ => true);
        Task? pending = null;
        await page.InvokeAsync(() =>
        {
            pending = page.Instance.PlacementChanged(
                fixture.Route,
                fixture.Origin,
                fixture.First.ToString(),
                new(0, 0, 8, 3),
                true,
                1,
                released
            );
        });
        page.WaitForAssertion(() => confirm.Invocations.ShouldHaveSingleItem());
        var current = await page.InvokeAsync(() =>
            page.Instance.GestureStarted(fixture.Route, fixture.Origin)
        );
        await page.InvokeAsync(() =>
            page.Instance.LocalEditStarted(fixture.Route, fixture.Origin, current)
        );
        if (cancelNewer)
        {
            await page.InvokeAsync(() =>
                page.Instance.GestureCancelled(fixture.Route, fixture.Origin, current, true)
            );
        }
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Unsaved");
        var cancellations = fixture.Grid.Invocations.Count(i => i.Identifier == "cancel");
        _ = confirm.SetResult(accept);
        await pending!;
        page.Find(".dashboard-save-state").TextContent.ShouldBe(cancelNewer ? "Saved" : "Unsaved");
        fixture.Grid.Invocations.Count(i => i.Identifier == "cancel").ShouldBe(cancellations);
        fixture.Geometry.ShouldBe(geometry);
        fixture.CardCount.ShouldBe(2);
        page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("button[aria-label='Redo']").HasAttribute("disabled").ShouldBeTrue();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal required SqliteBlokeBotDbFactory Database { get; init; }
        internal required BunitContext Ui { get; init; }
        internal required IRenderedComponent<DashboardPage> Page { get; init; }
        internal required BunitJSModuleInterop Grid { get; init; }
        internal required BunitJSModuleInterop Module { get; init; }
        internal required NavigationManager Navigation { get; init; }
        internal required Guid Dashboard { get; init; }
        internal required Guid First { get; init; }
        internal required Guid Second { get; init; }
        internal long Gesture { get; set; }
        internal long Route => (long)Module.Invocations["create"].Last().Arguments[2]!;
        internal long Origin => (long)Grid.Invocations["sync"].Last().Arguments[3]!;
        internal string Geometry =>
            JsonSerializer.Serialize(Grid.Invocations["sync"].Last().Arguments[0]);
        internal string Context =>
            Page.Find("[data-editor-menu-context]").GetAttribute("data-editor-menu-context")!;
        internal int CardCount => Page.FindAll("[data-widget-id]").Count;

        internal static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var first = new DashboardWidget(
                new(Guid.NewGuid()),
                "First",
                new DashboardWidgetConfiguration.Giveaway(
                    new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected)
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
            var ui = UiTestContextFactory.Create(database, hostId);
            _ = ui.Services.AddSingleton<DashboardAuthority>();
            _ = ui.Services.AddSingleton<DashboardConfigurationService>();
            _ = ui.Services.AddSingleton(
                TestEventBus.CreateContinueAndRecord<DashboardRecentViewersChanged>().Events
            );
            _ = ui.Services.AddSingleton<DashboardRecentViewers>();
            _ = ui.ComponentFactories.AddStub<DashboardWidgetControl>();
            _ = ui.ComponentFactories.AddStub<DashboardWidgetSettings>();
            var module = ui.JSInterop.SetupModule(
                "./Features/Dashboards/Page/DashboardPage.razor.js"
            );
            var grid = module.SetupModule("create", _ => true);
            grid.Mode = JSRuntimeMode.Loose;
            _ = grid.Setup<DashboardPendingPlacement[]>("flush").SetResult([]);
            var navigation = ui.Services.GetRequiredService<NavigationManager>();
            navigation.NavigateTo($"/dashboards/editor/{dashboard}");
            var page = ui.Render<DashboardPage>(p => p.Add(x => x.Dashboard, dashboard));
            page.WaitForAssertion(() => grid.Invocations["sync"].ShouldNotBeEmpty());
            return new()
            {
                Database = database,
                Ui = ui,
                Page = page,
                Grid = grid,
                Module = module,
                Navigation = navigation,
                Dashboard = dashboard,
                First = first.Id.Value,
                Second = second.Id.Value,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Ui.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}

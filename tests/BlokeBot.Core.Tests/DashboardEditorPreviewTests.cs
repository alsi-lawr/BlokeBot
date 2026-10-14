using System.Data.Common;
using System.Globalization;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Dashboards.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DashboardEditorPreviewTests
{
    [Test]
    [Arguments(7)]
    [Arguments(30)]
    public async Task AuthorizedMetadataIsSharedAndSettingsAndGeometryDoNotReloadIt(int count)
    {
        var reads = new PollMetadataReads();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(reads);
        var dashboard = Guid.NewGuid();
        int hostId;
        int localId;
        int foreignId;
        await using (var db = database.CreateDbContext())
        {
            var host = new BotHost { Login = "streamer", DisplayName = "Streamer" };
            var other = new BotHost { Login = "other", DisplayName = "Other" };
            db.Hosts.AddRange(host, other);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            var local = new TwitchPollTemplate
            {
                HostId = hostId,
                Title = "Local question",
                DurationSeconds = 60,
            };
            var foreign = new TwitchPollTemplate
            {
                HostId = other.Id,
                Title = "Private other question",
                DurationSeconds = 60,
            };
            db.TwitchPollTemplates.AddRange(local, foreign);
            _ = await db.SaveChangesAsync();
            localId = local.Id;
            foreignId = foreign.Id;
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = dashboard,
                    HostId = hostId,
                    Name = "Preview workload",
                    Revision = 1,
                    DocumentJson = DashboardDocuments.Serialize(
                        DashboardDocument.Create(
                            12,
                            Enumerable
                                .Range(0, count)
                                .Select(i =>
                                    (
                                        new DashboardWidget(
                                            new(Guid.NewGuid()),
                                            $"Poll {i}",
                                            new DashboardWidgetConfiguration.Poll(
                                                new DashboardBinding.Configured(
                                                    new DashboardTarget.Poll(localId)
                                                )
                                            )
                                        ),
                                        new DashboardPlacement(i % 2 * 6, i / 2 * 5, 6, 5, i)
                                    )
                                )
                                .ToArray()
                        )
                    ),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        await using var ui = UiTestContextFactory.Create(database, hostId);
        _ = ui.Services.AddSingleton<DashboardAuthority>();
        _ = ui.Services.AddSingleton<DashboardTargetCatalog>();
        _ = ui.Services.AddSingleton<DashboardConfigurationService>();
        _ = ui.Services.AddSingleton(
            TestEventBus.CreateContinueAndRecord<DashboardRecentViewersChanged>().Events
        );
        _ = ui.Services.AddSingleton<DashboardRecentViewers>();
        var previews = new List<DashboardEditorTargets>();
        _ = ui.ComponentFactories.AddStub<DashboardWidgetControl>(parameters =>
        {
            parameters.Get(x => x.Editing).ShouldBeTrue();
            previews.Add(parameters.Get(x => x.EditorTargets));
            return _ => { };
        });
        var module = ui.JSInterop.SetupModule("./Features/Dashboards/Page/DashboardPage.razor.js");
        var grid = module.SetupModule("create", _ => true);
        grid.Mode = JSRuntimeMode.Loose;
        _ = grid.Setup<DashboardPendingPlacement[]>("flush").SetResult([]);
        ui.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/dashboards/editor/{dashboard}");
        var page = ui.Render<DashboardPage>(p => p.Add(x => x.Dashboard, dashboard));
        page.WaitForAssertion(() => previews.Count.ShouldBeGreaterThanOrEqualTo(count));
        reads.Count.ShouldBe(1);
        var metadata = previews.Last();
        metadata
            .Label(
                new DashboardWidgetConfiguration.Poll(
                    new DashboardBinding.Configured(new DashboardTarget.Poll(localId))
                )
            )
            .ShouldBe("Local question");
        metadata
            .Label(
                new DashboardWidgetConfiguration.Poll(
                    new DashboardBinding.Configured(new DashboardTarget.Poll(foreignId))
                )
            )
            .ShouldBeNull();
        previews.ShouldAllBe(targets => ReferenceEquals(targets, metadata));
        await page.Find("button[aria-label='Configure Poll 0']").ClickAsync(new());
        page.FindComponent<DashboardWidgetSettings>().Instance.Targets.ShouldBeSameAs(metadata);
        await page.Find("button[aria-label='Close settings']").ClickAsync(new());
        await page.Find("button[aria-label='Configure Poll 1']").ClickAsync(new());
        await page.Find("button[aria-label='Close settings']").ClickAsync(new());
        var context = page.Find("[data-editor-menu-context]")
            .GetAttribute("data-editor-menu-context")!;
        await page.InvokeAsync(() =>
            page.Instance.ApplyEditorMenuAsync(DashboardEditorMenuAction.Narrow, context)
        );
        page.Find(".dashboard-save-state").TextContent.ShouldBe("Unsaved");
        reads.Count.ShouldBe(1);
        previews.ShouldAllBe(targets => ReferenceEquals(targets, metadata));
    }

    [Test]
    [Arguments(300)]
    [Arguments(86400)]
    [Arguments(90000)]
    [Arguments(31536000)]
    public void CountdownPreviewRepresentsTheWholeSavedDuration(int seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        var widget = new DashboardWidget(
            new(Guid.NewGuid()),
            "Break",
            new DashboardWidgetConfiguration.Countdown(
                new DashboardBinding.Configured(new DashboardTarget.Countdown("break")),
                duration
            )
        );
        var saved = DashboardDocuments.Deserialize(
            DashboardDocuments.Serialize(DashboardDocument.Create(12, (widget, new(0, 0, 6, 5, 0))))
        );
        using var ui = new BunitContext();
        var preview = ui.Render<DashboardWidgetPreview>(p =>
            p.Add(x => x.View, new DashboardWidgetView(saved.Widgets.Single()))
                .Add(x => x.Targets, new DashboardEditorTargets(new()))
        );
        TimeSpan
            .Parse(preview.Find(".dashboard-value").TextContent, CultureInfo.InvariantCulture)
            .ShouldBe(duration);
    }

    private sealed class PollMetadataReads : DbCommandInterceptor
    {
        internal int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains(
                    "FROM \"twitch_poll_templates\"",
                    StringComparison.Ordinal
                )
            )
            {
                ++Count;
            }
            return ValueTask.FromResult(result);
        }
    }
}

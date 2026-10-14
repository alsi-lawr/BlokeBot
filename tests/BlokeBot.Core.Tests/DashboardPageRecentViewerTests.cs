using System.Collections.Immutable;
using System.Data.Common;
using BlokeBot.Core.Features.CustomCommands;
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

public sealed class DashboardPageRecentViewerTests
{
    [Test]
    public async Task ThirtyWidgetsShareOneRecentProjectionAndHiddenPageResyncsWhenShown()
    {
        var reads = new RecentProjectionReads();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(reads);
        var dashboard = Guid.NewGuid();
        int hostId;
        await using (var db = database.CreateDbContext())
        {
            var host = new BotHost { Login = "streamer", DisplayName = "Streamer" };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.RecentChannelChatters.Add(
                new()
                {
                    HostId = hostId,
                    TwitchUserId = "first-id",
                    Login = "first",
                    DisplayName = "First",
                    ObservedAtUtc = DateTime.UtcNow,
                }
            );
            var widgets = Enumerable
                .Range(0, 30)
                .Select(i => new DashboardWidget(
                    new(Guid.NewGuid()),
                    $"Value {i}",
                    new DashboardWidgetConfiguration.Value(
                        new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected),
                        new DashboardValueSelection.Scalar()
                    )
                ))
                .ToImmutableArray();
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = dashboard,
                    HostId = hostId,
                    Name = "Thirty widgets",
                    Revision = 1,
                    DocumentJson = DashboardDocuments.Serialize(
                        DashboardDocument.Create(
                            12,
                            widgets
                                .Select(
                                    (widget, i) =>
                                        (
                                            widget,
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
        _ = ui.Services.AddSingleton<DashboardConfigurationService>();
        _ = ui.Services.AddSingleton(
            TestEventBus.CreateContinueAndRecord<DashboardRecentViewersChanged>().Events
        );
        _ = ui.Services.AddSingleton<DashboardRecentViewers>();
        DashboardRecentViewerView? projection = null;
        _ = ui.ComponentFactories.AddStub<DashboardWidgetControl>(parameters =>
        {
            projection = parameters.Get(x => x.RecentViewers);
            return _ => { };
        });
        var module = ui.JSInterop.SetupModule("./Features/Dashboards/Page/DashboardPage.razor.js");
        var grid = module.SetupModule("create", _ => true);
        grid.Mode = JSRuntimeMode.Loose;
        ui.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/dashboards/control/{dashboard}");
        var page = ui.Render<DashboardPage>(p => p.Add(x => x.Dashboard, dashboard));
        ImmutableArray<CustomCommandViewer> Viewers() =>
            (
                projection?.Result as DashboardResult<ImmutableArray<CustomCommandViewer>>.Succeeded
            )?.Value
            ?? [];
        page.WaitForAssertion(() => Viewers().Single().TwitchUserId.ShouldBe("first-id"));
        reads.Count.ShouldBe(1);
        var route = (long)module.Invocations["create"].Last().Arguments[2]!;
        await page.InvokeAsync(() => page.Instance.VisibilityChanged(route, false));
        await ui
            .Services.GetRequiredService<DashboardRecentViewers>()
            .MessageReceivedAsync(
                new ChatMessage(
                    "second",
                    "streamer",
                    "never retained",
                    "never retained",
                    new Dictionary<string, string> { ["user-id"] = "second-id" }
                ),
                CancellationToken.None
            );
        Viewers().Single().TwitchUserId.ShouldBe("first-id");
        reads.Count.ShouldBe(1);
        await page.InvokeAsync(() => page.Instance.VisibilityChanged(route, true));
        page.WaitForAssertion(() =>
            Viewers().Select(v => v.TwitchUserId).ShouldContain("second-id")
        );
        reads.Count.ShouldBe(2);
    }

    private sealed class RecentProjectionReads : DbCommandInterceptor
    {
        private int _count;
        internal int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            if (
                command.CommandText.Contains(
                    "FROM \"recent_channel_chatters\"",
                    StringComparison.Ordinal
                ) && command.CommandText.Contains("LIMIT", StringComparison.Ordinal)
            )
            {
                _ = Interlocked.Increment(ref _count);
            }
            return ValueTask.FromResult(result);
        }
    }
}

using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class AlertUiTests
{
    [Test]
    public async Task ActiveAlertInTopBar_ClickingIndicator_NavigatesToAlerts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "test",
                "top-bar",
                "Queue delayed",
                "Outbound messages are delayed.",
                "/alerts"
            )
            .RunAsync(CancellationToken.None);
        context.ComponentFactories.AddStub<SelectedChannelBotStatus>();
        context.ComponentFactories.AddStub<HostSelector>();
        context.ComponentFactories.AddStub<AccountMenu>();

        var cut = context.Render<TopBarControls>();

        var button = cut.Find("button[aria-label='1 active alert']");
        button.Click();
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/alerts");
    }

    [Test]
    public async Task SelectedOperator_RenderingSidebar_ShowsAlertsLink()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);

        var cut = context.Render<NavMenu>();

        var alertsLink = cut.FindAll("a")
            .Single(link => link.TextContent.Trim().Equals("Alerts", StringComparison.Ordinal));
        alertsLink.GetAttribute("href").ShouldBe("alerts");
    }

    [Test]
    public async Task ActiveAlertOnAlertsPage_Acknowledging_MovesAlertToHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "test",
                "acknowledge",
                "Queue delayed",
                "Outbound messages are delayed.",
                null
            )
            .RunAsync(CancellationToken.None);

        var cut = context.Render<AlertsPage>();
        cut.Find("button[aria-label='Mark Queue delayed as handled']").Click();

        var state = await alerts.LoadStateAsync(hostId, CancellationToken.None);
        state.Active.ShouldBeEmpty();
        state.History.Single().AcknowledgedByLogin.ShouldBe("streamer");
        cut.FindAll("button[aria-label='Mark Queue delayed as handled']").ShouldBeEmpty();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}

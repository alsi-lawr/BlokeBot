using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.UI.Tests;

public sealed class AlertUiTests
{
    [Test]
    public async Task Top_bar_shows_active_alert_and_navigates_to_alerts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        await alerts.CreateAsync(
            hostId,
            DurableAlertSeverity.Warning,
            "test",
            "top-bar",
            "Queue delayed",
            "Outbound messages are delayed.",
            "/alerts",
            CancellationToken.None
        );
        context.ComponentFactories.AddStub<SelectedChannelBotStatus>();
        context.ComponentFactories.AddStub<HostSelector>();
        context.ComponentFactories.AddStub<AccountMenu>();

        var cut = context.Render<TopBarControls>();

        var button = cut.Find("button[aria-label='1 active alert']");
        button.Click();
        context
            .Services.GetRequiredService<NavigationManager>()
            .Uri.ShouldEndWith("/alerts");
    }

    [Test]
    public async Task Sidebar_exposes_alerts_for_selected_operator()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);

        var cut = context.Render<NavMenu>();

        var alertsLink = cut
            .FindAll("a")
            .Single(link => link.TextContent.Trim().Equals("Alerts", StringComparison.Ordinal));
        alertsLink.GetAttribute("href").ShouldBe("alerts");
    }

    [Test]
    public async Task Alerts_page_acknowledges_active_alert_and_moves_it_to_history()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        await alerts.CreateAsync(
            hostId,
            DurableAlertSeverity.Warning,
            "test",
            "acknowledge",
            "Queue delayed",
            "Outbound messages are delayed.",
            null,
            CancellationToken.None
        );

        var cut = context.Render<AlertsPage>();
        cut.Find("button[aria-label='Acknowledge Queue delayed']").Click();

        var state = await alerts.LoadStateAsync(hostId, CancellationToken.None);
        state.Active.ShouldBeEmpty();
        state.History.Single().AcknowledgedByLogin.ShouldBe("streamer");
        cut.FindAll("button[aria-label='Acknowledge Queue delayed']").ShouldBeEmpty();
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

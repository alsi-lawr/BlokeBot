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

    [Test]
    public async Task AlertsPage_LoadFailure_RetryLoadsTheRouteInline()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var failFirstFactory = new FailFirstDbContextFactory(dbFactory);
        context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(failFirstFactory);

        var cut = context.Render<AlertsPage>();

        cut.Find("[role='alert']").TextContent.ShouldContain("couldn’t load alerts");
        cut.FindAll("h2").Select(heading => heading.TextContent.Trim()).ShouldNotContain("Active");

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry").Click();

        failFirstFactory.AttemptCount.ShouldBe(2);
        cut.FindAll("[role='alert']").ShouldBeEmpty();
        cut.FindAll("h2")
            .Select(heading => heading.TextContent.Trim())
            .ShouldContain("Active alerts");
    }

    [Test]
    public async Task AlertsPage_NoActiveAlerts_RendersOneNamedFrameAndSemanticHistoryCards()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        var alert = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "test",
                "history-card",
                "Queue delayed",
                "Outbound messages are delayed.",
                null
            )
            .RunAsync(CancellationToken.None);
        await alerts
            .Acknowledge(hostId, alert.Alert.Id, "streamer")
            .RunAsync(CancellationToken.None);

        var cut = context.Render<AlertsPage>();

        var active = cut.Find("section[aria-labelledby='active-alerts-title']");
        active.ClassList.ShouldContain("card");
        active
            .QuerySelectorAll("h2")
            .ShouldHaveSingleItem()
            .TextContent.Trim()
            .ShouldBe("Active alerts");
        active.TextContent.ShouldContain("No active alerts.");

        var history = cut.Find(".responsive-data-cards");
        history
            .QuerySelectorAll("td")
            .Select(cell => cell.GetAttribute("data-label"))
            .ShouldBe(["Alert", "Importance", "Handled by", "Handled at"]);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FailFirstDbContextFactory(
        IDbContextFactory<BlokeBotDbContext> innerFactory
    ) : IDbContextFactory<BlokeBotDbContext>
    {
        public int AttemptCount { get; private set; }

        public BlokeBotDbContext CreateDbContext()
        {
            AttemptCount++;
            return AttemptCount == 1
                ? throw new InvalidOperationException("Simulated alert load failure.")
                : innerFactory.CreateDbContext();
        }

        public ValueTask<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            AttemptCount++;
            return AttemptCount == 1
                ? ValueTask.FromException<BlokeBotDbContext>(
                    new InvalidOperationException("Simulated alert load failure.")
                )
                : new ValueTask<BlokeBotDbContext>(
                    innerFactory.CreateDbContextAsync(cancellationToken)
                );
        }
    }
}

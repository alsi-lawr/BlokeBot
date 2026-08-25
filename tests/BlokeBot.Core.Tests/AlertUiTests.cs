using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AlertUiTests
{
    [Test]
    public async Task AlertRecurrence_Committing_RefreshesAlertsPageAndKeepsTopBarAtOneIssue()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        var page = context.Render<AlertsPage>();
        _ = context.ComponentFactories.AddStub<SelectedChannelBotStatus>();
        _ = context.ComponentFactories.AddStub<HostSelector>();
        _ = context.ComponentFactories.AddStub<AccountMenu>();
        var authentication = context
            .Services.GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();
        RenderFragment topBarContent = builder =>
        {
            builder.OpenComponent<TopBarControls>(0);
            builder.CloseComponent();
        };
        var topBar = context.Render<CascadingValue<Task<AuthenticationState>>>(parameters =>
            parameters
                .Add(value => value.Value, authentication)
                .Add(value => value.IsFixed, true)
                .Add(value => value.ChildContent, topBarContent)
        );

        _ = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Info,
                "test",
                "recurrence",
                "Initial title",
                "Initial detail",
                null
            )
            .RunAsync(CancellationToken.None);
        page.WaitForAssertion(() => page.Markup.ShouldContain("Initial title"));
        topBar.Find(".topbar-alert-button").GetAttribute("aria-label").ShouldBe("1 active alert");

        _ = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Critical,
                "test",
                "recurrence",
                "Latest title",
                "Latest detail",
                "/latest"
            )
            .RunAsync(CancellationToken.None);

        page.WaitForAssertion(() =>
        {
            page.Markup.ShouldContain("Latest title");
            page.Markup.ShouldContain("Latest detail");
            page.Find("[data-alert-occurrence-count]")
                .GetAttribute("data-alert-occurrence-count")
                .ShouldBe("2");
        });
        topBar.Find(".topbar-alert-button").GetAttribute("aria-label").ShouldBe("1 active alert");
    }

    [Test]
    public async Task ActiveAlertOnAlertsPage_Acknowledging_MovesAlertToHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var alerts = context.Services.GetRequiredService<DurableAlertService>();
        _ = await alerts
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
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(failFirstFactory);

        var cut = context.Render<AlertsPage>();

        _ = cut.Find("[role='alert']");

        cut.FindAll("button").Single(static button => button.TextContent.Trim() == "Retry").Click();

        failFirstFactory.AttemptCount.ShouldBe(2);
        cut.FindAll("[role='alert']").ShouldBeEmpty();
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
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

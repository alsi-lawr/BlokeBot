using BlokeBot.Core.Features.BlokeRaid;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BlokeRaidUiTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Configuration_SavingRepresentativeValues_PersistsAndSchedulesWeeklyReset()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderConfiguration(context);

        page.Find("#raid-boss-name").Input("The Static Hydra");
        page.Find("#raid-attack-cooldown").Change("25");
        page.Find("#raid-reset-weekly").Change(true);
        page.WaitForAssertion(() => _ = page.Find("#raid-reset-day"));
        page.Find("#raid-reset-day").Change("Friday");
        page.Find("#raid-reset-hour").Change("18");
        Save(page, "success");

        await using var verify = await database.CreateDbContextAsync();
        var configuration = await verify.BlokeRaidConfigurations.SingleAsync();
        configuration.BossName.ShouldBe("The Static Hydra");
        configuration.AttackCooldownSeconds.ShouldBe(25);
        configuration.ResetPolicy.ShouldBe(BlokeRaidResetPolicy.Weekly);
        configuration.WeeklyResetDay.ShouldBe((int)DayOfWeek.Friday);
        configuration.WeeklyResetHourUtc.ShouldBe(18);
        _ = configuration.NextWeeklyResetAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task BarePath_NormalizesToCampaignAndSelectingConfigurationPushesOneHistoryEntry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database, campaign: true);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/raid");

        var page = context.Render<BlokeRaidPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/raid#campaign");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
            page.Find("#raid-workspace-campaign-tab").GetAttribute("href").ShouldBe("#campaign");
            page.Find("#raid-workspace-campaign-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            page.Find("#raid-workspace-configuration-tab")
                .GetAttribute("href")
                .ShouldBe("#configuration");
            page.Find("#raid-workspace-campaign-panel").GetAttribute("role").ShouldBe("tabpanel");
            _ = page.Find("[data-raid-campaign]");
            page.FindAll("[data-raid-configuration]").ShouldBeEmpty();
        });

        page.Find("#raid-workspace-configuration-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/raid#configuration");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find("#raid-workspace-configuration-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            page.Find("#raid-workspace-configuration-panel")
                .GetAttribute("aria-labelledby")
                .ShouldBe("raid-workspace-configuration-tab");
            _ = page.Find("[data-raid-configuration]");
            page.FindAll("[data-raid-campaign]").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task DirectConfigurationFragment_OpensConfigurationAndHistoryMovesBetweenWorkspaces()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database, campaign: true);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/raid#configuration");

        var page = context.Render<BlokeRaidPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#raid-workspace-configuration-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            _ = page.Find("#raid-boss-name");
        });

        navigation.NavigateTo("/raid#campaign");

        page.WaitForAssertion(() =>
        {
            page.Find("#raid-workspace-campaign-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            _ = page.Find("[data-raid-campaign]");
            page.FindAll("#raid-boss-name").ShouldBeEmpty();
        });

        navigation.NavigateTo("/raid#configuration");

        page.WaitForAssertion(() =>
        {
            page.Find("#raid-workspace-configuration-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            _ = page.Find("#raid-boss-name");
        });
    }

    private static IRenderedComponent<BlokeRaidPage> RenderConfiguration(BunitContext context)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/raid#configuration");
        var page = context.Render<BlokeRaidPage>();
        page.WaitForAssertion(() => _ = page.Find("#raid-boss-name"));
        return page;
    }

    private static void Save(IRenderedComponent<BlokeRaidPage> page, string outcome)
    {
        page.FindAll("button")
            .Single(button => button.TextContent.StartsWith("Save", StringComparison.Ordinal))
            .Click();
        page.WaitForAssertion(() => _ = page.Find($"[data-save-feedback='{outcome}']"));
    }

    private static async Task<BunitContext> CreateContextAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features = HostFeatureFlags.CooperativeGame,
        bool campaign = false
    )
    {
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "streamer-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = features,
                CreatedAtUtc = _now.UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }

        var context = UiTestContextFactory.Create(database, hostId);
        var service = new BlokeRaidService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new FixedRandom(),
            new FixedTimeProvider(_now)
        );
        _ = context.Services.AddSingleton(service);
        if (campaign)
        {
            _ = await service.StartAsync(
                hostId,
                new("ui-test:start", new("streamer-id", "streamer"), "test seed"),
                CancellationToken.None
            );
        }

        return context;
    }

    private sealed class FixedRandom : IBlokeRaidRandom
    {
        public int NextInclusive(int minimum, int maximum) => minimum;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

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

    [Test]
    [Arguments("#campaign")]
    [Arguments("#configuration")]
    public async Task DisabledFeature_ShowsRecoveryWithoutWorkspaceTabsOrPanels(string fragment)
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database, HostFeatureFlags.None);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/raid" + fragment);

        var page = context.Render<BlokeRaidPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-raid-disabled]")
                .TextContent.ShouldContain("Cooperative game is off for this channel.");
            page.Find("[data-raid-disabled]")
                .TextContent.ShouldContain(
                    "Saved configuration, campaigns, contributions, and history are retained"
                );
            page.FindAll("[role='tablist']").ShouldBeEmpty();
            page.FindAll("[role='tabpanel']").ShouldBeEmpty();
            page.FindAll("[data-raid-configuration]").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task Configuration_SavesEveryGroupedSectionThroughTheService()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderConfiguration(context);

        page.Find("#raid-boss-name").Input("The Static Hydra");
        page.Find("#raid-duration").Change("72");
        page.Find("#raid-health").Change("30000");
        page.Find("#raid-ward").Change("1500");
        page.Find("#raid-victory-reward").Input("400");
        page.Find("#raid-attack-minimum").Change("3");
        page.Find("#raid-attack-maximum").Change("9");
        page.Find("#raid-attack-cooldown").Change("25");
        page.Find("#raid-attack-limit").Change("30");
        page.Find("#raid-mend-minimum").Change("4");
        page.Find("#raid-mend-maximum").Change("8");
        page.Find("#raid-mend-cooldown").Change("35");
        page.Find("#raid-mend-limit").Change("15");
        page.Find("#raid-special-minimum").Change("10");
        page.Find("#raid-special-maximum").Change("16");
        page.Find("#raid-special-cooldown").Change("120");
        page.Find("#raid-special-limit").Change("4");
        page.Find("#raid-special-cost").Change("90");
        page.Find("#raid-guess-damage").Change("6");
        page.Find("#raid-phase-two").Change("60");
        page.Find("#raid-phase-three").Change("25");
        page.Find("#raid-phase-one-response").Input("The Hydra surfaces.");
        page.Find("#raid-phase-two-response").Input("Its scales split.");
        page.Find("#raid-phase-three-response").Input("One head remains.");
        page.Find("#raid-victory-response").Input("The Hydra falls.");
        page.Find("#raid-expiry-response").Input("The Hydra slipped away.");
        page.Find("#raid-reset-weekly").Change(true);
        page.WaitForAssertion(() => _ = page.Find("#raid-reset-day"));
        page.Find("#raid-reset-day").Change("Friday");
        page.Find("#raid-reset-hour").Change("18");
        Save(page, "success");

        await using var verify = await database.CreateDbContextAsync();
        var configuration = await verify.BlokeRaidConfigurations.SingleAsync();
        configuration.BossName.ShouldBe("The Static Hydra");
        configuration.CampaignDurationHours.ShouldBe(72);
        configuration.MaximumHealth.ShouldBe(30_000);
        configuration.MaximumWard.ShouldBe(1_500);
        configuration.VictoryPointReward.ShouldBe("400");
        configuration.AttackMinimum.ShouldBe(3);
        configuration.AttackMaximum.ShouldBe(9);
        configuration.AttackCooldownSeconds.ShouldBe(25);
        configuration.AttackPerStreamLimit.ShouldBe(30);
        configuration.MendMinimum.ShouldBe(4);
        configuration.MendMaximum.ShouldBe(8);
        configuration.MendCooldownSeconds.ShouldBe(35);
        configuration.MendPerStreamLimit.ShouldBe(15);
        configuration.SpecialMinimum.ShouldBe(10);
        configuration.SpecialMaximum.ShouldBe(16);
        configuration.SpecialCooldownSeconds.ShouldBe(120);
        configuration.SpecialPerStreamLimit.ShouldBe(4);
        configuration.SpecialPointCost.ShouldBe("90");
        configuration.CorrectGuessDamage.ShouldBe(6);
        configuration.PhaseTwoHealthPercent.ShouldBe(60);
        configuration.PhaseThreeHealthPercent.ShouldBe(25);
        configuration.PhaseOneResponse.ShouldBe("The Hydra surfaces.");
        configuration.PhaseTwoResponse.ShouldBe("Its scales split.");
        configuration.PhaseThreeResponse.ShouldBe("One head remains.");
        configuration.VictoryResponse.ShouldBe("The Hydra falls.");
        configuration.ExpiryResponse.ShouldBe("The Hydra slipped away.");
        configuration.ResetPolicy.ShouldBe(BlokeRaidResetPolicy.Weekly);
        configuration.WeeklyResetDay.ShouldBe((int)DayOfWeek.Friday);
        configuration.WeeklyResetHourUtc.ShouldBe(18);
        _ = configuration.NextWeeklyResetAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task InvalidPhaseThresholds_ReportValidationAndPersistNothing()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderConfiguration(context);

        page.Find("#raid-phase-two").Change("20");
        page.Find("#raid-phase-three").Change("40");
        Save(page, "validation");

        page.Find("[data-save-feedback='validation']")
            .TextContent.ShouldContain("Phase three must be below phase two");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.BlokeRaidConfigurations.SingleOrDefaultAsync()).ShouldBeNull();
    }

    [Test]
    public async Task InvalidVictoryReward_ReportsValidationBeforeReachingTheService()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderConfiguration(context);

        page.Find("#raid-victory-reward").Input("many");
        Save(page, "validation");

        page.Find("[data-save-feedback='validation']")
            .TextContent.ShouldContain("Victory reward must be a non-negative whole number.");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.BlokeRaidConfigurations.SingleOrDefaultAsync()).ShouldBeNull();
    }

    [Test]
    public async Task Configuration_KeepsOneStickySaveInsideItsOwnBoundaryAndLabelsEveryField()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderConfiguration(context);

        var region = page.Find("[data-save-scope]");
        page.FindAll("[data-save-scope]").Count.ShouldBe(1);
        region.GetAttribute("data-save-scope").ShouldBe("editor");
        var boundary = region.Closest("[data-sticky-save-scope]").ShouldNotBeNull();
        boundary.HasAttribute("data-raid-configuration").ShouldBeTrue();
        page.FindAll("button")
            .Count(button => button.TextContent.StartsWith("Save", StringComparison.Ordinal))
            .ShouldBe(1);
        page.FindAll("[data-raid-configuration] [inert]").ShouldBeEmpty();
        region.Closest("details").ShouldBeNull();
        region.Closest("[hidden]").ShouldBeNull();
        foreach (
            var id in new[]
            {
                "raid-boss-name",
                "raid-duration",
                "raid-health",
                "raid-ward",
                "raid-victory-reward",
                "raid-attack-minimum",
                "raid-attack-maximum",
                "raid-attack-cooldown",
                "raid-attack-limit",
                "raid-mend-minimum",
                "raid-mend-maximum",
                "raid-mend-cooldown",
                "raid-mend-limit",
                "raid-special-minimum",
                "raid-special-maximum",
                "raid-special-cooldown",
                "raid-special-limit",
                "raid-special-cost",
                "raid-guess-damage",
                "raid-phase-two",
                "raid-phase-three",
                "raid-phase-one-response",
                "raid-phase-two-response",
                "raid-phase-three-response",
                "raid-victory-response",
                "raid-expiry-response",
            }
        )
        {
            _ = page.FindAll($"label[for='{id}']").ShouldHaveSingleItem();
        }
        page.FindAll("input[name='raid-reset-policy']").Count.ShouldBe(2);
        page.Find("#raid-reset-manual")
            .Closest(".raid-option")!
            .ClassList.ShouldContain("raid-option--selected");
        page.FindAll("#raid-reset-day").ShouldBeEmpty();
    }

    [Test]
    public async Task CampaignWorkspace_KeepsLifecycleControlsOutOfEverySaveRegion()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database, campaign: true);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/raid#campaign");

        var page = context.Render<BlokeRaidPage>();

        page.WaitForAssertion(() => _ = page.Find("[data-raid-campaign]"));
        page.FindAll("[data-save-scope]").ShouldBeEmpty();
        foreach (var label in new[] { "End campaign", "Reset now" })
        {
            var control = page.FindAll("button").Single(button => button.TextContent == label);
            control.Closest("[data-save-scope]").ShouldBeNull();
        }
        page.FindAll("button")
            .Count(button => button.TextContent.StartsWith("Save", StringComparison.Ordinal))
            .ShouldBe(0);
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

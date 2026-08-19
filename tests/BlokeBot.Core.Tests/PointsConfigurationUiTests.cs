using System.Globalization;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsConfigurationUiTests
{
    [Test]
    public async Task InvalidPersistedBoundaries_SaveIsRejectedUntilEveryValueIsValid()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedInvalidSettingsAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.Services.AddSingleton<PointsChangeNotifier>();
        _ = context.Services.AddSingleton<PointsConfigurationService>();
        _ = context.ComponentFactories.AddStub<PointsEligibilitySelector>();
        var toasts = context.Services.GetRequiredService<ToastService>();
        var page = context.Render<PointsConfigurationPage>();

        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save")
            .Click();

        page.WaitForAssertion(() => toasts.Current.ShouldHaveSingleItem());

        var error = toasts.Current.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ToastKind.Error);
        await using (var invalidDb = await dbFactory.CreateDbContextAsync())
        {
            var persisted = await invalidDb.PointsSettings.SingleAsync();
            persisted.GamblingCooldownSeconds.ShouldBe(-4);
            persisted.GiveawayDurationSeconds.ShouldBe(0);
            persisted.GiveawayWinnerCount.ShouldBe(0);
            persisted.GiveawayCooldownSeconds.ShouldBe(299);
        }

        page.Find("#gamblingCooldown").Input("0");
        page.Find("#duration").Input("1");
        page.Find("#winnerCount").Input("1");
        page.Find("#cooldown")
            .Input(
                PointsConfigurationValidator.MinimumGiveawayCooldownSeconds.ToString(
                    CultureInfo.InvariantCulture
                )
            );
        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save")
            .Click();

        await using var correctedDb = await dbFactory.CreateDbContextAsync();
        var corrected = await correctedDb.PointsSettings.SingleAsync();
        corrected.GamblingCooldownSeconds.ShouldBe(0);
        corrected.GiveawayDurationSeconds.ShouldBe(1);
        corrected.GiveawayWinnerCount.ShouldBe(1);
        corrected.GiveawayCooldownSeconds.ShouldBe(
            PointsConfigurationValidator.MinimumGiveawayCooldownSeconds
        );
    }

    private static async Task<int> SeedInvalidSettingsAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Points,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointsSettings.Add(
            new PointsSettings
            {
                HostId = host.Id,
                GamblingCooldownSeconds = -4,
                GiveawayDurationSeconds = 0,
                GiveawayWinnerCount = 0,
                GiveawayCooldownSeconds = 299,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}

using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.WatchTime;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class WatchTimeConfigurationUiTests
{
    [Test]
    public async Task ActualPage_EnableRequiresWholeAmountSaveStepOneAndStatusRetainsDraft()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var watch = new WatchTimeTestSupport(database);
        var hostId = await watch.SeedAsync(false, null);
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(watch.Runtime);
        _ = context.Services.AddSingleton<IWatchTimeSettingsCommitObserver>(watch.Runtime);
        _ = context.Services.AddSingleton<PointsChangeNotifier>();
        _ = context.Services.AddSingleton<PointsConfigurationService>();
        _ = context.ComponentFactories.AddStub<PointsEligibilitySelector>();
        var page = context.Render<PointsConfigurationPage>();
        page.Find("#watch-amount").GetAttribute("value").ShouldBe(string.Empty);
        page.Find("#watch-enabled").Click();
        foreach (var invalid in new[] { "", "1.5", "0", "-1" })
        {
            page.Find("#watch-amount").Input(invalid);
            page.Find("[data-save-state]").Click();
            page.WaitForAssertion(() =>
                page.Find("#watch-amount").GetAttribute("aria-invalid").ShouldBe("true")
            );
            await using var verify = database.CreateDbContext();
            (await verify.PointsSettings.SingleAsync()).WatchTimePointsEnabled.ShouldBeFalse();
        }
        page.Find("#watch-amount").Input("123456789012345678901234567890");
        page.Find("button[aria-label='More points per person']").Click();
        page.Find("#watch-amount").GetAttribute("value").ShouldBe("123456789012345678901234567891");
        page.Find("[data-save-state]").Click();
        await using (var verify = database.CreateDbContext())
        {
            var settings = await verify.PointsSettings.SingleAsync();
            settings.WatchTimePointsEnabled.ShouldBeTrue();
            settings.WatchTimePointAmount.ShouldBe("123456789012345678901234567891");
        }
        page.Find("#points-label").Input("Unsaved label");
        page.Find("#watch-amount").Input("45");
        _ = await watch.SaveAsync(hostId, true, "99");
        await watch.Runtime.ReconcileAsync(default);
        page.Find("#points-label").GetAttribute("value").ShouldBe("Unsaved label");
        page.Find("#watch-amount").GetAttribute("value").ShouldBe("45");
        page.Find("#watch-saved-region").TextContent.ShouldContain("99");
    }
}

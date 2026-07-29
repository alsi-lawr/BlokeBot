using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PointsConfigurationUiTests
{
    [Test]
    public async Task InvalidPersistedBoundaries_Saving_ShowsEveryFieldErrorWithoutRewrite()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedInvalidSettingsAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        context.Services.AddSingleton<PointsChangeNotifier>();
        context.Services.AddSingleton<PointsConfigurationService>();
        context.ComponentFactories.AddStub<PointsEligibilitySelector>();
        var toasts = context.Services.GetRequiredService<ToastService>();
        var page = context.Render<PointsConfigurationPage>();

        page.FindAll(".settings-disclosure-stack").Count.ShouldBe(1);
        page.Find("#gamblingCooldown").GetAttribute("value").ShouldBe("-4");
        page.Find("#duration").GetAttribute("value").ShouldBe("0");
        page.Find("#winnerCount").GetAttribute("value").ShouldBe("0");
        page.Find("#cooldown").GetAttribute("value").ShouldBe("299");

        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save changes")
            .Click();

        page.WaitForAssertion(() =>
            page.Find("#gamblingCooldown").GetAttribute("aria-invalid").ShouldBe("true")
        );
        context.JSInterop.Invocations.ShouldContain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments.OfType<string>().SingleOrDefault() == "gamblingCooldown"
        );

        var error = toasts.Current.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ToastKind.Error);
        error.Message.ShouldContain("The wait between gambles cannot be negative.");
        error.Message.ShouldContain("Giveaway entry time must be at least 1 second.");
        error.Message.ShouldContain("A giveaway needs at least 1 winner.");
        error.Message.ShouldContain(
            $"The wait between giveaways must be at least {PointsConfigurationValidator.MinimumGiveawayCooldownSeconds} seconds."
        );
        AssertFieldError(
            page,
            "#gamblingCooldown",
            "gamblingCooldown-error",
            "The wait between gambles cannot be negative."
        );
        AssertFieldError(
            page,
            "#duration",
            "duration-error",
            "Giveaway entry time must be at least 1 second."
        );
        AssertFieldError(
            page,
            "#winnerCount",
            "winnerCount-error",
            "A giveaway needs at least 1 winner."
        );
        AssertFieldError(
            page,
            "#cooldown",
            "cooldown-error",
            $"The wait between giveaways must be at least {PointsConfigurationValidator.MinimumGiveawayCooldownSeconds} seconds."
        );
        await using (var invalidDb = await dbFactory.CreateDbContextAsync())
        {
            var persisted = await invalidDb.PointsSettings.SingleAsync();
            persisted.GamblingCooldownSeconds.ShouldBe(-4);
            persisted.GiveawayDurationSeconds.ShouldBe(0);
            persisted.GiveawayWinnerCount.ShouldBe(0);
            persisted.GiveawayCooldownSeconds.ShouldBe(299);
        }

        page.Find("#gamblingCooldown").Change("0");
        page.Find("#duration").Change("1");
        page.Find("#winnerCount").Change("1");
        page.Find("#cooldown")
            .Change(PointsConfigurationValidator.MinimumGiveawayCooldownSeconds.ToString());
        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save changes")
            .Click();

        AssertFieldErrorCleared(page, "#gamblingCooldown", "gamblingCooldown-error");
        AssertFieldErrorCleared(page, "#duration", "duration-error");
        AssertFieldErrorCleared(page, "#winnerCount", "winnerCount-error");
        AssertFieldErrorCleared(page, "#cooldown", "cooldown-error");
        await using var correctedDb = await dbFactory.CreateDbContextAsync();
        var corrected = await correctedDb.PointsSettings.SingleAsync();
        corrected.GamblingCooldownSeconds.ShouldBe(0);
        corrected.GiveawayDurationSeconds.ShouldBe(1);
        corrected.GiveawayWinnerCount.ShouldBe(1);
        corrected.GiveawayCooldownSeconds.ShouldBe(
            PointsConfigurationValidator.MinimumGiveawayCooldownSeconds
        );
    }

    private static void AssertFieldError(
        IRenderedComponent<PointsConfigurationPage> page,
        string inputSelector,
        string errorId,
        string message
    )
    {
        var input = page.Find(inputSelector);
        input.GetAttribute("aria-invalid").ShouldBe("true");
        input.GetAttribute("aria-describedby").ShouldBe(errorId);
        page.Find($"#{errorId}").TextContent.Trim().ShouldBe(message);
    }

    private static void AssertFieldErrorCleared(
        IRenderedComponent<PointsConfigurationPage> page,
        string inputSelector,
        string errorId
    )
    {
        var input = page.Find(inputSelector);
        input.GetAttribute("aria-invalid").ShouldBe("false");
        input.GetAttribute("aria-describedby").ShouldBeNull();
        page.FindAll($"#{errorId}").ShouldBeEmpty();
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
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        db.PointsSettings.Add(
            new PointsSettings
            {
                HostId = host.Id,
                GamblingCooldownSeconds = -4,
                GiveawayDurationSeconds = 0,
                GiveawayWinnerCount = 0,
                GiveawayCooldownSeconds = 299,
            }
        );
        await db.SaveChangesAsync();
        return host.Id;
    }
}

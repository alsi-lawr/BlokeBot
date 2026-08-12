using AngleSharp.Dom;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPassportUiTests
{
    [Test]
    public async Task SignedInDirectRoute_WhenDisabled_ShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(
            database,
            HostFeatureFlags.None,
            ViewerPassportVisibility.Public,
            "RETAINED-PROFILE-LINE"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Viewer passports is off for this channel");
            cut.Markup.ShouldContain("/host#chat-tools");
            cut.Markup.ShouldContain("retained");
            cut.Markup.ShouldNotContain("RETAINED-PROFILE-LINE");
        });
    }

    [Test]
    public async Task PublicRoute_PrivatePassport_ExposesNoProfileFields()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.Private,
            "PRIVATE-PROFILE-LINE"
        );
        using var context = new BunitContext();
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        _ = context.Services.AddSingleton<ViewerPassportService>();
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.AddAuthorization().SetNotAuthorized();

        var cut = context.Render<PublicViewerPassportPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer").Add(page => page.Viewer, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("This viewer passport is not available");
            cut.Markup.ShouldNotContain("PRIVATE-PROFILE-LINE");
        });
    }

    [Test]
    public async Task PublicRoute_ProfileLine_IsRenderedAsText()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.Public,
            "<script>alert('unsafe')</script>"
        );
        using var context = new BunitContext();
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        _ = context.Services.AddSingleton<ViewerPassportService>();
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.AddAuthorization().SetNotAuthorized();

        var cut = context.Render<PublicViewerPassportPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer").Add(page => page.Viewer, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("script").ShouldBeEmpty();
            cut.Markup.ShouldContain("&lt;script&gt;alert('unsafe')&lt;/script&gt;");
        });
    }

    [Test]
    public async Task VisibilityChoices_AreNativeRadiosStatingTheRetainedAuthorizationPolicy()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.ChannelMembers,
            "PROFILE-LINE"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() => cut.FindAll(".passport-visibility-option").Count.ShouldBe(3));
        cut.Find("fieldset.passport-visibility legend")
            .TextContent.Trim()
            .ShouldBe("Who can open this passport");
        var choices = cut.FindAll(".passport-visibility-option");
        choices
            .Select(choice => choice.QuerySelector("input")!.GetAttribute("name"))
            .ShouldAllBe(name => name == "passport-visibility");
        choices
            .Select(choice => choice.QuerySelector("input")!.GetAttribute("type"))
            .ShouldAllBe(type => type == "radio");
        Choice(cut, ViewerPassportVisibility.Public)
            .ClassList.ShouldContain("passport-visibility-option--public");
        Choice(cut, ViewerPassportVisibility.Public)
            .TextContent.ShouldContain(
                "Anyone with the link can see the profile fields you allow, even without signing in."
            );
        Choice(cut, ViewerPassportVisibility.ChannelMembers)
            .ClassList.ShouldContain("passport-visibility-option--members");
        Choice(cut, ViewerPassportVisibility.ChannelMembers)
            .TextContent.ShouldContain(
                "Signed-in viewers who have a passport in this channel, and channel managers."
            );
        Choice(cut, ViewerPassportVisibility.Private)
            .ClassList.ShouldContain("passport-visibility-option--private");
        Choice(cut, ViewerPassportVisibility.Private)
            .TextContent.ShouldContain("Only you and channel managers can open this passport.");
        cut.Find(".passport-attendance")
            .TextContent.ShouldContain("Counts consecutive days you chatted.");
    }

    [Test]
    public async Task VisibilityIcons_AreDecorativeAndHiddenFromAssistiveTechnology()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.Public,
            "PROFILE-LINE"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() =>
            cut.FindAll(".passport-visibility-option__icon").Count.ShouldBe(3)
        );
        foreach (var icon in cut.FindAll(".passport-visibility-option__icon"))
        {
            icon.GetAttribute("aria-hidden").ShouldBe("true");
            var glyph = icon.QuerySelector("svg").ShouldNotBeNull();
            glyph.GetAttribute("focusable").ShouldBe("false");
            glyph.QuerySelector("title").ShouldBeNull();
            glyph.GetAttribute("aria-label").ShouldBeNull();
        }
        cut.FindAll(".passport-visibility-option__icon svg")
            .Select(glyph => glyph.InnerHtml)
            .Distinct()
            .Count()
            .ShouldBe(3);
    }

    [Test]
    public async Task ChoosingVisibility_MovesTheSelectedStateAndPersistsTheChosenValue()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.ChannelMembers,
            "PROFILE-LINE"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() =>
            Choice(cut, ViewerPassportVisibility.ChannelMembers)
                .ClassList.ShouldContain("passport-visibility-option--selected")
        );
        Radio(cut, ViewerPassportVisibility.Private).Change(true);

        cut.WaitForAssertion(() =>
            Choice(cut, ViewerPassportVisibility.Private)
                .ClassList.ShouldContain("passport-visibility-option--selected")
        );
        cut.FindAll(".passport-visibility-option--selected").Count.ShouldBe(1);
        var save = cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save passport");
        _ = save.Closest("[data-save-scope]").ShouldNotBeNull();
        cut.FindAll("[data-save-scope]").Count.ShouldBe(1);
        save.Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Viewer passport saved."));
        await using var db = await database.CreateDbContextAsync();
        var saved = await db.ViewerPassports.SingleAsync();
        saved.Visibility.ShouldBe(ViewerPassportVisibility.Private);
        saved.ProfileLine.ShouldBe("PROFILE-LINE");
    }

    private static IElement Choice(
        IRenderedComponent<ViewerPassportsPage> cut,
        ViewerPassportVisibility visibility
    ) => Radio(cut, visibility).Closest(".passport-visibility-option")!;

    private static IElement Radio(
        IRenderedComponent<ViewerPassportsPage> cut,
        ViewerPassportVisibility visibility
    ) => cut.Find($"input[name='passport-visibility'][value='{visibility}']");

    private static async Task<int> SeedAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features,
        ViewerPassportVisibility visibility,
        string profileLine
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = features,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var passport = new ViewerPassport
        {
            HostId = host.Id,
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            ProfileLine = profileLine,
            Visibility = visibility,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _ = db.ViewerPassports.Add(passport);
        _ = await db.SaveChangesAsync();
        _ = db.ViewerPassportLogins.Add(
            new()
            {
                HostId = host.Id,
                PassportId = passport.Id,
                Login = passport.Login,
                FirstSeenAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}

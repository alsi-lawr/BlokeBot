using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class MainLayoutInteractionTests
{
    [Test]
    public void MobileNavigation_OpeningAndClosing_UpdatesInteractionStateAndRestoresMenuFocus()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<NavMenu>();
        context.ComponentFactories.AddStub<TopBarControls>();
        context.ComponentFactories.AddStub<ThemeToggle>();
        context.ComponentFactories.AddStub<PageHelpButton>();
        context.ComponentFactories.AddStub<ToastHost>();
        var cut = context.Render<MainLayout>();
        var menuButton = cut.Find("#mobile-navigation-menu-button");
        var background = cut.Find("[data-mobile-navigation-background]");

        background.HasAttribute("inert").ShouldBeFalse();

        menuButton.Click();

        background.HasAttribute("inert").ShouldBeTrue();
        menuButton.GetAttribute("aria-expanded").ShouldBe("true");
        var activate = context.JSInterop.Invocations.Single(invocation =>
            invocation.Identifier == "blokeBotNavigation.activateMobileDrawer"
        );
        activate.Arguments[0].ShouldBe("mobile-navigation-drawer");

        cut.Find("button[aria-label='Close navigation menu']").Click();

        background.HasAttribute("inert").ShouldBeFalse();
        menuButton.GetAttribute("aria-expanded").ShouldBe("false");
        var restoreFocus = context.JSInterop.Invocations.Single(invocation =>
            invocation.Identifier == "blokeBotNavigation.focus"
        );
        restoreFocus.Arguments[0].ShouldBe("mobile-navigation-menu-button");

        menuButton.Click();
        cut.Find("[data-mobile-navigation-overlay]").Click();

        background.HasAttribute("inert").ShouldBeFalse();
        menuButton.GetAttribute("aria-expanded").ShouldBe("false");
    }
}

public sealed class NavMenuInteractionTests
{
    [Test]
    public async Task ChatTools_ResetCollapsedAndRemembersExplicitChoice()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var module = context.JSInterop.SetupModule("./Components/Layout/NavMenu.razor.js");
        module.Setup<bool>("readBoolean").SetResult(false);

        var cut = context.Render<NavMenu>();
        var sections = cut.FindAll("[data-nav-section]");

        cut.FindAll("div")
            .Any(element => element.TextContent.Trim() == "Chat tools")
            .ShouldBeTrue();
        sections
            .Select(section => section.GetAttribute("data-nav-section"))
            .ShouldBe(["guessing", "points", "custom-commands", "native-twitch"]);
        sections.All(section => !section.HasAttribute("open")).ShouldBeTrue();

        cut.Find("[data-nav-section='guessing'] summary").Click();

        cut.Find("[data-nav-section='guessing']").HasAttribute("open").ShouldBeTrue();
        var persistedChoice = module.Invocations.Single(invocation =>
            invocation.Identifier == "writeBoolean"
        );
        persistedChoice.Arguments[0].ShouldBe("blokebot.sidebar.guessing.open");
        persistedChoice.Arguments[1].ShouldBe(true);
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

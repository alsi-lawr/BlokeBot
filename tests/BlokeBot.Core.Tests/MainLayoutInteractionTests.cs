using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
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

public sealed class CompactShellContractTests
{
    [Test]
    public void DesktopRail_HasAnAccessibleCollapsedStateControl()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<NavMenu>();
        context.ComponentFactories.AddStub<TopBarControls>();
        context.ComponentFactories.AddStub<ThemeToggle>();
        context.ComponentFactories.AddStub<PageHelpButton>();
        context.ComponentFactories.AddStub<ToastHost>();

        var cut = context.Render<MainLayout>();
        var toggle = cut.Find("button[aria-controls='desktop-navigation-rail']");

        toggle.GetAttribute("aria-expanded").ShouldBe("true");
        toggle.Click();

        cut.Find(".app-shell").GetAttribute("data-rail-collapsed").ShouldBe("true");
        toggle.GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Test]
    public void SharedPageHeader_UsesScrollingIdentityAndStickyActionRole()
    {
        using var context = new BunitContext();
        RenderFragment actions = builder => builder.AddMarkupContent(0, "<button>Save</button>");
        RenderFragment content = builder => builder.AddMarkupContent(0, "<p>Ready</p>");
        var cut = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(parameter => parameter.Kicker, "Chat tools")
                .Add(parameter => parameter.Title, "Points")
                .Add(parameter => parameter.Description, "Manage points")
                .Add(parameter => parameter.Actions, actions)
                .Add(parameter => parameter.ChildContent, content)
        );

        cut.Find(".page-header").ClassList.ShouldContain("page-header");
        cut.Find("[data-sticky-save-actions]").TextContent.ShouldContain("Save");
    }
}

public sealed class SharedDisclosureAndFailureTests
{
    [Test]
    public void Disclosure_RemovesCollapsedContentFromTheRenderedTraversal()
    {
        using var context = new BunitContext();
        RenderFragment content = builder =>
            builder.AddMarkupContent(0, "<input aria-label='Configuration value' />");
        var cut = context.Render<CollapsibleSection>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Configuration")
                .Add(parameter => parameter.InitiallyOpen, false)
                .Add(parameter => parameter.ChildContent, content)
        );

        cut.Find("button").GetAttribute("aria-expanded").ShouldBe("false");
        cut.FindAll("input").ShouldBeEmpty();

        cut.Find("button").Click();

        cut.Find("button").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find("input[aria-label='Configuration value']").ShouldNotBeNull();
    }

    [Test]
    public void DashboardPage_LoadFailureRendersAnInlineRetry()
    {
        using var context = new BunitContext();
        var retried = false;
        RenderFragment content = builder => builder.AddMarkupContent(0, "<p>Ready</p>");
        var cut = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(parameter => parameter.Kicker, "Chat tools")
                .Add(parameter => parameter.Title, "Points")
                .Add(
                    parameter => parameter.LoadFailure,
                    new InvalidOperationException("Service unavailable")
                )
                .Add(
                    parameter => parameter.RetryLoadAsync,
                    EventCallback.Factory.Create(this, () => retried = true)
                )
                .Add(parameter => parameter.ChildContent, content)
        );

        cut.Find("[role='alert']").TextContent.ShouldContain("Service unavailable");
        cut.Find("button").Click();

        retried.ShouldBeTrue();
    }
}

public sealed class RailPreferenceTests
{
    [Test]
    public void VersionedRailPreference_IsReadAndUpdatedAfterAnExplicitToggle()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        module.Setup<bool>("readRailCollapsed").SetResult(true);
        context.ComponentFactories.AddStub<NavMenu>();
        context.ComponentFactories.AddStub<TopBarControls>();
        context.ComponentFactories.AddStub<ThemeToggle>();
        context.ComponentFactories.AddStub<PageHelpButton>();
        context.ComponentFactories.AddStub<ToastHost>();

        var cut = context.Render<MainLayout>();
        var toggle = cut.Find("button[aria-controls='desktop-navigation-rail']");

        toggle.GetAttribute("aria-expanded").ShouldBe("false");
        toggle.Click();

        var write = module.Invocations.Single(invocation =>
            invocation.Identifier == "writeRailCollapsed"
        );
        write.Arguments[0].ShouldBe(false);
    }
}

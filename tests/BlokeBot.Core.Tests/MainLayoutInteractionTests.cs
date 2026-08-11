using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MainLayoutInteractionTests
{
    [Test]
    public void MobileNavigation_OpeningAndClosing_UpdatesInteractionStateAndRestoresMenuFocus()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = context.ComponentFactories.AddStub<NavMenu>();
        _ = context.ComponentFactories.AddStub<TopBarControls>();
        _ = context.ComponentFactories.AddStub<ThemeToggle>();
        _ = context.ComponentFactories.AddStub<PageHelpButton>();
        _ = context.ComponentFactories.AddStub<ToastHost>();
        var cut = context.Render<MainLayout>();
        var menuButton = cut.Find("#mobile-navigation-menu-button");
        var background = cut.Find("[data-mobile-navigation-background]");

        background.HasAttribute("inert").ShouldBeFalse();

        menuButton.Click();

        background.HasAttribute("inert").ShouldBeTrue();
        menuButton.GetAttribute("aria-expanded").ShouldBe("true");
        var activate = context.JSInterop.Invocations.Single(static invocation =>
            invocation.Identifier == "blokeBotNavigation.activateMobileDrawer"
        );
        activate.Arguments[0].ShouldBe("mobile-navigation-drawer");

        cut.Find("button[aria-label='Close navigation menu']").Click();

        background.HasAttribute("inert").ShouldBeFalse();
        menuButton.GetAttribute("aria-expanded").ShouldBe("false");
        var restoreFocus = context.JSInterop.Invocations.Single(static invocation =>
            invocation.Identifier == "blokeBotNavigation.focus"
        );
        restoreFocus.Arguments[0].ShouldBe("mobile-navigation-menu-button");

        menuButton.Click();
        cut.Find("[data-mobile-navigation-overlay]").Click();

        background.HasAttribute("inert").ShouldBeFalse();
        menuButton.GetAttribute("aria-expanded").ShouldBe("false");
    }
}

public sealed class DesktopRailInteractionTests
{
    [Test]
    public void VersionedIconRailPreference_TogglesAndPersists()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        _ = module.Setup<bool>("readRailPresentation").SetResult(true);
        _ = context.ComponentFactories.AddStub<NavMenu>();
        _ = context.ComponentFactories.AddStub<TopBarControls>();
        _ = context.ComponentFactories.AddStub<ThemeToggle>();
        _ = context.ComponentFactories.AddStub<PageHelpButton>();
        _ = context.ComponentFactories.AddStub<ToastHost>();

        var cut = context.Render<MainLayout>();
        var railToggle = cut.Find("button[aria-controls='desktop-navigation-rail']");

        cut.Find(".app-shell").GetAttribute("data-rail-presentation").ShouldBe("icon");
        railToggle.GetAttribute("aria-expanded").ShouldBe("false");
        railToggle.Click();

        cut.Find(".app-shell").GetAttribute("data-rail-presentation").ShouldBe("expanded");
        module
            .Invocations.Single(static invocation =>
                invocation.Identifier == "writeRailPresentation"
            )
            .Arguments[0]
            .ShouldBe(false);
    }
}

public sealed class NavMenuInteractionTests
{
    [Test]
    [Arguments(HostFeatureFlags.Collectives, true)]
    [Arguments(HostFeatureFlags.None, false)]
    public async Task CollectivesDestination_FollowsFeatureGate(
        HostFeatureFlags enabledFeatures,
        bool expected
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, enabledFeatures);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<NavMenu>();

        cut.WaitForAssertion(() =>
        {
            var destinations = cut.FindAll("a[href='collectives']");
            if (expected)
            {
                destinations.ShouldNotBeEmpty();
            }
            else
            {
                destinations.ShouldBeEmpty();
            }
        });
    }

    [Test]
    [Arguments(HostFeatureFlags.ViewerPassports, true)]
    [Arguments(HostFeatureFlags.None, false)]
    public async Task ViewerPassportsDestination_FollowsFeatureGate(
        HostFeatureFlags enabledFeatures,
        bool expected
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, enabledFeatures);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<NavMenu>();

        cut.WaitForAssertion(() =>
        {
            var destinations = cut.FindAll("a[href='passports']");
            if (expected)
            {
                destinations.ShouldNotBeEmpty();
            }
            else
            {
                destinations.ShouldBeEmpty();
            }
        });
    }

    [Test]
    [Arguments(HostFeatureFlags.Competitions, true)]
    [Arguments(HostFeatureFlags.None, false)]
    public async Task CompetitionsDestination_FollowsFeatureGate(
        HostFeatureFlags enabledFeatures,
        bool expected
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, enabledFeatures);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<NavMenu>();

        cut.WaitForAssertion(() =>
        {
            var destinations = cut.FindAll("a[href='competitions']");
            if (expected)
            {
                destinations.ShouldNotBeEmpty();
            }
            else
            {
                destinations.ShouldBeEmpty();
            }
        });
    }

    [Test]
    [Arguments(HostFeatureFlags.Bingo, true)]
    [Arguments(HostFeatureFlags.None, false)]
    public async Task BingoDestination_FollowsFeatureGate(
        HostFeatureFlags enabledFeatures,
        bool expected
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, enabledFeatures);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<NavMenu>();

        cut.WaitForAssertion(() =>
        {
            var destinations = cut.FindAll("a[href='bingo']");
            if (expected)
            {
                destinations.ShouldNotBeEmpty();
            }
            else
            {
                destinations.ShouldBeEmpty();
            }
        });
    }

    [Test]
    [Arguments(HostFeatureFlags.CommunityProgression, true)]
    [Arguments(HostFeatureFlags.None, false)]
    public async Task CommunityProgressionDestination_FollowsFeatureGate(
        HostFeatureFlags enabledFeatures,
        bool expected
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, enabledFeatures);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<NavMenu>();

        cut.WaitForAssertion(() =>
        {
            var destinations = cut.FindAll("a[href='community']");
            if (expected)
            {
                destinations.ShouldNotBeEmpty();
            }
            else
            {
                destinations.ShouldBeEmpty();
            }
        });
    }

    [Test]
    public async Task IconRail_GroupOpensAndEscapeCloses()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var module = context.JSInterop.SetupModule("./Components/Layout/NavMenu.razor.js");
        SetupExpandedGroups(module);
        var cut = context.Render<NavMenu>(static parameters =>
            parameters.Add(
                static parameter => parameter.Presentation,
                NavigationPresentation.IconRail
            )
        );

        var nativeButton = cut.Find("[data-nav-section='twitch-operations'] button");
        nativeButton.GetAttribute("aria-expanded").ShouldBe("false");

        nativeButton.Click();

        nativeButton = cut.Find("[data-nav-section='twitch-operations'] button");
        nativeButton.GetAttribute("aria-expanded").ShouldBe("true");

        nativeButton.KeyDown("Escape");

        cut.Find("[data-nav-section='twitch-operations'] button")
            .GetAttribute("aria-expanded")
            .ShouldBe("false");
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        HostFeatureFlags enabledFeatures = HostFeatureFlags.All
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = enabledFeatures,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static void SetupExpandedGroups(BunitJSModuleInterop module)
    {
        _ = module
            .Setup<bool>("readBoolean", "blokebot.sidebar.guessing.open", true)
            .SetResult(true);
        _ = module.Setup<bool>("readBoolean", "blokebot.sidebar.points.open", true).SetResult(true);
        _ = module
            .Setup<bool>("readBoolean", "blokebot.sidebar.customcommands.open", true)
            .SetResult(true);
        _ = module
            .Setup<bool>("readBoolean", "blokebot.sidebar.nativetwitch.open", true)
            .SetResult(true);
    }
}

public sealed class SharedDisclosureTests
{
    [Test]
    public void ClosedDisclosure_KeepsAResolvingTargetAndRemovesDescendantsFromTraversal()
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

        var trigger = cut.Find("button");
        var targetId = trigger.GetAttribute("aria-controls");
        targetId.ShouldNotBeNullOrWhiteSpace();
        trigger.GetAttribute("aria-expanded").ShouldBe("false");
        cut.Find($"#{targetId}").HasAttribute("hidden").ShouldBeTrue();
        cut.Find($"#{targetId}").GetAttribute("aria-hidden").ShouldBe("true");
        cut.FindAll("input").ShouldBeEmpty();

        trigger.Click();

        cut.Find("button").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find($"#{targetId}").HasAttribute("hidden").ShouldBeFalse();
        cut.Find($"#{targetId}").GetAttribute("aria-hidden").ShouldBe("false");
        _ = cut.Find("input[aria-label='Configuration value']").ShouldNotBeNull();
    }

    [Test]
    public void ValidationFocusRequest_RevealsTheDisclosureAndFocusesTheActionableField()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/CollapsibleSection.razor.js");
        _ = module.SetupVoid("focusElement", "validation-target").SetVoidResult();
        RenderFragment content = builder =>
            builder.AddMarkupContent(0, "<input id='validation-target' value='unsaved value' />");

        var cut = context.Render<CollapsibleSection>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Configuration")
                .Add(parameter => parameter.InitiallyOpen, false)
                .Add(parameter => parameter.FocusElementId, "validation-target")
                .Add(parameter => parameter.FocusRequest, 1L)
                .Add(parameter => parameter.ChildContent, content)
        );

        cut.Find("button").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find("#validation-target").GetAttribute("value").ShouldBe("unsaved value");
        module
            .Invocations.Single(invocation => invocation.Identifier == "focusElement")
            .Arguments[0]
            .ShouldBe("validation-target");
    }
}

public sealed class SharedPageContractTests
{
    [Test]
    public void AuthenticatedLoadFailure_RendersADurableInlineAlertAndRetry()
    {
        using var context = new BunitContext();
        var retryCount = 0;
        RenderFragment content = builder => builder.AddMarkupContent(0, "<p data-ready>Ready</p>");
        var cut = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Points")
                .Add(
                    parameter => parameter.LoadState,
                    new PageLoadState.Failure(
                        "The channel data is unavailable.",
                        () =>
                        {
                            retryCount++;
                            return Task.CompletedTask;
                        }
                    )
                )
                .Add(parameter => parameter.ChildContent, content)
        );

        cut.Find("button").Click();

        retryCount.ShouldBe(1);
    }
}

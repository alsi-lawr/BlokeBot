using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class DesktopRailInteractionTests
{
    [Test]
    public void VersionedIconRailPreference_TogglesWithoutChangingTheLabelledMobileDrawer()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        module.Setup<bool>("readRailPresentation").SetResult(true);
        context.ComponentFactories.AddStub<NavMenu>();
        context.ComponentFactories.AddStub<TopBarControls>();
        context.ComponentFactories.AddStub<ThemeToggle>();
        context.ComponentFactories.AddStub<PageHelpButton>();
        context.ComponentFactories.AddStub<ToastHost>();

        var cut = context.Render<MainLayout>();
        var railToggle = cut.Find("button[aria-controls='desktop-navigation-rail']");

        cut.Find(".app-shell").GetAttribute("data-rail-presentation").ShouldBe("icon");
        railToggle.GetAttribute("aria-expanded").ShouldBe("false");
        cut.Find("#mobile-navigation-drawer")
            .GetAttribute("aria-label")
            .ShouldBe("Main navigation");
        cut.Find("#mobile-navigation-drawer").HasAttribute("inert").ShouldBeTrue();

        railToggle.Click();

        cut.Find(".app-shell").GetAttribute("data-rail-presentation").ShouldBe("expanded");
        module
            .Invocations.Single(invocation => invocation.Identifier == "writeRailPresentation")
            .Arguments[0]
            .ShouldBe(false);
    }
}

public sealed class NavMenuInventoryTests
{
    [Test]
    public async Task IconRail_ExposesEveryDirectGroupAndChildDestinationWithCurrentState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var module = context.JSInterop.SetupModule("./Components/Layout/NavMenu.razor.js");
        SetupExpandedGroups(module);
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/twitch-operations/polls");

        var cut = context.Render<NavMenu>(parameters =>
            parameters.Add(parameter => parameter.Presentation, NavigationPresentation.IconRail)
        );

        cut.Find(".nav-menu").GetAttribute("data-navigation-mode").ShouldBe("icon");
        cut.FindAll("[data-nav-destination]")
            .Select(element => element.GetAttribute("data-nav-destination"))
            .ShouldBe(["home", "alerts", "host"]);
        cut.FindAll("[data-nav-section]")
            .Select(element => element.GetAttribute("data-nav-section"))
            .ShouldBe(["twitch-operations", "guessing", "points", "custom-commands"]);
        cut.FindAll("[data-nav-section] button")
            .ShouldAllBe(button => button.GetAttribute("aria-expanded") == "false");

        var nativeButton = cut.Find("[data-nav-section='twitch-operations'] button");
        var nativeBodyId = nativeButton.GetAttribute("aria-controls");
        nativeBodyId.ShouldNotBeNullOrWhiteSpace();
        cut.Find($"#{nativeBodyId}").HasAttribute("hidden").ShouldBeTrue();

        nativeButton.Click();

        nativeButton = cut.Find("[data-nav-section='twitch-operations'] button");
        nativeButton.GetAttribute("aria-expanded").ShouldBe("true");
        nativeButton.GetAttribute("aria-current").ShouldBe("page");
        var childDestinations = cut.FindAll("[data-nav-section] a")
            .Select(link => link.GetAttribute("href"))
            .ToArray();
        childDestinations.ShouldBe([
            "twitch-operations/shoutouts",
            "twitch-operations/polls",
            "twitch-operations/clips-markers",
            "twitch-operations/channel-points",
            "twitch-operations/predictions",
            "guessing",
            "guessing/settings",
            "points",
            "points/settings",
            "custom-commands/settings",
        ]);
        cut.Find("a[href='twitch-operations/polls']").GetAttribute("aria-current").ShouldBe("page");
        cut.FindAll("nav a[href]")
            .Where(link => !link.ClassList.Contains("nav-menu__brand-link"))
            .ShouldAllBe(link => !string.IsNullOrWhiteSpace(link.GetAttribute("aria-label")));

        foreach (var describedControl in cut.FindAll("[aria-describedby]"))
        {
            var helpId = describedControl.GetAttribute("aria-describedby");
            helpId.ShouldNotBeNullOrWhiteSpace();
            cut.Find($"#{helpId}").GetAttribute("role").ShouldBe("tooltip");
        }

        nativeButton.KeyDown("Escape");

        cut.Find("[data-nav-section='twitch-operations'] button")
            .GetAttribute("aria-expanded")
            .ShouldBe("false");
        cut.Find($"#{nativeBodyId}").HasAttribute("hidden").ShouldBeTrue();
    }

    [Test]
    public async Task LabelledNavigation_KeepsLabelsAndExpandedGroupPreferences()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var module = context.JSInterop.SetupModule("./Components/Layout/NavMenu.razor.js");
        SetupExpandedGroups(module);

        var cut = context.Render<NavMenu>();

        cut.Find(".nav-menu").GetAttribute("data-navigation-mode").ShouldBe("labelled");
        cut.FindAll(".nav-menu__label")
            .Select(element => element.TextContent.Trim())
            .ShouldContain("Channel setup");
        cut.FindAll(".nav-menu__section-label, [data-nav-section]")
            .Select(element =>
                element.ClassList.Contains("nav-menu__section-label")
                    ? element.TextContent.Trim()
                    : element.GetAttribute("data-nav-section")
            )
            .ShouldBe(["Chat tools", "twitch-operations", "guessing", "points", "custom-commands"]);
        cut.FindAll("[data-nav-section] button")
            .ShouldAllBe(button => button.GetAttribute("aria-expanded") == "true");
        cut.FindAll("[aria-describedby]").ShouldBeEmpty();
        cut.Find(".nav-menu__active-channel").TextContent.ShouldContain("#streamer");
        cut.Find(".nav-menu__build").TextContent.ShouldContain("Build");
    }

    [Test]
    public async Task NativeTwitchOnly_RendersFirstWithinTheChatToolsSection()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, HostFeatureFlags.NativeTwitch);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var module = context.JSInterop.SetupModule("./Components/Layout/NavMenu.razor.js");
        SetupExpandedGroups(module);

        var cut = context.Render<NavMenu>();

        cut.FindAll(".nav-menu__section-label, [data-nav-section]")
            .Select(element =>
                element.ClassList.Contains("nav-menu__section-label")
                    ? element.TextContent.Trim()
                    : element.GetAttribute("data-nav-section")
            )
            .ShouldBe(["Chat tools", "twitch-operations"]);
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
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static void SetupExpandedGroups(BunitJSModuleInterop module)
    {
        module.Setup<bool>("readBoolean", "blokebot.sidebar.guessing.open", true).SetResult(true);
        module.Setup<bool>("readBoolean", "blokebot.sidebar.points.open", true).SetResult(true);
        module
            .Setup<bool>("readBoolean", "blokebot.sidebar.customcommands.open", true)
            .SetResult(true);
        module
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
        cut.Find("input[aria-label='Configuration value']").ShouldNotBeNull();
    }

    [Test]
    public void ValidationFocusRequest_RevealsTheDisclosureAndFocusesTheActionableField()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/CollapsibleSection.razor.js");
        module.SetupVoid("focusElement", "validation-target").SetVoidResult();
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

        cut.Find("[role='alert']").TextContent.ShouldContain("The channel data is unavailable.");
        cut.FindAll("[data-ready]").ShouldBeEmpty();

        cut.Find("button").Click();

        retryCount.ShouldBe(1);
    }

    [Test]
    [Arguments(PageSaveFeedbackKind.Dirty, "status")]
    [Arguments(PageSaveFeedbackKind.Saving, "status")]
    [Arguments(PageSaveFeedbackKind.Validation, "alert")]
    [Arguments(PageSaveFeedbackKind.Success, "status")]
    [Arguments(PageSaveFeedbackKind.Failure, "alert")]
    public void SaveFeedback_RemainsReachableAndAnnouncesEveryState(
        PageSaveFeedbackKind kind,
        string expectedRole
    )
    {
        using var context = new BunitContext();
        RenderFragment actions = builder =>
            builder.AddMarkupContent(0, "<button>Save changes</button>");
        RenderFragment content = builder => builder.AddMarkupContent(0, "<p>Ready</p>");
        var cut = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Settings")
                .Add(parameter => parameter.Width, DashboardPageWidth.Wide)
                .Add(parameter => parameter.Actions, actions)
                .Add(parameter => parameter.SaveFeedback, new PageSaveFeedback("Save state", kind))
                .Add(parameter => parameter.ChildContent, content)
        );

        cut.Find(".dashboard-page").ClassList.ShouldContain("dashboard-page--wide");
        cut.Find("[data-persistent-page-actions]").TextContent.ShouldContain("Save changes");
        cut.Find("[data-save-feedback]")
            .GetAttribute("data-save-feedback")
            .ShouldBe(kind.ToString().ToLowerInvariant());
        cut.Find("[data-save-feedback]").GetAttribute("role").ShouldBe(expectedRole);
    }

    [Test]
    public void SharedFieldAndDataRoles_ExposeFullRowAndBoundedTableContracts()
    {
        using var context = new BunitContext();
        var field = context.Render<Field>(parameters =>
            parameters
                .Add(parameter => parameter.Id, "channel")
                .Add(parameter => parameter.Label, "Channel")
        );
        RenderFragment table = builder =>
            builder.AddMarkupContent(0, "<table><tbody><tr><td>Data</td></tr></tbody></table>");
        var region = context.Render<DataTableRegion>(parameters =>
            parameters
                .Add(parameter => parameter.Label, "Channel history")
                .Add(parameter => parameter.ChildContent, table)
        );

        field.Find("[data-field]").ClassList.ShouldContain("field");
        field.Find("input").ClassList.ShouldContain("input");
        region.Find("[role='region']").GetAttribute("aria-label").ShouldBe("Channel history");
        region.Find("[role='region']").GetAttribute("tabindex").ShouldBe("0");
    }

    [Test]
    public void MobileFieldRow_WithAdjacentAction_ExposesFullRowFieldContract()
    {
        using var context = new BunitContext();
        RenderFragment fieldRow = builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "field-row");
            builder.OpenComponent<Field>(2);
            builder.AddAttribute(3, nameof(Field.Id), "channel");
            builder.AddAttribute(4, nameof(Field.Label), "Channel");
            builder.CloseComponent();
            builder.OpenElement(5, "button");
            builder.AddContent(6, "Connect");
            builder.CloseElement();
            builder.CloseElement();
        };

        var cut = context.Render(fieldRow);
        var controlsStyles = ReadRepositoryFile(
            "src",
            "BlokeBot.Core",
            "Styles",
            "components",
            "controls.css"
        );
        var normalizedStyles = string.Join(
            " ",
            controlsStyles.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );

        cut.Find(".field-row > .field").ClassList.ShouldContain("field");
        cut.Find(".field-row > .field + button").TextContent.ShouldBe("Connect");
        normalizedStyles.ShouldContain(
            "@media (max-width: 30rem) { .field { flex-basis: 100%; min-width: 100%; width: 100%; }"
        );
    }

    [Test]
    public void SettingsDisclosureStack_WithAdjacentPanels_UsesTwelvePixelGapContract()
    {
        using var context = new BunitContext();
        RenderFragment stack = builder =>
            builder.AddMarkupContent(
                0,
                """
                <div class="settings-disclosure-stack">
                    <section class="disclosure-panel">General</section>
                    <section class="disclosure-panel">Chat commands</section>
                </div>
                """
            );

        var cut = context.Render(stack);
        var pageContextStyles = ReadRepositoryFile(
            "src",
            "BlokeBot.Core",
            "Styles",
            "components",
            "page-context.css"
        );
        var normalizedStyles = string.Join(
            " ",
            pageContextStyles.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );

        cut.FindAll(".settings-disclosure-stack > .disclosure-panel").Count.ShouldBe(2);
        normalizedStyles.ShouldContain(
            ".settings-disclosure-stack { display: grid; gap: 0.75rem; }"
        );
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(relativePath)}'."
        );
    }
}

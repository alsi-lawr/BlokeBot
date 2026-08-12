using AngleSharp.Dom;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PageHelpButtonTests
{
    private const string _focusInterop = "Blazor._internal.domWrapper.focus";

    /// <summary>
    /// The complete route map BLOKEBOT-208 accepted: every page-help location and the
    /// BlokeBot.Site guide it must resolve to.
    /// </summary>
    private static readonly IReadOnlyList<(string Path, string Fragment, string Guide)> _routeMap =
    [
        ("/", "", "/dashboard"),
        ("/guessing", "", "/guessing"),
        ("/guessing/settings", "", "/guessing"),
        ("/points", "", "/points"),
        ("/points/settings", "", "/points"),
        ("/custom-commands/settings", "", "/commands"),
        ("/host", "", "/channels"),
        ("/requests", "", "/community/request-boards"),
        ("/bounties", "", "/community/bounties"),
        ("/community", "", "/community/progression"),
        ("/raid", "", "/community/blokeraid"),
        ("/passports", "", "/community/passports"),
        ("/passports/samplechannel/me", "", "/community/passports"),
        ("/bingo", "", "/community/bingo"),
        ("/competitions", "", "/community/competitions"),
        ("/raid-collaboration", "", "/community/raid-collaboration"),
        ("/collectives", "", "/community/collectives"),
        ("/queues", "", "/community/play-with-viewers"),
        ("/moments", "", "/community/moments"),
        ("/overlays", "", "/overlays"),
        ("/overlays", "cues", "/overlays/cues"),
        ("/overlays", "media", "/overlays/media"),
        ("/twitch-operations/shoutouts", "", "/twitch-operations/shoutouts"),
        ("/twitch-operations/polls", "", "/twitch-operations/polls"),
        ("/twitch-operations/clips-markers", "", "/twitch-operations/clips-markers"),
        ("/twitch-operations/channel-points", "", "/twitch-operations/channel-points"),
        ("/twitch-operations/predictions", "", "/twitch-operations/predictions"),
    ];

    [Test]
    public void EveryConcreteHostSelectedFeatureRoute_HasUsefulRouteSpecificHelp()
    {
        var routes = HostSelectedRoutes();

        routes.ShouldAllBe(static route => PageHelpButton.HasUsefulHelpForPath(route));
    }

    [Test]
    public void EveryConcreteHostSelectedFeatureRoute_IsCoveredByTheGuideRouteMap()
    {
        var mapped = _routeMap.Select(static entry => entry.Path).ToHashSet(StringComparer.Ordinal);

        HostSelectedRoutes().ShouldAllBe(route => mapped.Contains(route));
    }

    [Test]
    public void EveryHelpLocation_ResolvesToItsAcceptedSiteGuidePath()
    {
        foreach (var (path, fragment, guide) in _routeMap)
        {
            PageHelpButton
                .GuidePathForLocation(path, fragment)
                .ShouldBe(guide, $"{path}#{fragment}");
        }
    }

    [Test]
    public void RouteWithoutHelp_HasNoGuideDestination()
    {
        PageHelpButton.GuidePathForLocation("/automations/events", "").ShouldBeNull();
        PageHelpButton.GuidePathForLocation("/not-a-route", "").ShouldBeNull();
    }

    [Test]
    public void WithdrawnAutomationEventsSurface_HasNoRouteOrPageHelp()
    {
        typeof(AutomationEventsPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .ShouldBeEmpty();
        PageHelpButton.HasUsefulHelpForPath("/automations/events").ShouldBeFalse();
    }

    [Test]
    public void ViewerPassportEditorRoutes_HaveUsefulHelp()
    {
        PageHelpButton.HasUsefulHelpForPath("/passports").ShouldBeTrue();
        PageHelpButton.HasUsefulHelpForPath("/passports/samplechannel/me").ShouldBeTrue();
    }

    [Test]
    [Arguments("https://guide.example.com", "https://guide.example.com/community/bounties")]
    [Arguments("https://guide.example.com/", "https://guide.example.com/community/bounties")]
    [Arguments("http://guide.example.com", "http://guide.example.com/community/bounties")]
    [Arguments("https://example.com/docs", "https://example.com/docs/community/bounties")]
    [Arguments("https://example.com/docs/", "https://example.com/docs/community/bounties")]
    [Arguments("  https://example.com/docs  ", "https://example.com/docs/community/bounties")]
    public void AcceptedBase_KeepsItsPathPrefixWhenResolvingAMappedGuide(
        string configured,
        string expected
    ) => HelpSiteGuide.Resolve(configured, "/community/bounties")?.ToString().ShouldBe(expected);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("/guide")]
    [Arguments("guide.example.com")]
    [Arguments("://guide.example.com")]
    [Arguments("ftp://guide.example.com/")]
    [Arguments("javascript:alert('x')")]
    [Arguments("file:///etc/guide")]
    [Arguments("https://user:secret@guide.example.com/")]
    [Arguments("https://guide.example.com/?tenant=1")]
    [Arguments("https://guide.example.com/#top")]
    public void RejectedBase_ResolvesToNoLinkAtAll(string? configured)
    {
        HelpSiteGuide.BaseAddress(configured).ShouldBeNull();
        HelpSiteGuide.Resolve(configured, "/community/bounties").ShouldBeNull();
    }

    [Test]
    public void RejectedBase_IsNotAStartupValidationFailure() =>
        BlokeBotOptionsValidation
            .IsValid(new BlokeBotOptions { HelpSiteBaseUrl = "not-a-url" })
            .ShouldBeTrue();

    [Test]
    public void ConfiguredBase_RendersOneGuideLinkNamedReadTheFullGuide()
    {
        using var context = CreateContext("https://guide.example.com/docs");
        var help = RenderAt(context, "/bounties");

        help.Find("button[aria-label='Page help']").Click();

        var links = help.FindAll("[data-help-guide]");
        links.Count.ShouldBe(1);
        links[0].GetAttribute("href").ShouldBe("https://guide.example.com/docs/community/bounties");
        AccessibleName(links[0]).ShouldBe("Read the full guide");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not-a-url")]
    public void BlankOrRejectedBase_RendersHelpWithNoGuideLink(string? configured)
    {
        using var context = CreateContext(configured);
        var help = RenderAt(context, "/bounties");

        help.Find("button[aria-label='Page help']").Click();

        help.FindAll("[data-help-guide]").ShouldBeEmpty();
        help.Find("#page-help-popover").TextContent.ShouldContain("Fund and settle a challenge");
    }

    [Test]
    public void HelpTrigger_IsAKeyboardActivatedButtonThatOwnsItsLabelledPopover()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        var trigger = help.Find("button[aria-label='Page help']");

        trigger.GetAttribute("type").ShouldBe("button");
        trigger.GetAttribute("aria-expanded").ShouldBe("false");
        trigger.GetAttribute("aria-controls").ShouldBe("page-help-popover");
        help.FindAll("#page-help-popover").ShouldBeEmpty();

        trigger.Click();

        help.Find("button[aria-label='Page help']").GetAttribute("aria-expanded").ShouldBe("true");
        var popover = help.Find("#page-help-popover");
        popover.GetAttribute("aria-labelledby").ShouldBe("page-help-title");
        help.Find("#page-help-title").TextContent.Trim().ShouldBe("Play with viewers");
    }

    [Test]
    public void HelpTrigger_TogglesClosedAndKeepsFocusOnTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");

        help.Find("button[aria-label='Page help']").Click();
        help.Find("button[aria-label='Page help']").Click();

        help.FindAll("#page-help-popover").ShouldBeEmpty();
        help.Find("button[aria-label='Page help']").GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Test]
    public void CloseButton_ClosesThePopoverAndRestoresFocusToTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        help.Find("button[aria-label='Page help']").Click();
        var focusCalls = FocusCalls(context);

        help.Find("button[aria-label='Close help']").Click();

        help.FindAll("#page-help-popover").ShouldBeEmpty();
        FocusCalls(context).ShouldBe(focusCalls + 1);
    }

    [Test]
    public void Escape_ClosesThePopoverAndRestoresFocusToTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        help.Find("button[aria-label='Page help']").Click();
        var focusCalls = FocusCalls(context);

        help.Find("#page-help-popover").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        help.FindAll("#page-help-popover").ShouldBeEmpty();
        FocusCalls(context).ShouldBe(focusCalls + 1);
    }

    [Test]
    public void Navigation_DismissesThePopoverWithoutStealingFocusBackToTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        help.Find("button[aria-label='Page help']").Click();
        var focusCalls = FocusCalls(context);

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/bounties");

        help.WaitForAssertion(() => help.FindAll("#page-help-popover").ShouldBeEmpty());
        FocusCalls(context).ShouldBe(focusCalls);
    }

    [Test]
    public void EveryHelpPopover_DropsTheSharedFeatureSwitchSectionAndItsVocabulary()
    {
        string[] banned =
        [
            "turning this tool on or off",
            "suppressed",
            "reconciliation",
            "watermark",
            "authoritative",
            "idempotent",
            "projection",
            "provider action",
            "provider work",
            "provider call",
            "provider access",
        ];

        foreach (var (path, fragment, _) in _routeMap)
        {
            var text = HelpText(path, fragment).ToLowerInvariant();
            foreach (var phrase in banned)
            {
                text.ShouldNotContain(phrase, Case.Sensitive, $"{path}#{fragment}");
            }
        }
    }

    [Test]
    public void HelpPopovers_KeepTheirTestActionLabelsAndNamedSafetyMeaning()
    {
        HelpText("/custom-commands/settings", "").ShouldContain("Test cue");
        HelpText("/overlays", "").ShouldContain("Send test pulse");

        HelpText("/passports", "")
            .ShouldContain("Reset permanently removes the passport and its chat-presence days");
        HelpText("/bounties", "").ShouldContain("Private bounties show nothing publicly");
        HelpText("/queues", "").ShouldContain("Lobby messages and moderator notes stay private");
        HelpText("/community", "")
            .ShouldContain("resets active repeatable progress, so you are asked to confirm first");
        HelpText("/collectives", "").ShouldContain("shared summary");
    }

    [Test]
    public void RaidCollaborationHelp_KeepsTheReviewedTwoSectionCompositionAndItsSafetyMeaning()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/raid-collaboration");
        help.Find("button[aria-label='Page help']").Click();

        var sections = help.Find("#page-help-popover").QuerySelectorAll("h3");

        sections
            .Select(section => section.TextContent.Trim())
            .ShouldBe(["Choose where to raid", "Change welcome and shortlist rules"]);
        var text = help.Find("#page-help-popover").TextContent;
        text.ShouldContain("Approval is an allowlist you control");
        text.ShouldContain("Prepare raid always asks you to confirm");
        text.ShouldContain("no individual viewer is recorded");
    }

    [Test]
    public void HelpCopy_AssertsWithoutFirstNegatingSomethingElse()
    {
        string[] bannedConstructions = [", not a ", ", not an ", ", not the ", " but rather "];

        foreach (var (path, fragment, _) in _routeMap)
        {
            var text = HelpText(path, fragment);
            foreach (var construction in bannedConstructions)
            {
                text.ShouldNotContain(construction, Case.Sensitive, $"{path}#{fragment}");
            }
        }
    }

    private static IReadOnlyList<string> HostSelectedRoutes()
    {
        const string RedirectOnlyRoute = "/twitch-operations";
        var routes = DiscoverHostSelectedRoutes(RedirectOnlyRoute);
        routes.Count.ShouldBeGreaterThan(20);
        return routes;
    }

    private static IReadOnlyList<string> DiscoverHostSelectedRoutes(string redirectOnlyRoute) =>
        [
            .. typeof(PageHelpButton)
                .Assembly.GetTypes()
                .Where(static type =>
                    type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                        .Cast<AuthorizeAttribute>()
                        .Any(static attribute => attribute.Policy == "HostSelected")
                )
                .SelectMany(static type =>
                    type.GetCustomAttributes(typeof(RouteAttribute), true)
                        .Cast<RouteAttribute>()
                        .Select(static route => route.Template)
                )
                .Where(route => route != redirectOnlyRoute)
                .Order(StringComparer.Ordinal),
        ];

    private static string HelpText(string path, string fragment)
    {
        using var context = CreateContext(null);
        var location = string.IsNullOrEmpty(fragment) ? path : $"{path}#{fragment}";
        var help = RenderAt(context, location);
        help.Find("button[aria-label='Page help']").Click();
        return help.Find("#page-help-popover").TextContent;
    }

    private static IRenderedComponent<PageHelpButton> RenderAt(
        BunitContext context,
        string location
    )
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(location);
        return context.Render<PageHelpButton>();
    }

    private static BunitContext CreateContext(string? helpSiteBaseUrl)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = context.Services.AddScoped<DashboardFragmentState>();
        _ = context.Services.AddSingleton<IOptions<BlokeBotOptions>>(
            Options.Create(new BlokeBotOptions { HelpSiteBaseUrl = helpSiteBaseUrl })
        );
        return context;
    }

    private static int FocusCalls(BunitContext context) =>
        context.JSInterop.Invocations.Count(invocation => invocation.Identifier == _focusInterop);

    private static string AccessibleName(IElement element)
    {
        var clone = (IElement)element.Clone(deep: true);
        foreach (var hidden in clone.QuerySelectorAll("[aria-hidden='true']").ToArray())
        {
            hidden.Remove();
        }

        return clone.TextContent.Trim();
    }
}

using BlokeBot.Core.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PageHelpButtonTests
{
    [Test]
    [Arguments("/twitch-operations/shoutouts", "Shoutouts", "page shows when you can send")]
    [Arguments("/twitch-operations/polls", "Polls", "Save a question")]
    [Arguments("/twitch-operations/clips-markers", "Clips & markers", "Check outcome")]
    [Arguments(
        "/twitch-operations/channel-points",
        "Rewards & redemptions",
        "return the viewer’s Channel Points"
    )]
    [Arguments("/twitch-operations/predictions", "Predictions", "winning outcome")]
    [Arguments("/requests", "Request boards", "public board link becomes available")]
    [Arguments("/queues", "Play with viewers", "viewer-page link becomes available")]
    [Arguments("/moments", "Moments", "shareable recap in a new tab")]
    [Arguments("/overlays/sources", "Overlays", "require both Overlays and Guessing game")]
    public void FeatureRoute_RendersOneButtonAndOpensRouteSpecificHelp(
        string path,
        string title,
        string distinctiveContent
    )
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var cut = context.Render<PageHelpButton>();

        var helpButtons = cut.FindAll("button[aria-label='Page help']");
        helpButtons.Count.ShouldBe(1);
        helpButtons.Single().HasAttribute("aria-expanded").ShouldBeFalse();

        helpButtons.Single().Click();

        cut.Find("button[aria-label='Page help']").HasAttribute("aria-expanded").ShouldBeTrue();
        _ = cut.Find("button[aria-label='Close help']").ShouldNotBeNull();
        cut.Find("h2").TextContent.ShouldBe(title);
        cut.Markup.ShouldContain(distinctiveContent);
    }

    [Test]
    public void EveryConcreteHostSelectedFeatureRoute_HasUsefulRouteSpecificHelp()
    {
        const string RedirectOnlyRoute = "/twitch-operations";
        var routes = typeof(PageHelpButton)
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
            .Where(static route => route != RedirectOnlyRoute)
            .Order(StringComparer.Ordinal)
            .ToArray();

        routes.ShouldBe([
            "/custom-commands/settings",
            "/guessing",
            "/guessing/settings",
            "/moments",
            "/overlays/cues",
            "/overlays/media",
            "/overlays/sources",
            "/points",
            "/points/settings",
            "/queues",
            "/requests",
            "/twitch-operations/channel-points",
            "/twitch-operations/clips-markers",
            "/twitch-operations/polls",
            "/twitch-operations/predictions",
            "/twitch-operations/shoutouts",
        ]);
        routes.ShouldAllBe(static route => PageHelpButton.HasUsefulHelpForPath(route));
    }

    [Test]
    public void ShoutoutsHelp_CoversTheAcceptedAutomaticRaidContract()
    {
        using var context = new BunitContext();
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/twitch-operations/shoutouts");
        var cut = context.Render<PageHelpButton>();

        cut.Find("button[aria-label='Page help']").Click();

        var text = cut.Markup;
        text.ShouldContain("Automatic raid shoutouts");
        text.ShouldContain("up to two minutes");
        text.ShouldContain("either a native Twitch shoutout or one chat message");
        text.ShouldContain("When chat delivery is selected");
        text.ShouldContain("Use these details in the message");
        text.ShouldContain("A pinned shoutout");
        text.ShouldContain("regular, pinned, or announcement");
        text.ShouldContain("does not switch modes or send it again");
        text.ShouldContain("replaces the current pin");
        text.ShouldContain("previous pin is not restored");
        text.ShouldContain("{twitch_handle}");
        text.ShouldContain("{display_name}");
        text.ShouldContain("{channel_url}");
        text.ShouldContain("{last_game|fallback}");
        text.ShouldContain("{stream_title|fallback}");
        text.ShouldContain("{viewer_count}");
    }

    [Test]
    public void CustomCommandsHelp_ExplainsAutomationRuntimeAndInheritedSwitches()
    {
        using var context = new BunitContext();
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/custom-commands/settings");
        var cut = context.Render<PageHelpButton>();

        cut.Find("button[aria-label='Page help']").Click();

        cut.Markup.ShouldContain("Run automation flow is a runtime foundation");
        cut.Markup.ShouldContain("visual flow building and editing are not available here");
        cut.Markup.ShouldContain("both the Custom commands and Automations switches");
        cut.Markup.ShouldContain("without replaying work suppressed");
    }

    [Test]
    public void SignedInHelp_UsesTaskLanguageAndPreservesPrivacyAndOrderingFacts()
    {
        var host = OpenHelpFor("/host");
        host.ShouldContain("Twitch actions");
        host.ShouldContain("main command name");
        host.ShouldContain("first command name");
        host.ShouldNotContain("provider actions");
        host.ShouldNotContain("canonical name");

        var guessing = OpenHelpFor("/guessing/settings");
        guessing.ShouldContain(
            "Enter the main answer first, then any accepted alternatives, separated by commas."
        );
        guessing.ShouldNotContain("canonical name");

        var queues = OpenHelpFor("/queues");
        queues.ShouldContain("Every configured entry field is optional");
        queues.ShouldContain("viewer page and Viewer Queue overlay");
        queues.ShouldContain("Lobby messages and moderator notes stay private");
        queues.ShouldNotContain("Entry fields are private to moderators");

        var customCommands = OpenHelpFor("/custom-commands/settings");
        customCommands.ShouldContain("selected cue and Browser Source");
        customCommands.ShouldContain("without sending chat");
        customCommands.ShouldContain("consuming a one-time viewer use");
        customCommands.ShouldNotContain("host-bound playback admission");
        customCommands.ShouldNotContain("use claims");

        var requests = OpenHelpFor("/requests");
        requests.ShouldContain("actions on connected services");
        requests.ShouldNotContain("provider actions");
    }

    [Test]
    public void OpenHelp_RouteChangeClosesThePanelAndSelectsTheNewRouteContent()
    {
        using var context = new BunitContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/twitch-operations/shoutouts");
        var cut = context.Render<PageHelpButton>();
        cut.Find("button[aria-label='Page help']").Click();

        navigation.NavigateTo("/twitch-operations/polls");

        cut.Find("button[aria-label='Page help']").HasAttribute("aria-expanded").ShouldBeFalse();
        cut.FindAll("button[aria-label='Close help']").ShouldBeEmpty();

        cut.Find("button[aria-label='Page help']").Click();
        cut.Find("h2").TextContent.ShouldBe("Polls");
    }

    [Test]
    public void UnmappedRoute_DoesNotRenderAHelpButton()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/alerts");

        var cut = context.Render<PageHelpButton>();

        cut.FindAll("button[aria-label='Page help']").ShouldBeEmpty();
    }

    [Test]
    public void ExistingMappedRoute_RetainsItsHelpButtonAndContent()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/");
        var cut = context.Render<PageHelpButton>();

        cut.Find("button[aria-label='Page help']").Click();

        cut.Find("h2").TextContent.ShouldBe("Home");
        cut.Markup.ShouldContain("Where to go next");
    }

    [Test]
    public void ChannelSetupHelp_DistinguishesConnectionsAndExplainsIntegrationControls()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/host");
        var cut = context.Render<PageHelpButton>();

        cut.Find("button[aria-label='Page help']").Click();

        cut.Markup.ShouldContain("Chat access");
        cut.Markup.ShouldContain("Twitch integration");
        cut.Markup.ShouldContain("disconnect it to remove BlokeBot's stored authorization");
        cut.Markup.ShouldContain("bot account");
        cut.Markup.ShouldNotContain("Twitch operations");
    }

    private static string OpenHelpFor(string path)
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var cut = context.Render<PageHelpButton>();
        cut.Find("button[aria-label='Page help']").Click();
        return cut.Markup;
    }
}

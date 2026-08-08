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
    [Arguments("/overlays", "Overlays", "require both Overlays and Guessing game")]
    [Arguments("/overlays#sources", "Overlays", "require both Overlays and Guessing game")]
    [Arguments("/overlays#cues", "Cues", "Use Media library to upload or replace cue media")]
    [Arguments("/overlays#media", "Media library", "cannot be deleted; edit the cue first")]
    [Arguments("/automations/events", "Twitch events", "no Twitch subscription is created")]
    public void FeatureRoute_RendersOneButtonAndOpensRouteSpecificHelp(
        string path,
        string title,
        string distinctiveContent
    )
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
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
            "/automations/events",
            "/custom-commands/settings",
            "/guessing",
            "/guessing/settings",
            "/moments",
            "/overlays",
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
    public void OpenHelp_RouteChangeClosesThePanelAndSelectsTheNewRouteContent()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
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
        _ = context.Services.AddScoped<DashboardFragmentState>();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/alerts");

        var cut = context.Render<PageHelpButton>();

        cut.FindAll("button[aria-label='Page help']").ShouldBeEmpty();
    }

    [Test]
    public void ExistingMappedRoute_RetainsItsHelpButtonAndContent()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/");
        var cut = context.Render<PageHelpButton>();

        cut.Find("button[aria-label='Page help']").Click();

        cut.Find("h2").TextContent.ShouldBe("Home");
        cut.Markup.ShouldContain("Where to go next");
    }
}

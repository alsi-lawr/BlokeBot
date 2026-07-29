using BlokeBot.Core.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PageHelpButtonTests
{
    [Test]
    [Arguments("/twitch-operations/shoutouts", "Shoutouts", "same-channel cooldown")]
    [Arguments("/twitch-operations/polls", "Polls", "reusable template")]
    [Arguments("/twitch-operations/clips-markers", "Clips & markers", "Check outcome")]
    [Arguments(
        "/twitch-operations/channel-points",
        "Rewards & redemptions",
        "cancel it so Twitch refunds"
    )]
    [Arguments("/twitch-operations/predictions", "Predictions", "winning outcome")]
    public void NativeRoute_RendersOneButtonAndOpensRouteSpecificHelp(
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
        cut.Find("button[aria-label='Close help']").ShouldNotBeNull();
        cut.Find("h2").TextContent.ShouldBe(title);
        cut.Markup.ShouldContain(distinctiveContent);
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
        text.ShouldContain("viewer threshold");
        text.ShouldContain("within two minutes");
        text.ShouldContain("either a native Twitch shoutout or one chat message");
        text.ShouldContain("regular, pinned, or announcement");
        text.ShouldContain("does not switch mechanisms or automatically retry");
        text.ShouldContain("replaces the current pin");
        text.ShouldContain("does not restore the previous pin");
        text.ShouldContain("{twitch_handle}");
        text.ShouldContain("{display_name}");
        text.ShouldContain("{channel_url}");
        text.ShouldContain("{last_game|fallback}");
        text.ShouldContain("{stream_title|fallback}");
        text.ShouldContain("{viewer_count}");
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
}

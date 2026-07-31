using BlokeBot.Core.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class SegmentedTabsTests
{
    [Test]
    public void ActionTabs_ExposeActiveSemanticsAndSelectByKey()
    {
        using var context = new BunitContext();
        string? selected = null;
        var tabs = context.Render<SegmentedTabs>(parameters =>
            parameters
                .Add(value => value.AriaLabel, "Dashboard view")
                .Add(
                    value => value.Items,
                    [
                        new("live", "Live"),
                        new("history", "History"),
                        new("leaderboard", "Leaderboard"),
                    ]
                )
                .Add(value => value.ActiveKey, "history")
                .Add(
                    value => value.ActiveKeyChanged,
                    EventCallback.Factory.Create<string>(this, value => selected = value)
                )
        );

        tabs.Find("[role='tablist']").GetAttribute("aria-label").ShouldBe("Dashboard view");
        tabs.FindAll("[role='tab']").Count.ShouldBe(3);
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("History");
        tabs.FindAll("a").ShouldBeEmpty();

        tabs.FindAll("button").Single(button => button.TextContent.Trim() == "Leaderboard").Click();

        selected.ShouldBe("leaderboard");
    }

    [Test]
    public void RouteTabs_UseExactLinksAndFollowNavigation()
    {
        using var context = new BunitContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/overlays/cues");
        var tabs = context.Render<SegmentedTabs>(parameters =>
            parameters
                .Add(value => value.AriaLabel, "Overlays sections")
                .Add(
                    value => value.Items,
                    [
                        new("sources", "Sources", "overlays/sources"),
                        new("cues", "Cues", "overlays/cues"),
                        new("media", "Media", "overlays/media"),
                    ]
                )
        );

        tabs.FindAll("a")
            .Select(link => link.GetAttribute("href"))
            .ShouldBe(["overlays/sources", "overlays/cues", "overlays/media"]);
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Cues");

        navigation.NavigateTo("/overlays/media");

        tabs.WaitForAssertion(() =>
            tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Media")
        );
    }

    [Test]
    public void GuessingAndOverlays_UseTheSharedComponentWithoutDuplicateTabMarkup()
    {
        var root = RepositoryRoot();
        var guessing = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BlokeBot.Core",
                "Features",
                "Guessing",
                "Rounds",
                "GuessingDashboard.razor"
            )
        );
        var overlays = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BlokeBot.Core",
                "Features",
                "Overlays",
                "OverlaySectionTabs.razor"
            )
        );

        guessing.ShouldContain("<SegmentedTabs");
        overlays.ShouldContain("<SegmentedTabs");
        guessing.ShouldNotContain("segmented-motion__indicator");
        overlays.ShouldNotContain("btn-secondary");
        overlays.ShouldNotContain("btn-primary");
    }

    [Test]
    public void SharedPresentation_KeepsOneTabWidthOnDesktopAndDistributesAtPhoneWidth()
    {
        var styles = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "BlokeBot.Core",
                "Styles",
                "components",
                "segmented-controls.css"
            )
        );

        styles.ShouldContain(
            "grid-template-columns: repeat(var(--segmented-count), minmax(5.5rem, 7.5rem));"
        );
        styles.ShouldContain("justify-self: start;");
        styles.ShouldContain("width: max-content;");
        styles.ShouldContain(
            """
            .segmented-motion--shared .segmented-motion__tab {
                    align-items: center;
                    display: flex;
                    justify-content: center;
                    text-align: center;
                }
            """
        );
        styles.ShouldContain(
            "grid-template-columns: repeat(var(--segmented-count), minmax(0, 1fr));"
        );
        styles.ShouldContain("justify-self: stretch;");
        styles.ShouldContain("width: calc((100% - 0.5rem) / var(--segmented-count));");
        styles.ShouldContain("transform: translateX(calc(var(--segmented-active-index) * 100%));");
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}

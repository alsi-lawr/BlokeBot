using AngleSharp.Dom;
using BlokeBot.Site.Components;
using BlokeBot.Site.Content;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Site.Tests;

public sealed class SiteComponentTests
{
    private static readonly IReadOnlySet<string> _expectedMedia = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "/media/blokebot-banner.svg",
        "/media/channel-setup.png",
        "/media/custom-commands.png",
        "/media/dashboard-home.png",
        "/media/guessing-leaderboard.png",
        "/media/guessing-workflow.webp",
        "/media/points-settings.png",
    };

    [Test]
    public void EveryPublicRoute_RendersAccessibleStaticStructure()
    {
        foreach (var route in SiteRoutes.All)
        {
            using var context = new BunitContext();
            context.Services.GetRequiredService<NavigationManager>().NavigateTo(route);

            var rendered = context.Render<Routes>();

            rendered.Find("header").ShouldNotBeNull();
            rendered.Find("nav[aria-label='Main navigation']").ShouldNotBeNull();
            rendered.Find("main#main-content").ShouldNotBeNull();
            rendered.Find("footer").ShouldNotBeNull();
            rendered.Find("a.skip-link").GetAttribute("href").ShouldBe("#main-content");
            rendered.FindAll("h1").Count.ShouldBe(1, $"Route {route} must have one h1.");
            rendered.FindAll("form").ShouldBeEmpty();

            AssertHeadingOrder(rendered.FindAll("h1, h2, h3").ToArray(), route);

            foreach (var image in rendered.FindAll("img"))
            {
                image.GetAttribute("alt").ShouldNotBeNullOrWhiteSpace();
                image.Closest("figure")?.QuerySelector("figcaption").ShouldNotBeNull();
            }
        }
    }

    [Test]
    public void LandingPage_UsesAcceptedPositioningAndPrimaryActions()
    {
        using var context = new BunitContext();
        var rendered = context.Render<Routes>();

        rendered.Find("h1").TextContent.Trim().ShouldBe("Own your channel tools.");
        rendered
            .Find("p.lede")
            .TextContent.Trim()
            .ShouldBe(
                "Run commands, guessing games, points and giveaways from one dashboard built around your Twitch channel."
            );
        rendered
            .Find("p.availability")
            .TextContent.Trim()
            .ShouldBe("BlokeBot is free, open-source, easy to host, and available now.");

        var actions = rendered.FindAll(".action-row a");
        actions
            .Select(action => action.TextContent.Trim())
            .ShouldBe(["See how it works", "Read the user guide"]);
        actions.Select(action => action.GetAttribute("href")).ShouldBe(["/how-it-works", "/guide"]);
    }

    [Test]
    public void InternalLinksAndMedia_ResolveWithinThePublishedSite()
    {
        var internalLinks = new HashSet<string>(StringComparer.Ordinal);
        var mediaSources = new HashSet<string>(StringComparer.Ordinal);

        foreach (var route in SiteRoutes.All)
        {
            using var context = new BunitContext();
            context.Services.GetRequiredService<NavigationManager>().NavigateTo(route);
            var rendered = context.Render<Routes>();

            foreach (var link in rendered.FindAll("a[href]"))
            {
                var href = link.GetAttribute("href");
                if (href is not null && href.StartsWith("/", StringComparison.Ordinal))
                {
                    internalLinks.Add(href);
                }
            }

            foreach (var image in rendered.FindAll("img[src]"))
            {
                mediaSources.Add(image.GetAttribute("src")!);
            }
        }

        internalLinks.Except(SiteRoutes.All).ShouldBeEmpty();
        internalLinks.ShouldNotContain("/install");
        mediaSources.ShouldBe(_expectedMedia, ignoreOrder: true);

        foreach (var source in mediaSources)
        {
            File.Exists(
                    Path.Combine(
                        SiteTestPaths.SiteRoot,
                        "wwwroot",
                        source.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)
                    )
                )
                .ShouldBeTrue($"Media source {source} must exist in the site project.");
        }
    }

    [Test]
    public void ServerOwnerPage_LinksToTheExistingTechnicalWiki()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/server-owners");
        var rendered = context.Render<Routes>();

        rendered
            .Find("a[href^='https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide']")
            .TextContent.Trim()
            .ShouldBe("Open the technical Server Owner Guide");
    }

    private static void AssertHeadingOrder(IReadOnlyList<IElement> headings, string route)
    {
        var previousLevel = 0;

        foreach (var heading in headings)
        {
            var level = int.Parse(heading.TagName[1..]);
            if (previousLevel > 0)
            {
                level.ShouldBeLessThanOrEqualTo(
                    previousLevel + 1,
                    $"Route {route} skips a heading level at {heading.TextContent.Trim()}."
                );
            }

            previousLevel = level;
        }
    }
}

using System.Xml.Linq;
using BlokeBot.Site.Content;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Site.Tests;

public sealed class SiteBoundaryTests
{
    private static readonly IReadOnlyList<string> _expectedRoutes =
    [
        "/",
        "/how-it-works",
        "/install",
        "/guide",
        "/guide/getting-started",
        "/dashboard",
        "/channels",
        "/connect",
        "/tools",
        "/commands",
        "/guessing",
        "/points",
        "/giveaways",
        "/leaderboards",
        "/troubleshooting",
        "/moderators",
        "/server-owners",
    ];

    [Test]
    public void SiteProject_HasNoProjectDependenciesAndOnlyHostLoggingPackages()
    {
        var project = XDocument.Load(Path.Combine(SiteTestPaths.SiteRoot, "BlokeBot.Site.csproj"));

        project.Descendants("ProjectReference").ShouldBeEmpty();
        project
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ShouldBe(
                ["Serilog.AspNetCore", "Serilog.Settings.Configuration", "Serilog.Sinks.Console"],
                ignoreOrder: true
            );
        Directory.Exists(Path.Combine(SiteTestPaths.SiteRoot, "node_modules")).ShouldBeFalse();
        File.Exists(Path.Combine(SiteTestPaths.SiteRoot, "package.json")).ShouldBeFalse();
        Directory
            .EnumerateFiles(SiteTestPaths.SiteRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".js" or ".mjs" or ".ts" or ".tsx")
            .ShouldBeEmpty();
    }

    [Test]
    public void PublicRouteContract_IncludesOnlyTheAuthorisedInstallationRoute()
    {
        SiteRoutes.All.ShouldBe(_expectedRoutes);
        SiteRoutes.All.ShouldContain("/install");
        SiteRoutes.All.ShouldNotContain("/installation");
    }
}

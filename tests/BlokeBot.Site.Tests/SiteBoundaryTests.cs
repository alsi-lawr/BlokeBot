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

    private static readonly IReadOnlyList<string> _forbiddenSourceText =
    [
        "<form",
        "@rendermode",
        "AddInteractive",
        "AddAuthentication",
        "AddAuthorization",
        "AddCookie",
        "AddDbContext",
        "ConnectionString",
        "EntityFramework",
        "IJSRuntime",
        "MapBlazorHub",
        "Microsoft.AspNetCore.Authentication",
        "RequireAuthorization",
        "Sqlite",
        "UseAuthentication",
        "UseAuthorization",
        "UseCookiePolicy",
        "BlokeBot.Persistence",
        "BlokeBot.Twitch.Auth",
        "BlokeBot.Twitch.Runtime",
        "document.cookie",
        "GoogleAnalytics",
        "gtag(",
    ];

    private static readonly IReadOnlyList<string> _technicalInstructions =
    [
        "docker run",
        "dotnet run",
        "nix run",
        "systemctl",
        "BotUsername",
        "ClientSecret",
        "ConnectionStrings__",
        "TwitchBot__",
    ];

    [Test]
    public void SiteProject_HasNoProjectOrRuntimePackageDependencies()
    {
        var project = XDocument.Load(Path.Combine(SiteTestPaths.SiteRoot, "BlokeBot.Site.csproj"));

        project.Descendants("ProjectReference").ShouldBeEmpty();
        project.Descendants("PackageReference").ShouldBeEmpty();
        Directory.Exists(Path.Combine(SiteTestPaths.SiteRoot, "node_modules")).ShouldBeFalse();
        File.Exists(Path.Combine(SiteTestPaths.SiteRoot, "package.json")).ShouldBeFalse();
        Directory
            .EnumerateFiles(SiteTestPaths.SiteRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".js" or ".mjs" or ".ts" or ".tsx")
            .ShouldBeEmpty();
    }

    [Test]
    public void SiteSource_RemainsStaticStatelessAndOwnerFocused()
    {
        var sourceFiles = Directory
            .EnumerateFiles(SiteTestPaths.SiteRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".razor" or ".csproj" or ".html")
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        var source = string.Join('\n', sourceFiles.Select(File.ReadAllText));

        foreach (var forbidden in _forbiddenSourceText)
        {
            source.ShouldNotContain(forbidden, Case.Insensitive);
        }

        foreach (var instruction in _technicalInstructions)
        {
            source.ShouldNotContain(instruction, Case.Insensitive);
        }
    }

    [Test]
    public void PublicRouteContract_IsExactAndHasNoInstallationRoute()
    {
        SiteRoutes.All.ShouldBe(_expectedRoutes);
        SiteRoutes.All.ShouldNotContain("/install");
        SiteRoutes.All.ShouldNotContain("/installation");
    }
}

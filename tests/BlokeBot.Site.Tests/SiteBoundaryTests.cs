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
        "dotnet run",
        "systemctl",
        "BotUsername",
        "ClientSecret",
        "ConnectionStrings__",
        "TwitchBot__",
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
    public void PublicRouteContract_IncludesOnlyTheAuthorisedInstallationRoute()
    {
        SiteRoutes.All.ShouldBe(_expectedRoutes);
        SiteRoutes.All.ShouldContain("/install");
        SiteRoutes.All.ShouldNotContain("/installation");
    }

    [Test]
    public void InstallationCommands_AreConfinedToTheAuthorisedStaticPage()
    {
        var pages = Path.Combine(SiteTestPaths.SiteRoot, "Components", "Pages");
        var installPage = Path.Combine(pages, "Install.razor");
        var installSource = File.ReadAllText(installPage);
        var otherSource = string.Join(
            '\n',
            Directory
                .EnumerateFiles(pages, "*.razor", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(path, installPage, StringComparison.Ordinal))
                .Select(File.ReadAllText)
        );

        installSource.ShouldContain("nix run github:alsi-lawr/BlokeBot/v0.1.0#blokebot -- serve");
        installSource.ShouldNotContain("github:alsi-lawr/BlokeBot#blokebot");
        installSource.ShouldContain("docker run --rm -p 8080:8080");
        installSource.ShouldContain("docker.io/alsilawr/blokebot:0.1.0");
        installSource.ShouldContain("ghcr.io/alsi-lawr/blokebot:0.1.0");
        installSource.ShouldContain("blokebot-v0.1.0-osx-arm64.zip");
        installSource.ShouldContain("checksums_sha256.txt");
        installSource.ShouldNotContain("checksums.toml");
        otherSource.ShouldNotContain("docker run", Case.Insensitive);
        otherSource.ShouldNotContain("nix run", Case.Insensitive);
    }
}

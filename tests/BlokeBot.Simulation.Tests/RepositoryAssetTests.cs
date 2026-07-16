using System.Security.Cryptography;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Simulation.Tests;

public sealed class RepositoryAssetTests
{
    private static readonly IReadOnlyList<PromotedMedia> _promotedMedia =
    [
        new(
            "assets/simulation/output/laptop-light-channel-setup.png",
            "src/BlokeBot.Site/wwwroot/media/channel-setup.png",
            "f8bf1fb7dffabd09e307845b81a42410af8ab22bb3c650720f7c34aac391201a"
        ),
        new(
            "assets/simulation/output/laptop-light-custom-commands.png",
            "src/BlokeBot.Site/wwwroot/media/custom-commands.png",
            "1a77e01e30aab31fcd0c60af254bfde39a994f5f992ed366fdff10f11af004cd"
        ),
        new(
            "assets/simulation/output/laptop-light-home.png",
            "src/BlokeBot.Site/wwwroot/media/dashboard-home.png",
            "631e7f272ed7936620937ea18473f656374bfe191836b8d07697d55c91e88b0f"
        ),
        new(
            "assets/simulation/output/laptop-light-guessing-leaderboard.png",
            "src/BlokeBot.Site/wwwroot/media/guessing-leaderboard.png",
            "8eb23d195d9a53e767b30cae4136c7053abfa6ed39f794c6435fee9bad7b071f"
        ),
        new(
            "assets/simulation/animations/laptop-light-guessing-workflow.webp",
            "src/BlokeBot.Site/wwwroot/media/guessing-workflow.webp",
            "8c5e71e5e13ecc3bdb1822e85048acf8581444a6da540ce40783232f52ffcd6b"
        ),
        new(
            "assets/simulation/output/laptop-light-points-settings.png",
            "src/BlokeBot.Site/wwwroot/media/points-settings.png",
            "5d190399c729cd72736330ba763eca0fa4f368d3ca35f41c316df9d101d9d13b"
        ),
    ];

    [Test]
    public void CurrentSimulationManifests_MatchTheCompleteCorpus()
    {
        ValidateManifest("assets/simulation/output/SHA256SUMS", ".png", 44);
        ValidateManifest("assets/simulation/animations/SHA256SUMS", ".webp", 8);
    }

    [Test]
    public void ApprovedSiteMedia_IsPromotedByteForByteWithExactHashes()
    {
        foreach (var media in _promotedMedia)
        {
            var source = RepositoryPath(media.Source);
            var target = RepositoryPath(media.Target);

            Hash(source).ShouldBe(media.Sha256);
            Hash(target).ShouldBe(media.Sha256);
            File.ReadAllBytes(target).ShouldBe(File.ReadAllBytes(source));
        }
    }

    [Test]
    public void CoreCss_IsGeneratedBeforeStaticWebAssetsAndOnlyOutputPathIsIgnored()
    {
        var project = XDocument.Load(RepositoryPath("src/BlokeBot.Core/BlokeBot.Core.csproj"));
        var target = project
            .Descendants("Target")
            .Single(element => element.Attribute("Name")?.Value == "BuildTailwindCss");

        target.Attribute("BeforeTargets")?.Value.ShouldBe("PrepareForBuild");
        target.Attribute("DependsOnTargets")?.Value.ShouldBe("RestoreNodePackages");
        target
            .Descendants("Exec")
            .Single()
            .Attribute("Command")
            ?.Value.ShouldBe("npm run css:build");

        var ignoreLines = File.ReadAllLines(RepositoryPath(".gitignore"));
        ignoreLines.Count(line => line == "/src/BlokeBot.Core/wwwroot/app.css").ShouldBe(1);
        ignoreLines.ShouldNotContain("/src/BlokeBot.Core/wwwroot/");
    }

    [Test]
    public void CoreAndSiteProjectBoundaries_ExcludeForbiddenDependencies()
    {
        var core = XDocument.Load(RepositoryPath("src/BlokeBot.Core/BlokeBot.Core.csproj"));
        core.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .ShouldNotContain(package => package.StartsWith("Serilog", StringComparison.Ordinal));

        var site = XDocument.Load(RepositoryPath("src/BlokeBot.Site/BlokeBot.Site.csproj"));
        site.Descendants("ProjectReference").ShouldBeEmpty();
    }

    private static void ValidateManifest(string relativeManifest, string extension, int count)
    {
        var manifest = RepositoryPath(relativeManifest);
        var directory = Path.GetDirectoryName(manifest)!;
        var entries = File.ReadAllLines(manifest)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split("  ", 2, StringSplitOptions.None))
            .ToArray();
        var files = Directory.EnumerateFiles(directory, $"*{extension}").ToArray();

        entries.Length.ShouldBe(count);
        files.Length.ShouldBe(count);
        foreach (var entry in entries)
        {
            entry.Length.ShouldBe(2);
            var path = Path.Combine(directory, entry[1].TrimStart('.', '/', '\\'));
            Hash(path).ShouldBe(entry[0]);
        }
    }

    private static string Hash(string path)
    {
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static string RepositoryPath(string relativePath)
    {
        return Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)
        );
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed record PromotedMedia(string Source, string Target, string Sha256);
}

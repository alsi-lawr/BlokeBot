using BlokeBot.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class BlokeBotStatePathTests
{
    [Test]
    public void ExplicitAndDataDirectoryPaths_Resolving_UseFieldByFieldPrecedence()
    {
        var result = BlokeBotStatePathResolver.Resolve(
            new BlokeBotStatePathRequest(
                BlokeBotOperatingSystem.Linux,
                new("/home/alex", "/xdg", null),
                "/service/state",
                "/explicit/blokebot.sqlite",
                null
            )
        );

        var paths = result.ShouldBeOfType<BlokeBotStatePathResolution.Resolved>().Paths;
        paths.DatabasePath.ShouldBe("/explicit/blokebot.sqlite");
        paths.TokenCachePath.ShouldBe("/service/state/twitch.tokens.json");
    }

    [Test]
    public void BothExplicitPaths_Resolving_DoesNotRequirePlatformDirectories()
    {
        var result = BlokeBotStatePathResolver.Resolve(
            new BlokeBotStatePathRequest(
                BlokeBotOperatingSystem.Windows,
                new(null, null, null),
                null,
                @"D:\blokebot\database.db",
                @"E:\blokebot\tokens.json"
            )
        );

        var paths = result.ShouldBeOfType<BlokeBotStatePathResolution.Resolved>().Paths;
        paths.DatabasePath.ShouldBe(@"D:\blokebot\database.db");
        paths.TokenCachePath.ShouldBe(@"E:\blokebot\tokens.json");
    }

    [Test]
    public void MissingPlatformDirectory_Resolving_ReturnsActionableFailure()
    {
        var result = BlokeBotStatePathResolver.Resolve(
            new BlokeBotStatePathRequest(
                BlokeBotOperatingSystem.Linux,
                new(null, null, null),
                null,
                null,
                null
            )
        );

        var failure = result.ShouldBeOfType<BlokeBotStatePathResolution.Failed>();
        failure.Message.ShouldContain("--data-dir PATH");
    }

    [Test]
    public void UnwritableStateDirectory_Preparing_ReturnsActionableFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"blokebot-path-test-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        var blockingFile = Path.Combine(root, "not-a-directory");
        File.WriteAllText(blockingFile, "blocked");
        try
        {
            var result = BlokeBotStatePathPreparer.Prepare(
                new BlokeBotStatePaths(
                    Path.Combine(blockingFile, "blokebot.db"),
                    Path.Combine(blockingFile, "twitch.tokens.json")
                )
            );

            var failure = result.ShouldBeOfType<BlokeBotStatePathPreparation.Failed>();
            failure.Message.ShouldContain("could not prepare its state files");
            failure.Message.ShouldContain("--data-dir PATH");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void WildcardBoundAddress_Reporting_ReturnsUsableLocalUrl()
    {
        var addresses = new ServerAddressesFeature();
        addresses.Addresses.Add("http://[::]:43127");

        BlokeBotServerUrlPolicy.LocalUrl(addresses).ShouldBe("http://127.0.0.1:43127");
    }
}

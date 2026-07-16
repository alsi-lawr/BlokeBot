using BlokeBot.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class BlokeBotStatePathTests
{
    [Test]
    public void LinuxWithXdgStateHome_Resolving_UsesLowercaseXdgApplicationDirectory()
    {
        var paths = Resolve(
            BlokeBotOperatingSystem.Linux,
            new("/home/alex", "/var/lib/state", null)
        );

        paths.DatabasePath.ShouldBe("/var/lib/state/blokebot/blokebot.db");
        paths.TokenCachePath.ShouldBe("/var/lib/state/blokebot/twitch.tokens.json");
    }

    [Test]
    public void LinuxWithoutXdgStateHome_Resolving_UsesHomeStateFallback()
    {
        var paths = Resolve(BlokeBotOperatingSystem.Linux, new("/home/alex", null, null));

        paths.DatabasePath.ShouldBe("/home/alex/.local/state/blokebot/blokebot.db");
        paths.TokenCachePath.ShouldBe("/home/alex/.local/state/blokebot/twitch.tokens.json");
    }

    [Test]
    public void MacOs_Resolving_UsesApplicationSupportDirectory()
    {
        var paths = Resolve(BlokeBotOperatingSystem.MacOS, new("/Users/alex", null, null));

        paths.DatabasePath.ShouldBe("/Users/alex/Library/Application Support/BlokeBot/blokebot.db");
        paths.TokenCachePath.ShouldBe(
            "/Users/alex/Library/Application Support/BlokeBot/twitch.tokens.json"
        );
    }

    [Test]
    public void Windows_Resolving_UsesLocalApplicationDataDirectory()
    {
        var paths = Resolve(
            BlokeBotOperatingSystem.Windows,
            new(null, null, @"C:\Users\Alex\AppData\Local")
        );

        paths.DatabasePath.ShouldBe(@"C:\Users\Alex\AppData\Local\BlokeBot\blokebot.db");
        paths.TokenCachePath.ShouldBe(@"C:\Users\Alex\AppData\Local\BlokeBot\twitch.tokens.json");
    }

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
        Directory.CreateDirectory(root);
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
    public void ExplicitUrlConfiguration_Detecting_OnlyAppliesDefaultWhenAbsent()
    {
        var empty = Configuration([]);
        var urls = Configuration(new Dictionary<string, string?> { ["urls"] = "http://*:9000" });
        var ports = Configuration(new Dictionary<string, string?> { ["HTTP_PORTS"] = "9001" });
        var kestrel = Configuration(
            new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:9002",
            }
        );

        BlokeBotServerUrlPolicy.HasExplicitConfiguration(empty).ShouldBeFalse();
        BlokeBotServerUrlPolicy.HasExplicitConfiguration(urls).ShouldBeTrue();
        BlokeBotServerUrlPolicy.HasExplicitConfiguration(ports).ShouldBeTrue();
        BlokeBotServerUrlPolicy.HasExplicitConfiguration(kestrel).ShouldBeTrue();
        BlokeBotServerUrlPolicy.DefaultUrl.ShouldBe("http://127.0.0.1:8080");
    }

    [Test]
    public void WildcardBoundAddress_Reporting_ReturnsUsableLocalUrl()
    {
        var addresses = new ServerAddressesFeature();
        addresses.Addresses.Add("http://[::]:43127");

        BlokeBotServerUrlPolicy.LocalUrl(addresses).ShouldBe("http://127.0.0.1:43127");
    }

    [Test]
    public void MissingTwitchConfiguration_Inspecting_ReportsActionableEnvironmentFields()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["TwitchBot:Identity:RedirectUri"] = "http://127.0.0.1/oauth/callback",
            }
        );

        var missing = BlokeBotTwitchConfiguration.MissingFields(configuration);
        var guidance = BlokeBotTwitchConfiguration.OfflineGuidance(missing);

        missing
            .Select(field => field.EnvironmentKey)
            .ShouldBe([
                "TwitchBot__Identity__BotUsername",
                "TwitchBot__Identity__ClientId",
                "TwitchBot__Identity__ClientSecret",
            ]);
        guidance.ShouldContain("Twitch features are offline");
        guidance.ShouldContain("restart blokebot");
    }

    private static BlokeBotStatePaths Resolve(
        BlokeBotOperatingSystem operatingSystem,
        BlokeBotPlatformEnvironment environment
    )
    {
        return BlokeBotStatePathResolver
            .Resolve(new(operatingSystem, environment, null, null, null))
            .ShouldBeOfType<BlokeBotStatePathResolution.Resolved>()
            .Paths;
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}

using BlokeBot.Cli;
using BlokeBot.Core.Hosting;
using BlokeBot.Hosting;
using BlokeBot.Twitch.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

[NotInParallel]
public sealed class BlokeBotHostTests
{
    [Test]
    public async Task HostComposition_UsesPackagedContentRootAndOfflineWiring()
    {
        var dataDirectory = TemporaryDirectory();
        try
        {
            await using var composition = BlokeBotHost.Create(
                new BlokeBotServeOptions(null, null, dataDirectory, null)
            );

            composition.App.Environment.ContentRootPath.ShouldBe(AppContext.BaseDirectory);
            composition.Twitch.Mode.ShouldBe(BlokeBotRuntimeMode.Offline);
            composition.Twitch.MissingEnvironmentKeys.ShouldContain(
                "TwitchBot__Identity__ClientSecret"
            );
            composition.Twitch.OfflineGuidance().ShouldContain("Twitch features are offline");
            composition
                .App.Services.GetRequiredService<IBotRuntimeStatusAccessor>()
                .GetType()
                .Name.ShouldBe("OfflineBotStatusAccessor");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Test]
    public async Task CompleteTwitchConfiguration_SelectsOnlineCoreWiring()
    {
        var dataDirectory = TemporaryDirectory();
        var configurationPath = Path.Combine(dataDirectory, "online.json");
        await File.WriteAllTextAsync(
            configurationPath,
            """
            {
              "TwitchBot": {
                "Identity": {
                  "BotUsername": "configured-bot",
                  "ClientId": "configured-client",
                  "ClientSecret": "configured-secret",
                  "RedirectUri": "http://127.0.0.1/oauth/callback"
                }
              }
            }
            """
        );

        try
        {
            await using var composition = BlokeBotHost.Create(
                new BlokeBotServeOptions(null, null, dataDirectory, configurationPath)
            );

            composition.Twitch.Mode.ShouldBe(BlokeBotRuntimeMode.Online);
            composition.Twitch.MissingEnvironmentKeys.ShouldBeEmpty();
            composition
                .App.Services.GetRequiredService<IBotRuntimeStatusAccessor>()
                .GetType()
                .Name.ShouldBe("BotRuntimeStatusStore");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Test]
    public void Configuration_PrecedenceAndRelativeConfigPath_AreExplicit()
    {
        var root = TemporaryDirectory();
        var relativePath = Path.Combine("config", "operator.json");
        var expectedPath = Path.Combine(root, relativePath);

        BlokeBotHost.ResolveConfigurationPath(relativePath, root).ShouldBe(expectedPath);
        BlokeBotHost.ResolveConfigurationPath(expectedPath, "/ignored").ShouldBe(expectedPath);
        BlokeBotServerUrlPolicy.ExplicitUrl("0.0.0.0", 9191).ShouldBe("http://0.0.0.0:9191");

        Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task OptionalConfigThenEnvironmentThenFlags_ApplyInRequiredOrder()
    {
        var environmentKey = "BlokeBotTest__Precedence";
        var originalValue = Environment.GetEnvironmentVariable(environmentKey);
        var root = TemporaryDirectory();
        var configurationPath = Path.Combine(root, "operator.json");
        await File.WriteAllTextAsync(
            configurationPath,
            """
            {
              "urls": "http://127.0.0.1:8181",
              "BlokeBotTest": {
                "Precedence": "optional-config"
              }
            }
            """
        );
        Environment.SetEnvironmentVariable(environmentKey, "environment");

        try
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = [],
                    ApplicationName = typeof(BlokeBotHost).Assembly.GetName().Name,
                    ContentRootPath = AppContext.BaseDirectory,
                }
            );
            BlokeBotHost.Configure(
                builder,
                new BlokeBotServeOptions("0.0.0.0", 9191, null, configurationPath)
            );

            builder.Configuration["BlokeBotTest:Precedence"].ShouldBe("environment");
            builder.Configuration["urls"].ShouldBe("http://0.0.0.0:9191");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, originalValue);
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void TwitchSelection_ReportsOnlyMissingHostConfigurationKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TwitchBot:Identity:RedirectUri"] = "http://127.0.0.1/oauth/callback",
                }
            )
            .Build();

        var selection = BlokeBotTwitchModeSelection.FromConfiguration(configuration);

        selection.Mode.ShouldBe(BlokeBotRuntimeMode.Offline);
        selection.MissingEnvironmentKeys.ShouldBe([
            "TwitchBot__Identity__BotUsername",
            "TwitchBot__Identity__ClientId",
            "TwitchBot__Identity__ClientSecret",
        ]);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blokebot-host-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

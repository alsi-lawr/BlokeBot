using System.Text.Json;
using BlokeBot.Cli;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Hosting;
using BlokeBot.Hosting;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;

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
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Test]
    public async Task HostComposition_StagesMarketplaceStateBesideDatabaseNotPluginPrivateData()
    {
        var dataDirectory = TemporaryDirectory();
        try
        {
            await using var composition = BlokeBotHost.Create(
                new BlokeBotServeOptions(null, null, dataDirectory, null)
            );

            var options =
                composition.App.Services.GetRequiredService<PluginMarketplaceStorageOptions>();
            options.PackageStateRoot.ShouldBe(Path.Combine(dataDirectory, "plugin-packages"));
            options.PluginPrivateStateRoot.ShouldBe(Path.Combine(dataDirectory, "plugins"));
            options.PackageStateRoot.ShouldNotStartWith(options.PluginPrivateStateRoot);
            _ = composition.App.Services.GetRequiredService<PluginMarketplaceApplicationService>();
            composition
                .App.Services.GetRequiredService<IPluginLifecyclePackageResolver>()
                .GetType()
                .Name.ShouldBe("MarketplacePluginLifecyclePackageResolver");
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
                },
                "EventSubWebhook": {
                  "CallbackUri": "https://bot.blokebot.com/eventsub/twitch",
                  "Secret": "configured-webhook-secret"
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
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Test]
    public async Task StartedHost_ReportsBoundedLiveAndDatabaseReadinessWithoutAuthentication()
    {
        var dataDirectory = TemporaryDirectory();
        var composition = BlokeBotHost.Create(
            new BlokeBotServeOptions("127.0.0.1", 0, dataDirectory, null)
        );
        try
        {
            await BlokeBotDatabaseStartup.InitializeAsync(composition.App, CancellationToken.None);
            await composition.App.StartAsync();
            var address = composition
                .App.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var liveResponse = await client.GetAsync("/health/live");
            _ = liveResponse.EnsureSuccessStatusCode();
            liveResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var liveBody = await liveResponse.Content.ReadAsStringAsync();
            using var live = JsonDocument.Parse(liveBody);
            live.RootElement.GetProperty("status").GetString().ShouldBe("live");

            using var readyResponse = await client.GetAsync("/health/ready");
            _ = readyResponse.EnsureSuccessStatusCode();
            readyResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var readyBody = await readyResponse.Content.ReadAsStringAsync();
            using var ready = JsonDocument.Parse(readyBody);
            ready.RootElement.GetProperty("status").GetString().ShouldBe("ready");
            var database = ready.RootElement.GetProperty("database");
            database.GetProperty("provider").GetString().ShouldBe("Sqlite");
            database.GetProperty("category").GetString().ShouldBe("ready");
            readyBody.ShouldNotContain(dataDirectory);
        }
        finally
        {
            await composition.DisposeAsync();
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
                new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory }
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
        _ = Directory.CreateDirectory(path);
        return path;
    }
}

using System.Net;
using System.Text.Json;
using BlokeBot.Cli;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Hosting;
using BlokeBot.Hosting;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
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
    public async Task FailedStartup_DisposalPreservesTheOriginalDependencyFailure()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(_ =>
            throw new InvalidOperationException("startup-dependency-sentinel")
        );
        var composition = new BlokeBotHostComposition(
            builder.Build(),
            new(BlokeBotRuntimeMode.Offline, []),
            new("unused-state", "unused.db", "unused.tokens")
        );
        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var owned = composition;
            await owned.App.StartAsync();
        });
        failure.Message.ShouldBe("startup-dependency-sentinel");
    }

    [Test]
    [Arguments("")]
    [Arguments("&start=false")]
    public Task PublicSiteDashboardNavigation_ReachesReadOnlyLoginWithoutCookies(
        string startQuery
    ) =>
        WithOfflineWebAuthHostAsync(async client =>
        {
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-site");
            using var dashboard = await client.GetAsync("/");

            dashboard.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            dashboard.Headers.Contains("Set-Cookie").ShouldBeFalse();
            var login = dashboard.Headers.Location.ShouldNotBeNull();
            login.AbsolutePath.ShouldBe("/auth/login");
            QueryHelpers.ParseQuery(login.Query)["ReturnUrl"].ToString().ShouldBe("/");

            using var response = await client.GetAsync(login + startQuery);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
            (await response.Content.ReadAsStringAsync()).ShouldContain(
                "href=\"/auth/login?start=true\""
            );
            response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        });

    [Test]
    [Arguments("same-site")]
    [Arguments("cross-site")]
    public Task AuthCookieWrites_RejectUntrustedStartsAndSessionsButRetainNonceCallbacks(
        string site
    ) =>
        WithOfflineWebAuthHostAsync(async client =>
        {
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", site);
            var dashboardOrigin = client.BaseAddress.ShouldNotBeNull();
            foreach (
                var path in new[]
                {
                    "/auth/login?start=true&returnUrl=%2F",
                    "/AUTH/LOGIN/?START=TrUe",
                    "/auth/logout",
                    "/auth/twitch/callback?error=access_denied&state=missing-nonce",
                }
            )
            {
                using var rejected = await client.GetAsync(path);

                rejected.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
                rejected.Headers.CacheControl?.NoStore.ShouldBe(true);
                rejected.Headers.Contains("Set-Cookie").ShouldBeFalse(path);
            }

            foreach (
                var query in new[]
                {
                    "start=false&start=true",
                    "start=true&start=false",
                    "start=true&start=true",
                }
            )
            {
                using var ambiguous = await client.GetAsync($"/auth/login?{query}");

                ambiguous.Headers.Contains("Set-Cookie").ShouldBeFalse(query);
                if (ambiguous.Headers.Location is { } location)
                {
                    new Uri(dashboardOrigin, location)
                        .GetLeftPart(UriPartial.Authority)
                        .ShouldBe(dashboardOrigin.GetLeftPart(UriPartial.Authority), query);
                }
            }

            using var startRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/auth/login?start=TrUe&returnUrl=%2F"
            );
            startRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
            startRequest.Headers.Referrer = client.BaseAddress;
            using var started = await client.SendAsync(startRequest);

            started.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            var authorization = started.Headers.Location.ShouldNotBeNull();
            var state = QueryHelpers.ParseQuery(authorization.Query)["state"].ToString();
            state.ShouldNotBeNullOrWhiteSpace();
            started
                .Headers.GetValues("Set-Cookie")
                .ShouldContain(cookie => cookie.StartsWith($"BlokeBot.AuthState={state};"));
            started
                .Headers.GetValues("Set-Cookie")
                .ShouldContain(cookie => cookie.StartsWith("BlokeBot.AuthReturnUrl="));

            using var mismatchedRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/auth/twitch/callback?error=access_denied&state=mismatched-nonce"
            );
            mismatchedRequest.Headers.Add("Cookie", $"BlokeBot.AuthState={state}");
            using var mismatched = await client.SendAsync(mismatchedRequest);

            mismatched.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            mismatched.Headers.Contains("Set-Cookie").ShouldBeFalse();

            using var callbackRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/auth/twitch/callback?error=access_denied&state={state}"
            );
            callbackRequest.Headers.Add("Cookie", $"BlokeBot.AuthState={state}");
            using var callback = await client.SendAsync(callbackRequest);

            callback.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            callback
                .Headers.GetValues("Set-Cookie")
                .ShouldContain(cookie => cookie.StartsWith("BlokeBot.AuthState=;"));
            callback
                .Headers.GetValues("Set-Cookie")
                .ShouldContain(cookie => cookie.StartsWith("BlokeBot.AuthReturnUrl=;"));

            using var logoutRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/logout");
            logoutRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
            logoutRequest.Headers.Referrer = client.BaseAddress;
            using var logout = await client.SendAsync(logoutRequest);

            logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            logout
                .Headers.GetValues("Set-Cookie")
                .ShouldContain(cookie => cookie.StartsWith("BlokeBot.Auth=;"));
        });

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

    private static async Task WithOfflineWebAuthHostAsync(Func<HttpClient, Task> journey)
    {
        var dataDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-workspace",
            "dashboard-login-375",
            Guid.NewGuid().ToString("N")
        );
        _ = Directory.CreateDirectory(dataDirectory);
        var configurationPath = Path.Combine(dataDirectory, "web-auth.json");
        await File.WriteAllTextAsync(
            configurationPath,
            """
            {
              "TwitchBot": {
                "Identity": {
                  "BotUsername": "",
                  "ClientId": "offline-web-client",
                  "ClientSecret": "offline-web-secret"
                }
              }
            }
            """
        );
        try
        {
            await using var composition = BlokeBotHost.Create(
                new BlokeBotServeOptions("127.0.0.1", 0, dataDirectory, configurationPath)
            );
            composition.Twitch.Mode.ShouldBe(BlokeBotRuntimeMode.Offline);
            composition
                .App.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
                .Get(GitHubPluginMarketplaceRepositoryTransport.ClientName)
                .HttpMessageHandlerBuilderActions.Add(builder =>
                    builder.PrimaryHandler = new OfflineMarketplaceHandler()
                );
            await BlokeBotDatabaseStartup.InitializeAsync(composition.App, CancellationToken.None);
            await composition.App.StartAsync();
            var address = composition
                .App.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Referrer = new Uri("https://www.blokebot.com/");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            await journey(client);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private sealed class OfflineMarketplaceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

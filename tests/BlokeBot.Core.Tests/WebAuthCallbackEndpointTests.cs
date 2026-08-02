using System.Runtime.CompilerServices;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class WebAuthCallbackEndpointTests
{
    [Test]
    public async Task OAuthErrorQuery_ReturnsActionableRedactedFailurePage()
    {
        const string Sentinel = "oauth-query-sentinel-secret";
        await using var host = await CallbackHost.StartAsync();

        using var response = await host.Client.GetAsync($"/auth/twitch/callback?error={Sentinel}");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch is temporarily unavailable");
        page.ShouldNotContain("Connection link expired");
        page.ShouldContain("role=\"alert\"");
        page.ShouldContain("href=\"/auth/login?start=true\">Try again</a>");
        page.ShouldContain("href=\"/auth/login\">Return to sign in</a>");
        page.ShouldContain("Close window</button>");
        page.ShouldNotContain(Sentinel);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    [Test]
    public async Task CallbackQueryValues_AreAbsentFromEveryCapturedLogCategory()
    {
        const string Error = "web-error-sentinel";
        const string Code = "web-code-sentinel";
        const string State = "web-state-sentinel";
        using var logs = new CallbackLogCapture();
        await using var host = await CallbackHost.StartAsync(logs);

        using var response = await host.Client.GetAsync(
            $"/auth/twitch/callback?error={Error}&code={Code}&state={State}"
        );

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        AssertRedacted(logs, Error, Code, State);
    }

    private sealed class CallbackHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<CallbackHost> StartAsync(CallbackLogCapture? logs = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddLogging();
            BlokeBotLogging.Configure(builder.Logging);
            if (logs is not null)
            {
                builder.Logging.ClearProviders();
                builder.Logging.AddProvider(logs);
            }
            builder.Services.AddSingleton(Uninitialized<WebAuthService>());
            builder.Services.AddSingleton(Uninitialized<AuthSessionService>());
            builder.Services.AddSingleton(Uninitialized<HostModAccessService>());
            builder.Services.AddSingleton(Uninitialized<HostedChannelDirectoryService>());

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapAuthEndpoints();
            await app.StartAsync();

            var server = app.Services.GetRequiredService<IServer>();
            var address =
                server.Features.Get<IServerAddressesFeature>()?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException(
                    "The callback test host did not publish an address."
                );
            return new(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }

        private static TService Uninitialized<TService>()
            where TService : class =>
            (TService)RuntimeHelpers.GetUninitializedObject(typeof(TService));
    }

    private static void AssertRedacted(CallbackLogCapture logs, params string[] sentinels)
    {
        logs.Entries.ShouldNotBeEmpty();
        foreach (var entry in logs.Entries)
        {
            foreach (var sentinel in sentinels)
            {
                entry.Message.ShouldNotContain(sentinel);
                entry
                    .Properties.Values.Any(value =>
                        value is not null
                        && value.ToString()!.Contains(sentinel, StringComparison.Ordinal)
                    )
                    .ShouldBeFalse();
            }
        }
    }
}

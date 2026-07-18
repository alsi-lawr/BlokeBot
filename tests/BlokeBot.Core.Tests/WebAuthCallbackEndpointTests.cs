using System.Runtime.CompilerServices;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostConfig.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
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
        page.ShouldContain("Twitch sign-in failed");
        page.ShouldContain("role=\"alert\"");
        page.ShouldContain("href=\"/auth/login?start=true\">Try again</a>");
        page.ShouldContain("href=\"/auth/login\">Return to sign in</a>");
        page.ShouldContain("Close window</button>");
        page.ShouldNotContain(Sentinel);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    private sealed class CallbackHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<CallbackHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddLogging();
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
            where TService : class
        {
            return (TService)RuntimeHelpers.GetUninitializedObject(typeof(TService));
        }
    }
}

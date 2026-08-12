using System.Net;
using BlokeBot.Core.Components;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DeletedShoutoutRouteTests
{
    [Test]
    public async Task ShoutoutsRoute_IsNotServedWhileTheSurvivingNativeRoutesStillAre()
    {
        await using var host = await RouteHost.StartAsync();

        using var deleted = await host.Client.GetAsync("/twitch-operations/shoutouts");
        using var surviving = await host.Client.GetAsync("/twitch-operations/polls");
        using var merged = await host.Client.GetAsync("/raid-collaboration");

        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        deleted.Headers.Location.ShouldBeNull();
        surviving.StatusCode.ShouldBe(HttpStatusCode.Found);
        merged.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private sealed class RouteHost(
        WebApplication app,
        HttpClient client,
        SqliteBlokeBotDbFactory database
    ) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<RouteHost> StartAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(App).Assembly.GetName().Name,
                    ContentRootPath = AppContext.BaseDirectory,
                    EnvironmentName = Environments.Production,
                }
            );
            _ = builder.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
            _ = builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            _ = builder.AddBlokeBotCore(BlokeBotRuntimeMode.Offline);
            _ = builder.Services.RemoveAll<IHostedService>();

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            _ = app.UseAntiforgery();
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();
            _ = app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
            await app.StartAsync();

            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException(
                    "The route test host did not publish an address."
                );
            return new(
                app,
                new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri(address),
                },
                database
            );
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}

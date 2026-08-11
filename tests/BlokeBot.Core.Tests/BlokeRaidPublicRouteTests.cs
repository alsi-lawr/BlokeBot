using System.Net;
using BlokeBot.Core.Components;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
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

public sealed class BlokeRaidPublicRouteTests
{
    [Test]
    public async Task AnonymousRequest_FeatureOffRendersUnavailablePublicState()
    {
        await using var host = await PublicRouteHost.StartAsync();

        using var response = await host.Client.GetAsync("/raid/alpha");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("data-public-raid-unavailable");
        response.Headers.Location.ShouldBeNull();
    }

    private sealed class PublicRouteHost(
        WebApplication app,
        HttpClient client,
        SqliteBlokeBotDbFactory database
    ) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<PublicRouteHost> StartAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            await SeedDisabledHostAsync(database);
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
            _ = app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .RequireAuthorization();
            await app.StartAsync();

            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException(
                    "The public-route test host did not publish an address."
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

        private static async Task SeedDisabledHostAsync(SqliteBlokeBotDbFactory database)
        {
            await using var db = await database.CreateDbContextAsync();
            _ = db.Hosts.Add(
                new BotHost
                {
                    TwitchUserId = "alpha-id",
                    Login = "alpha",
                    DisplayName = "Alpha",
                    EnabledFeatures = HostFeatureFlags.None,
                    CreatedAtUtc = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
                }
            );
            _ = await db.SaveChangesAsync();
        }
    }
}

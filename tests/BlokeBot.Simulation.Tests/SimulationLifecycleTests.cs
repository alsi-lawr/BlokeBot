using System.Net;
using BlokeBot.Persistence;
using BlokeBot.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Simulation.Tests;

[NotInParallel]
public sealed class SimulationLifecycleTests
{
    [Test]
    public async Task ApplicationEnvironment_RemainsSimulationWhenArgumentsConflict()
    {
        await using var app = SimulationApplication.Build(["--environment", "Development"]);
        try
        {
            app.Environment.EnvironmentName.ShouldBe(SimulationMode.EnvironmentName);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task SharedMemoryDatabase_PersistsAcrossContextsWhileKeeperLives()
    {
        await using var app = SimulationApplication.Build(["--environment", "Simulation"]);
        try
        {
            await app.InitializeSimulationAsync(CancellationToken.None);
            var keeper = app.Services.GetRequiredService<SimulationDatabaseKeeper>();
            var factory = app.Services.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();

            await using var first = await factory.CreateDbContextAsync();
            var firstHostCount = await first.Hosts.CountAsync();
            await using var second = await factory.CreateDbContextAsync();
            var secondHostCount = await second.Hosts.CountAsync();

            keeper.IsOpen.ShouldBeTrue();
            firstHostCount.ShouldBeGreaterThan(0);
            secondHostCount.ShouldBe(firstHostCount);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task FixtureSeeding_IsIdempotent()
    {
        await using var app = SimulationApplication.Build(["--environment", "Simulation"]);
        try
        {
            await app.InitializeSimulationAsync(CancellationToken.None);
            var seeder = app.Services.GetRequiredService<SimulationFixtureSeeder>();
            var before = await CountsAsync(app.Services);

            await seeder.SeedAsync(CancellationToken.None);
            var after = await CountsAsync(app.Services);

            after.ShouldBe(before);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task ReadyAndLoginEndpoints_AreAnonymousAndLoginSetsSessionCookie()
    {
        await using var app = SimulationApplication.Build(["--environment", "Simulation"]);
        try
        {
            app.Urls.Add("http://127.0.0.1:0");
            await app.InitializeSimulationAsync(CancellationToken.None);
            await app.StartAsync();

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(ListeningAddress(app)),
            };
            using var ready = await client.GetAsync("/simulation/ready");
            using var login = await client.GetAsync(
                "/simulation/login?view=points-settings&theme=dark"
            );

            ready.StatusCode.ShouldBe(HttpStatusCode.OK);
            login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            login.Headers.Location?.OriginalString.ShouldBe(
                "/points/settings?simulationTheme=dark"
            );
            login.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
            cookies.ShouldNotBeNull();
            cookies.ShouldContain(cookie =>
                cookie.Contains("BlokeBot.Auth=", StringComparison.Ordinal)
            );
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task HostDisposal_ClosesKeeperConnectionCleanly()
    {
        var app = SimulationApplication.Build(["--environment", "Simulation"]);
        await app.InitializeSimulationAsync(CancellationToken.None);
        var keeper = app.Services.GetRequiredService<SimulationDatabaseKeeper>();
        keeper.IsOpen.ShouldBeTrue();

        await app.DisposeAsync();
        await Log.CloseAndFlushAsync();

        keeper.IsOpen.ShouldBeFalse();
    }

    private static async Task<SimulationCounts> CountsAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return new SimulationCounts(
            await db.Hosts.CountAsync(),
            await db.Rounds.CountAsync(),
            await db.PointBalances.CountAsync(),
            await db.CustomCommands.CountAsync(),
            await db.DurableAlerts.CountAsync()
        );
    }

    private static string ListeningAddress(WebApplication app)
    {
        return app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?.Addresses.Single()
            ?? throw new InvalidOperationException(
                "Simulation did not report a listening address."
            );
    }

    private sealed record SimulationCounts(
        int Hosts,
        int Rounds,
        int PointBalances,
        int CustomCommands,
        int DurableAlerts
    );
}

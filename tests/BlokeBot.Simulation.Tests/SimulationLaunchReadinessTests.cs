using System.Net;
using BlokeBot.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Simulation.Tests;

[NotInParallel]
public sealed class SimulationLaunchReadinessTests
{
    [Test]
    public async Task ReadyDashboard_OnlyBecomesReadyAfterNormalStartupPhases()
    {
        Should.Throw<InvalidOperationException>(() =>
            SimulationMode.SelectScenario(["--simulation-scenario", "unknown"])
        );

        await using var simulation = await SimulationApplication.BuildAsync(
            ["--urls=http://127.0.0.1:5080"],
            CancellationToken.None
        );
        await simulation.App.InitializeSimulationAsync(CancellationToken.None);
        await simulation.App.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080") };

        (await client.GetAsync("/simulation/ready")).StatusCode.ShouldBe(
            HttpStatusCode.ServiceUnavailable
        );
        await simulation
            .App.Services.GetRequiredService<SimulationStartupCoordinator>()
            .BootstrapAsync(simulation.App, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            using var ready = await client.GetAsync("/simulation/ready", timeout.Token);
            if (ready.StatusCode == HttpStatusCode.OK)
            {
                var projection = await ready.Content.ReadAsStringAsync(timeout.Token);
                projection.ShouldContain("\"scenario\":\"ready-dashboard\"");
                projection.ShouldContain("\"ready\":true");
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }
}

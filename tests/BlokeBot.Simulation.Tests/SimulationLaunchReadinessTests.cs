using System.Net;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Simulation;
using Microsoft.EntityFrameworkCore;
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

    [Test]
    public async Task ReadyDashboard_MomentCaptureUsesProductionLivenessAndProviderPath()
    {
        await using var simulation = await SimulationApplication.BuildAsync(
            ["--urls=http://127.0.0.1:5081"],
            CancellationToken.None
        );
        await simulation.App.InitializeSimulationAsync(CancellationToken.None);
        await simulation.App.StartAsync();
        await simulation
            .App.Services.GetRequiredService<SimulationStartupCoordinator>()
            .BootstrapAsync(simulation.App, CancellationToken.None);

        var liveness = await simulation
            .App.Services.GetRequiredService<IHostStreamLivenessProvider>()
            .GetStreamLiveness("samplechannel")
            .RunAsync(CancellationToken.None);
        var live = liveness.ShouldBeOfType<HostStreamLivenessOutcome.Live>();
        var database = simulation.App.Services.GetRequiredService<
            IDbContextFactory<BlokeBotDbContext>
        >();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            hostId = await db.Hosts.Select(value => value.Id).SingleAsync();
        }

        var capture = await simulation
            .App.Services.GetRequiredService<MomentHubService>()
            .CaptureAsync(
                hostId,
                new CaptureMomentCommand(
                    live.StreamId,
                    new MomentViewerIdentity("nightowl", "viewer-1000"),
                    "Simulation moment"
                ),
                CancellationToken.None
            );

        var captured = capture.ShouldBeOfType<MomentResult<MomentView>.Succeeded>().Value;
        captured.State.ShouldBe(MomentCandidateState.ClipReady);
        captured.ProviderUrl.ShouldStartWith("https://clips.twitch.tv/fake-clip-");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentCandidates.CountAsync()).ShouldBe(1);
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
        (await verify.MomentCandidates.SingleAsync()).TwitchClipId.ShouldNotBeNull();
        simulation.FakeTwitch.Authority.Transcript.ShouldContain(value =>
            value.Kind == "oauth.app-token"
        );
        simulation.FakeTwitch.Authority.Transcript.ShouldContain(value =>
            value.Kind == "helix.clip.create"
        );
    }
}

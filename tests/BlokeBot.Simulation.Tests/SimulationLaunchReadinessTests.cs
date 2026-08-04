using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Simulation.Tests;

[NotInParallel]
public sealed class SimulationLaunchReadinessTests
{
    [Test]
    public async Task ReadyDashboard_OnlyBecomesReadyAfterNormalStartupPhases()
    {
        _ = Should.Throw<InvalidOperationException>(static () =>
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
        (await verify.MomentCandidates.CountAsync()).ShouldBe(2);
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
        _ = (
            await verify.MomentCandidates.SingleAsync(value => value.PublicId == captured.PublicId)
        ).TwitchClipId.ShouldNotBeNull();
        simulation.FakeTwitch.Authority.Transcript.ShouldContain(value =>
            value.Kind == "oauth.app-token"
        );
        simulation.FakeTwitch.Authority.Transcript.ShouldContain(value =>
            value.Kind == "helix.clip.create"
        );
    }

    [Test]
    public async Task CommandCatalogScenario_ExposesDeterministicChatAndStateTransitions()
    {
        await using var simulation = await SimulationApplication.BuildAsync(
            ["--urls=http://127.0.0.1:5082"],
            CancellationToken.None
        );
        await simulation.App.InitializeSimulationAsync(CancellationToken.None);
        await simulation.App.StartAsync();
        await simulation
            .App.Services.GetRequiredService<SimulationStartupCoordinator>()
            .BootstrapAsync(simulation.App, CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5082") };
        var database = simulation.App.Services.GetRequiredService<
            IDbContextFactory<BlokeBotDbContext>
        >();

        using var initial = await client.GetFromJsonAsync<JsonDocument>(
            "/simulation/commands/catalog"
        );
        var initialJson = initial!.RootElement.GetRawText();
        initialJson.ShouldContain("!commands");
        initialJson.ShouldContain("!guess");
        initialJson.ShouldContain("!enter");
        initialJson.ShouldContain("!welcome");
        initialJson.ShouldNotContain("!hello");
        var moderatorFixture = initial
            .RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "!modfixture");
        moderatorFixture
            .GetProperty("accessSummary")
            .GetString()
            .ShouldBe("Moderators + 1 selected person");
        initialJson.ShouldContain("shadowed by another command");

        using var chatResponse = await client.PostAsync("/simulation/commands/chat", null);
        var chat = await chatResponse.Content.ReadAsStringAsync();
        chat.ShouldContain("Available viewer commands:");
        chat.Length.ShouldBeGreaterThan(500);

        (await client.PostAsync("/simulation/commands/round/none", null)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );
        (
            await client.PostAsync("/simulation/commands/giveaway/inactive", null)
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (
            var (state, expected) in new[]
            {
                ("all-disabled", HostFeatureFlags.None),
                ("selective-native", HostFeatureFlags.Shoutouts | HostFeatureFlags.Predictions),
                (
                    "mixed",
                    HostFeatureFlags.RequestBoards
                        | HostFeatureFlags.Moments
                        | HostFeatureFlags.Points
                        | HostFeatureFlags.CustomCommands
                ),
                ("all-enabled", HostFeatureFlags.All),
            }
        )
        {
            (
                await client.PostAsync($"/simulation/commands/features/{state}", null)
            ).StatusCode.ShouldBe(HttpStatusCode.OK);
            await using var verify = await database.CreateDbContextAsync();
            (await verify.Hosts.Select(static host => host.EnabledFeatures).SingleAsync()).ShouldBe(
                expected
            );
        }
        (
            await client.PostAsync("/simulation/commands/features/unavailable", null)
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await client.PostAsync("/simulation/commands/liveness/unavailable", null)
        ).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var unavailable = await client.GetFromJsonAsync<JsonDocument>(
            "/simulation/commands/catalog"
        );
        var unavailableJson = unavailable!.RootElement.GetRawText();
        unavailableJson.ShouldNotContain("!guess");
        unavailableJson.ShouldNotContain("!enter");
        unavailableJson.ShouldContain("!welcome");
        unavailableJson.ShouldContain("!moment");
        unavailableJson.ShouldContain("Moment commands are unavailable");
    }
}

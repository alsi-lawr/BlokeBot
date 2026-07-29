using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class SimulationNativeFixtureTests
{
    [Test]
    public void NativeAliasesResolveToTheFiveExactRoutes()
    {
        new Dictionary<string, string>
        {
            ["native-shoutouts"] = "/twitch-operations/shoutouts",
            ["native-polls"] = "/twitch-operations/polls",
            ["native-clips-markers"] = "/twitch-operations/clips-markers",
            ["native-channel-points"] = "/twitch-operations/channel-points",
            ["native-predictions"] = "/twitch-operations/predictions",
        }.ShouldAllBe(pair => SimulationViewCatalog.PathFor(pair.Key) == pair.Value);
    }

    [Test]
    public async Task OfflineSimulationSeedsAutomaticConfigurationOutcomesAndLocalDelivery()
    {
        await using var app = SimulationApplication.Build([]);
        await app.InitializeSimulationAsync(CancellationToken.None);

        var factory = app.Services.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Login == SimulationMode.Login);
        var settings = await db.AutomaticRaidShoutoutSettings.SingleAsync(value =>
            value.HostId == host.Id
        );
        settings.Enabled.ShouldBeTrue();
        settings.Mechanism.ShouldBe(AutomaticRaidShoutoutMechanism.Chat);
        settings.ChatPresentation.ShouldBe(AutomaticRaidChatPresentation.Pinned);
        (
            await db.AutomaticRaidShoutoutOutcomes.CountAsync(value => value.HostId == host.Id)
        ).ShouldBe(4);
        (
            await db.AutomaticRaidShoutoutOutcomes.AnyAsync(value =>
                value.HostId == host.Id
                && value.ResultCode == AutomaticRaidShoutoutResultCode.PartialFailure
            )
        ).ShouldBeTrue();

        var delivery = app.Services.GetRequiredService<IAutomaticRaidShoutoutDelivery>();
        delivery.GetType().Assembly.ShouldBe(typeof(SimulationApplication).Assembly);
    }
}

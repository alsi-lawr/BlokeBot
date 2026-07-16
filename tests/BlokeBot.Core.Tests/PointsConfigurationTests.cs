using System.Numerics;
using BlokeBot.Commands;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PointsConfigurationTests : PointsTestBase
{
    [Test]
    public async Task ChangedGamblingCooldown_SavingConfiguration_RoundTripsValue()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateConfigurationService(dbFactory);

        var config = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        config.GamblingCooldownSeconds = 42;
        var command = ValidConfiguration(config);
        var result = await service
            .SaveConfiguration(hostId, command)
            .ExecuteAsync(CancellationToken.None);
        result.Match(
            static _ => true,
            failure => throw new InvalidOperationException(failure.Message)
        );
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        loaded.GamblingCooldownSeconds.ShouldBe(42);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.PointsSettings.SingleAsync(CancellationToken.None);
        settings.GamblingCooldownSeconds.ShouldBe(42);
    }
}

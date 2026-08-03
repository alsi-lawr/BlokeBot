using Microsoft.EntityFrameworkCore;
using Shouldly;

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
        _ = result.Match(
            static _ => true,
            static failure => throw new InvalidOperationException(failure.Message)
        );
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        loaded.GamblingCooldownSeconds.ShouldBe(42);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.PointsSettings.SingleAsync(CancellationToken.None);
        settings.GamblingCooldownSeconds.ShouldBe(42);
    }
}

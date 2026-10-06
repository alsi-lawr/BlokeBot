using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class WatchTimeRuntimeTests
{
    [Test, Arguments(1), Arguments(6)]
    public async Task Sqlite_ObservedPointsReenableStartsFullInterval(int disabledMinutes) =>
        await ObservedPointsReenableAsync(
            await PointProviderFixture.SqliteAsync(),
            disabledMinutes
        );

    [Test, Explicit, Arguments(1), Arguments(6)]
    public async Task PostgreSql_ObservedPointsReenableStartsFullInterval(int disabledMinutes) =>
        await ObservedPointsReenableAsync(
            await PointProviderFixture.PostgreSqlAsync(),
            disabledMinutes
        );

    private static async Task ObservedPointsReenableAsync(
        PointProviderFixture database,
        int disabledMinutes
    )
    {
        await using var owned = database;
        await using var fixture = new WatchTimeTestSupport(database);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        await fixture.Runtime.ReconcileAsync(default);
        using var alerts = new DurableAlertService(database, clock, fixture.Events);
        var features = new HostFeatureService(
            database,
            new(
                [],
                new(fixture.Events),
                alerts,
                NullLogger<HostFeatureActivationAuthority>.Instance
            ),
            clock
        );
        clock.Advance(TimeSpan.FromMinutes(2));
        _ = (
            await features.DisableAsync(hostId, HostFeatureFlags.Points, default)
        ).ShouldBeOfType<HostFeatureUpdateResult.Saved>();
        await fixture.Runtime.ReconcileAsync(default);
        clock.Advance(TimeSpan.FromMinutes(disabledMinutes));
        _ = (
            await features.EnableAsync(hostId, HostFeatureFlags.Points, default)
        ).ShouldBeOfType<HostFeatureUpdateResult.Saved>();
        await fixture.Runtime.ReconcileAsync(default);
        var firstDue = clock.GetUtcNow() + TimeSpan.FromMinutes(5);
        fixture.Runtime.GetStatus(hostId)!.NextDue.ShouldBe(firstDue);
        _ = await fixture.SaveAsync(hostId, true, "7");
        await fixture.Runtime.ReconcileAsync(default);
        _ = await fixture.SaveAsync(hostId, true, "9");
        await fixture.Runtime.ReconcileAsync(default);
        fixture.Runtime.GetStatus(hostId)!.NextDue.ShouldBe(firstDue);
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await fixture.Runtime.ProcessDueAsync(default);
        fixture.Http.Requests.ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Runtime.ProcessDueAsync(default);
        await using var verify = database.CreateDbContext();
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(3);
        (await verify.PointBalances.Select(value => value.Amount).ToArrayAsync()).ShouldAllBe(
            value => value == "9"
        );
    }
}

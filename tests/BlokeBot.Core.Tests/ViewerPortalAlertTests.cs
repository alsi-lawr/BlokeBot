using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalAlertTests
{
    [Test]
    public async Task SustainedFaults_ReportOnceAndAcknowledgementRecoveryAndCooldownDoNotWritePerRead()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var host = await context.HostAsync("alpha", HostFeatureFlags.Points);
        using var alerts = new DurableAlertService(database, context.Clock, context.Events);
        using var telemetry = new PortalReadTelemetry(
            context.Clock,
            alerts,
            NullLogger<PortalReadTelemetry>.Instance
        );
        for (var index = 0; index < 20; index++)
        {
            await telemetry.ObserveAsync(
                host,
                PortalIcon.Points,
                PortalAudience.Public,
                PortalReadOutcome.BudgetExceeded,
                TimeSpan.FromMilliseconds(20)
            );
        }
        (await alerts.CountActiveAsync(host, default)).ShouldBe(0);
        context.Clock.Advance(TimeSpan.FromSeconds(31));
        await telemetry.ObserveAsync(
            host,
            PortalIcon.Points,
            PortalAudience.Public,
            PortalReadOutcome.BudgetExceeded,
            TimeSpan.FromMilliseconds(20)
        );
        (await alerts.CountActiveAsync(host, default)).ShouldBe(1);
        for (var index = 0; index < 20; index++)
        {
            await telemetry.ObserveAsync(
                host,
                PortalIcon.Points,
                PortalAudience.Public,
                PortalReadOutcome.Unavailable,
                TimeSpan.FromMilliseconds(20)
            );
        }
        await using (var verify = await database.CreateDbContextAsync())
        {
            var alert = await verify.DurableAlerts.SingleAsync();
            alert.OccurrenceCount.ShouldBe(1);
            _ = await alerts.Acknowledge(host, alert.Id, "operator").RunAsync(default);
        }
        await telemetry.ObserveAsync(
            host,
            PortalIcon.Points,
            PortalAudience.Public,
            PortalReadOutcome.Unavailable,
            TimeSpan.FromMilliseconds(20)
        );
        (await alerts.CountActiveAsync(host, default)).ShouldBe(0);
        for (var index = 0; index < 3; index++)
        {
            await telemetry.ObserveAsync(
                host,
                PortalIcon.Points,
                PortalAudience.Public,
                PortalReadOutcome.Available,
                TimeSpan.FromMilliseconds(20)
            );
        }
        context.Clock.Advance(TimeSpan.FromMinutes(31));
        for (var index = 0; index < 10; index++)
        {
            await telemetry.ObserveAsync(
                host,
                PortalIcon.Points,
                PortalAudience.Public,
                PortalReadOutcome.Unavailable,
                TimeSpan.FromMilliseconds(20)
            );
            context.Clock.Advance(TimeSpan.FromSeconds(4));
        }
        (await alerts.CountActiveAsync(host, default)).ShouldBe(1);
        for (var index = 0; index < 3; index++)
        {
            await telemetry.ObserveAsync(
                host,
                PortalIcon.Points,
                PortalAudience.Public,
                PortalReadOutcome.Available,
                TimeSpan.FromMilliseconds(20)
            );
        }
        (await alerts.CountActiveAsync(host, default)).ShouldBe(0);
        await using var final = await database.CreateDbContextAsync();
        (await final.DurableAlerts.CountAsync()).ShouldBe(2);
        (await final.DurableAlerts.SumAsync(value => value.OccurrenceCount)).ShouldBe(2);
    }
}

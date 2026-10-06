using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.WatchTime;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class WatchTimeRuntimeTests
{
    [Test, Explicit]
    public async Task PostgreSql_ActualWorkerCreditsSnapshotOnlyOncePerOpportunity()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        await fixture.StartAsync();
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Waiting);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Active);
        await fixture.Runtime.ProcessDueAsync(default);
        await using var verify = database.CreateDbContext();
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(3);
        (await verify.PointBalances.CountAsync()).ShouldBe(3);
        (await verify.PointBalances.Select(value => value.Amount).ToArrayAsync()).ShouldAllBe(
            value => value == "7"
        );
    }

    [Test]
    public async Task ActualWorker_FirstFullIntervalFreshCompleteSnapshotAndNoCatchUp()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        var hostId = await fixture.SeedAsync(amount: "123456789012345678901234567890");
        var target = await fixture.ConnectAsync(hostId);
        var observed = 0;
        using var subscription = fixture.Events.Subscribe(
            AppEventKind.PointsChanged,
            ObserverIdentity.Named("watch-time-test"),
            (_, _) =>
            {
                observed++;
                return ValueTask.CompletedTask;
            }
        );
        await fixture.StartAsync();
        await fixture.WaitStatusAsync(
            hostId,
            WatchTimeStatusKind.Waiting,
            clock.GetUtcNow() + TimeSpan.FromMinutes(5)
        );
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        fixture.Runtime.AcceptedStarted(target);
        await fixture.WaitUntilAsync(() =>
            Task.FromResult(
                fixture.Runtime.GetStatus(hostId)?.NextDue
                    == new DateTimeOffset(2026, 10, 6, 12, 5, 0, TimeSpan.Zero)
            )
        );
        _ = await clock.WaitForTimerRegistrationAsync();
        fixture.Http.Requests.ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Active);
        await using (var db = await database.CreateDbContextAsync())
        {
            var balances = await db.PointBalances.OrderBy(value => value.Login).ToArrayAsync();
            balances
                .Select(value => value.Login)
                .ShouldBe(["another_bot", "just_joined", "streamer"]);
            balances.All(value => value.Amount == "123456789012345678901234567890").ShouldBeTrue();
            var ledger = await db.PointLedgerEntries.ToArrayAsync();
            ledger.Length.ShouldBe(3);
            ledger
                .All(value =>
                    value.Kind == PointLedgerKind.WatchTimeReward
                    && value.Delta == value.BalanceAfter
                    && value.OperationKey is not null
                )
                .ShouldBeTrue();
        }
        observed.ShouldBe(1);
        fixture.Http.Requests.ShouldBe(2);
        fixture
            .Tokens.Required.All(value => value.SequenceEqual([Scopes.ModeratorReadChatters]))
            .ShouldBeTrue();
        fixture.Streams.Current = new HostStreamLivenessOutcome.Offline();
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Offline);
        fixture.Http.Requests.ShouldBe(2);
        fixture.Streams.Current = new HostStreamLivenessOutcome.Live("stream-2", clock.GetUtcNow());
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(15));
        await fixture.WaitStatusAsync(
            hostId,
            WatchTimeStatusKind.Active,
            clock.GetUtcNow() + TimeSpan.FromMinutes(5)
        );
        await using var final = await database.CreateDbContextAsync();
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(6);
        observed.ShouldBe(2);
        fixture.Http.Requests.ShouldBe(4);
    }

    [Test]
    public async Task ActualWorker_UnavailablePartialPageAndSettingsAbaNeverCredit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        fixture.Http.Response = (request, _) =>
            Task.FromResult(
                request == 1
                    ? WatchHttp.Json(
                        """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{"cursor":"second"}}"""
                    )
                    : new System.Net.Http.HttpResponseMessage(
                        System.Net.HttpStatusCode.ServiceUnavailable
                    )
            );
        await fixture.StartAsync();
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Waiting);
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Unavailable);
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.PointBalances.CountAsync()).ShouldBe(0);
        }
        fixture.Http.Response = async (_, _) =>
        {
            _ = await fixture.SaveAsync(hostId, false, null);
            _ = await fixture.SaveAsync(hostId, true, "9");
            return WatchHttp.Json(
                """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{}}"""
            );
        };
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.WaitStatusAsync(
            hostId,
            WatchTimeStatusKind.Waiting,
            clock.GetUtcNow() + TimeSpan.FromMinutes(5)
        );
        await using var final = await database.CreateDbContextAsync();
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(0);
        (await final.PointsSettings.SingleAsync()).WatchTimePointAmount.ShouldBe("9");
    }

    [Test]
    public async Task FreshHelix_InvalidWholeSnapshotOrChangedAuthorityIsUnavailable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        await fixture.Runtime.ReconcileAsync(default);
        fixture.Http.Response = (_, _) =>
            Task.FromResult(
                WatchHttp.Json(
                    """{"data":[{"user_id":"300","user_login":"bad login","user_name":"Invalid"}],"pagination":{}}"""
                )
            );
        ((ManualTestTimeProvider)fixture.Clock).Advance(TimeSpan.FromMinutes(5));
        await fixture.Runtime.ProcessDueAsync(default);
        fixture.Runtime.GetStatus(hostId)!.Kind.ShouldBe(WatchTimeStatusKind.Unavailable);
        fixture.Http.Response = (_, _) =>
        {
            fixture.Tokens.Status = WatchTokens.Ready("201", "bot");
            return Task.FromResult(
                WatchHttp.Json(
                    """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{}}"""
                )
            );
        };
        ((ManualTestTimeProvider)fixture.Clock).Advance(TimeSpan.FromMinutes(5));
        await fixture.Runtime.ProcessDueAsync(default);
        fixture.Runtime.GetStatus(hostId)!.Kind.ShouldBe(WatchTimeStatusKind.Unavailable);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ActualWorker_ZeroHostCensusAndReconnectWaitFullInterval()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        await fixture.StartAsync();
        _ = await clock.WaitForTimerRegistrationAsync();
        var hostId = await fixture.SeedAsync();
        var target = await fixture.ConnectAsync(hostId);
        await fixture.WaitStatusAsync(
            hostId,
            WatchTimeStatusKind.Waiting,
            clock.GetUtcNow() + TimeSpan.FromMinutes(5)
        );
        clock.Advance(TimeSpan.FromMinutes(4));
        fixture.BotRuntime.Set(new BotRuntimeStatus.Authorized());
        (
            await fixture.Sessions.ConfirmStoppedAsync("streamer", target.SessionIdentity, default)
        ).ShouldBeTrue();
        fixture.Runtime.AcceptedStopped(target);
        _ = await fixture.ConnectAsync(hostId);
        await fixture.WaitStatusAsync(
            hostId,
            WatchTimeStatusKind.Waiting,
            clock.GetUtcNow() + TimeSpan.FromMinutes(5)
        );
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(4));
        await fixture.Runtime.ReconcileAsync(default);
        await fixture.Runtime.ProcessDueAsync(default);
        fixture.Http.Requests.ShouldBe(0);
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Active);
    }

    [Test]
    public async Task ExpiredInFlightSnapshotCannotCreditOrCatchUp()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        await fixture.Runtime.ReconcileAsync(default);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        fixture.Http.Response = (_, _) =>
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            return Task.FromResult(
                WatchHttp.Json(
                    """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{}}"""
                )
            );
        };
        clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.Runtime.ProcessDueAsync(default);
        await using (var verify = database.CreateDbContext())
        {
            (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
        }
        fixture.Http.Response = (_, _) =>
            Task.FromResult(
                WatchHttp.Json(
                    """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{}}"""
                )
            );
        await fixture.Runtime.ProcessDueAsync(default);
        await using var final = database.CreateDbContext();
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(1);
        (await final.PointBalances.SingleAsync()).Amount.ShouldBe("7");
    }

    [Test]
    public async Task FirstDatabaseSnapshotRejectsFeatureDisabledDuringHttp()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        await fixture.Runtime.ReconcileAsync(default);
        fixture.Http.Response = async (_, _) =>
        {
            await using var change = database.CreateDbContext();
            (await change.Hosts.SingleAsync()).EnabledFeatures = HostFeatureFlags.None;
            _ = await change.SaveChangesAsync();
            return WatchHttp.Json(
                """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{}}"""
            );
        };
        ((ManualTestTimeProvider)fixture.Clock).Advance(TimeSpan.FromMinutes(5));
        await fixture.Runtime.ProcessDueAsync(default);
        await using var verify = database.CreateDbContext();
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
        (await verify.PointBalances.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ActualWorkerCancellationDuringHttpDrainsWithoutCredit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(database);
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Http.Response = async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new System.Diagnostics.UnreachableException();
        };
        await fixture.StartAsync();
        await fixture.WaitStatusAsync(hostId, WatchTimeStatusKind.Waiting);
        var clock = (ManualTestTimeProvider)fixture.Clock;
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await fixture.Runtime.StopAsync(default);
        await using var verify = database.CreateDbContext();
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
        (await verify.PointBalances.CountAsync()).ShouldBe(0);
    }
}

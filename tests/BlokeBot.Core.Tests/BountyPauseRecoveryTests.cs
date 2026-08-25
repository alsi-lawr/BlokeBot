using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BountyPauseRecoveryTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RestartRecovery_ShiftsActiveDeadlinesOnceAndRecordsOnlyAdminAudits()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var funding = await CreateInStatusAsync(service, hostId, "Funding", BountyStatus.Funding);
        var accepted = await CreateInStatusAsync(
            service,
            hostId,
            "Accepted",
            BountyStatus.Accepted
        );
        var proposed = await CreateInStatusAsync(
            service,
            hostId,
            "Proposed",
            BountyStatus.Proposed
        );
        var eventCount = await CountEventsAsync(database);
        var features = CreateFeatures(database, service, clock);

        _ = await features.DisableAsync(hostId, HostFeatureFlags.Bounties, default);
        var pausedAtUtc = await PausedAtAsync(database);
        clock.Advance(TimeSpan.FromHours(1));
        _ = await features.DisableAsync(hostId, HostFeatureFlags.Points, default);
        (await PausedAtAsync(database)).ShouldBe(pausedAtUtc);
        clock.Advance(TimeSpan.FromHours(1));
        await EnableWithoutAutomaticWorkAsync(database, hostId);

        var restartedObserver = new BountyPauseObserver(database, service);
        await restartedObserver.RecoverAsync(default);
        await restartedObserver.RecoverAsync(default);

        await using var verify = await database.CreateDbContextAsync();
        var rows = await verify
            .Bounties.AsNoTracking()
            .Where(value =>
                value.PublicId == funding.PublicId
                || value.PublicId == accepted.PublicId
                || value.PublicId == proposed.PublicId
            )
            .ToDictionaryAsync(value => value.PublicId);
        AssertShifted(rows[funding.PublicId], funding, TimeSpan.FromHours(2));
        AssertShifted(rows[accepted.PublicId], accepted, TimeSpan.FromHours(2));
        rows[proposed.PublicId].ExpiresAtUtc.ShouldBe(proposed.ExpiresAtUtc);
        rows[proposed.PublicId].Revision.ShouldBe(proposed.Revision);
        (await verify.Hosts.SingleAsync()).BountiesPausedAtUtc.ShouldBeNull();

        var audits = await verify
            .BountyModerationAudits.AsNoTracking()
            .Where(value => value.Action == BountyAuditAction.PauseAdjusted)
            .OrderBy(value => value.BountyId)
            .ToArrayAsync();
        audits.Length.ShouldBe(2);
        foreach (var audit in audits)
        {
            var original = audit.BountyId == rows[funding.PublicId].Id ? funding : accepted;
            var shifted = rows[original.PublicId];
            var previousExpiry = shifted.ExpiresAtUtc.AddHours(-2);
            audit.ActorTwitchUserId.ShouldBe("BlokeBot.BountyPauseRecovery");
            audit.ActorLogin.ShouldBe("blokebot");
            audit.FromStatus.ShouldBe(original.Status);
            audit.ToStatus.ShouldBe(original.Status);
            audit.BountyRevision.ShouldBe(original.Revision + 1);
            audit.OperationId.ShouldBe(
                BountyPauseAdjustmentOperationId.Create(
                    original.PublicId,
                    pausedAtUtc,
                    clock.GetUtcNow().UtcDateTime
                )
            );
            audit.Reason.ShouldContain(previousExpiry.ToString("O"));
            audit.Reason.ShouldContain(shifted.ExpiresAtUtc.ToString("O"));
            audit.Reason.ShouldContain(pausedAtUtc.ToString("O"));
            audit.Reason.ShouldContain(clock.GetUtcNow().UtcDateTime.ToString("O"));
            audit.Reason.Length.ShouldBeLessThanOrEqualTo(BountyLimits.MaximumReasonLength);
        }
        (await verify.BountyEvents.CountAsync()).ShouldBe(eventCount);
        (await verify.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task PrePauseModeratorRevision_IsRejectedAfterRecovery()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = await CreateInStatusAsync(
            service,
            hostId,
            "Stale revision",
            BountyStatus.Funding
        );
        await PauseAndEnableWithoutAutomaticWorkAsync(database, service, hostId, clock);
        await service.ReconcilePauseAsync(hostId, BountyPauseRecoveryCause.Restart(), default);

        var result = await service.ExtendAsync(
            hostId,
            new ExtendBountyCommand(
                Guid.NewGuid(),
                bounty.PublicId,
                bounty.Revision,
                clock.GetUtcNow().AddHours(4).UtcDateTime,
                new BountyActor("moderator-id", "moderator")
            ),
            default
        );

        var stale = Rejection(result).ShouldBeOfType<BountyRejection.StaleRevision>();
        stale.CurrentRevision.ShouldBe(bounty.Revision + 1);
        var recovered = (
            await service.GetAsync(hostId, bounty.PublicId, default)
        ).ShouldNotBeNull();
        recovered.ExpiresAtUtc.ShouldBe(bounty.ExpiresAtUtc.AddHours(2));
    }

    [Test]
    public async Task ConcurrentRecoveryAndModeratorEdit_SerializeWithoutLosingEitherDeadline()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = await CreateInStatusAsync(
            service,
            hostId,
            "Concurrent edit",
            BountyStatus.Funding
        );
        await PauseAndEnableWithoutAutomaticWorkAsync(database, service, hostId, clock);
        var moderatorExpiry = clock.GetUtcNow().AddHours(4).UtcDateTime;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = RecoverAsync();
        var edit = EditAsync();

        start.SetResult();
        await recovery;
        var editResult = await edit;
        var final = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();

        if (editResult is BountyResult<BountyView>.Succeeded)
        {
            final.ExpiresAtUtc.ShouldBe(moderatorExpiry.AddHours(2));
            final.Revision.ShouldBe(bounty.Revision + 2);
        }
        else
        {
            _ = Rejection(editResult).ShouldBeOfType<BountyRejection.StaleRevision>();
            final.ExpiresAtUtc.ShouldBe(bounty.ExpiresAtUtc.AddHours(2));
            final.Revision.ShouldBe(bounty.Revision + 1);
        }

        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.BountyModerationAudits.CountAsync(value =>
                value.Bounty.PublicId == bounty.PublicId
                && value.Action == BountyAuditAction.PauseAdjusted
            )
        ).ShouldBe(1);

        async Task RecoverAsync()
        {
            await start.Task;
            await service.ReconcilePauseAsync(hostId, BountyPauseRecoveryCause.Restart(), default);
        }

        async Task<BountyResult<BountyView>> EditAsync()
        {
            await start.Task;
            return await service.ExtendAsync(
                hostId,
                new ExtendBountyCommand(
                    Guid.NewGuid(),
                    bounty.PublicId,
                    bounty.Revision,
                    moderatorExpiry,
                    new BountyActor("moderator-id", "moderator")
                ),
                default
            );
        }
    }

    [Test]
    public async Task CancellationWhileRecoveryWaitsForWriter_LeavesPauseRecoverable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = await CreateInStatusAsync(
            service,
            hostId,
            "Cancellation",
            BountyStatus.Funding
        );
        await PauseAndEnableWithoutAutomaticWorkAsync(database, service, hostId, clock);

        await using (var blocker = await database.CreateDbContextAsync())
        {
            await blocker.Database.OpenConnectionAsync();
            var connection = (SqliteConnection)blocker.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            _ = await Should.ThrowAsync<OperationCanceledException>(() =>
                service.ReconcilePauseAsync(
                    hostId,
                    BountyPauseRecoveryCause.Restart(),
                    cancellation.Token
                )
            );

            await using var verify = await database.CreateDbContextAsync();
            var unchanged = await verify.Bounties.SingleAsync(value =>
                value.PublicId == bounty.PublicId
            );
            unchanged.ExpiresAtUtc.ShouldBe(bounty.ExpiresAtUtc);
            unchanged.Revision.ShouldBe(bounty.Revision);
            _ = (await verify.Hosts.SingleAsync()).BountiesPausedAtUtc.ShouldNotBeNull();
            (
                await verify.BountyModerationAudits.CountAsync(value =>
                    value.Action == BountyAuditAction.PauseAdjusted
                )
            ).ShouldBe(0);
        }

        await service.ReconcilePauseAsync(hostId, BountyPauseRecoveryCause.Restart(), default);
        var recovered = (
            await service.GetAsync(hostId, bounty.PublicId, default)
        ).ShouldNotBeNull();
        recovered.ExpiresAtUtc.ShouldBe(bounty.ExpiresAtUtc.AddHours(2));
        recovered.Revision.ShouldBe(bounty.Revision + 1);
    }

    private static HostFeatureService CreateFeatures(
        SqliteBlokeBotDbFactory database,
        BountyService service,
        TimeProvider clock
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(events),
            [new BountyPauseObserver(database, service)],
            clock
        );
    }

    private static BountyService CreateService(
        SqliteBlokeBotDbFactory database,
        TimeProvider clock
    ) => new(database, TestEventBus.Create<AppEventKind>(), clock);

    private static async Task<BountyView> CreateInStatusAsync(
        BountyService service,
        int hostId,
        string title,
        BountyStatus status
    )
    {
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                new CreateBountyCommand(
                    Guid.NewGuid(),
                    title,
                    "Pause recovery test bounty.",
                    new PointAmount(100),
                    _now.AddHours(1).UtcDateTime,
                    PointAmount.Zero,
                    BountyVisibility.Public,
                    BountyFailurePledgePolicy.Refund,
                    BountyRewardDistribution.Proportional,
                    new BountyActor("host-id", "streamer")
                ),
                default
            )
        ).Value;
        if (status == BountyStatus.Proposed)
        {
            return bounty;
        }

        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        return status == BountyStatus.Funding
            ? bounty
            : Success(
                await service.TransitionAsync(
                    hostId,
                    Transition(bounty, BountyTransitionAction.Accept),
                    default
                )
            ).Value;
    }

    private static TransitionBountyCommand Transition(
        BountyView bounty,
        BountyTransitionAction action
    ) =>
        new(
            Guid.NewGuid(),
            bounty.PublicId,
            bounty.Revision,
            action,
            new BountyActor("host-id", "streamer")
        );

    private static async Task PauseAndEnableWithoutAutomaticWorkAsync(
        SqliteBlokeBotDbFactory database,
        BountyService service,
        int hostId,
        ManualTimeProvider clock
    )
    {
        _ = await CreateFeatures(database, service, clock)
            .DisableAsync(hostId, HostFeatureFlags.Bounties, default);
        clock.Advance(TimeSpan.FromHours(2));
        await EnableWithoutAutomaticWorkAsync(database, hostId);
    }

    private static async Task EnableWithoutAutomaticWorkAsync(
        SqliteBlokeBotDbFactory database,
        int hostId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures |= HostFeatureFlags.Bounties | HostFeatureFlags.Points;
        _ = await db.SaveChangesAsync();
    }

    private static async Task<DateTime> PausedAtAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        return (await db.Hosts.SingleAsync()).BountiesPausedAtUtc.ShouldNotBeNull();
    }

    private static async Task<int> CountEventsAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        return await db.BountyEvents.CountAsync();
    }

    private static void AssertShifted(Bounty row, BountyView original, TimeSpan pausedFor)
    {
        row.ExpiresAtUtc.ShouldBe(original.ExpiresAtUtc.Add(pausedFor));
        row.Revision.ShouldBe(original.Revision + 1);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "host-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static BountyResult<T>.Succeeded Success<T>(BountyResult<T> result) =>
        result.Match(
            static succeeded => succeeded,
            static rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private static BountyRejection Rejection<T>(BountyResult<T> result) =>
        result.Match(
            static _ => throw new InvalidOperationException("Expected rejection."),
            static rejected => rejected.Reason
        );

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}

using System.Globalization;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BountyServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CompletedBounty_CapsFundingConsumesPledgesAndDistributesReward()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "first", "100");
        await SeedBalanceAsync(database, hostId, "second", "100");
        var service = CreateService(database);
        var bounty = Success(await service.CreateAsync(hostId, Create(), default)).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;

        var first = Success(
            await service.PledgeAsync(hostId, Pledge(bounty, "1", "first", 80), default)
        ).Value;
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        var second = Success(
            await service.PledgeAsync(hostId, Pledge(bounty, "2", "second", 50), default)
        ).Value;
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Accept),
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Complete),
                default
            )
        ).Value;

        first.ReservedAmount.ShouldBe(new PointAmount(80));
        second.ReservedAmount.ShouldBe(new PointAmount(20));
        bounty.Status.ShouldBe(BountyStatus.Completed);
        bounty.PledgedAmount.ShouldBe(new PointAmount(100));
        bounty.ContributorCount.ShouldBe(2);
        bounty.Contributors.ShouldBe([
            new BountyContributorView("first", new PointAmount(80)),
            new BountyContributorView("second", new PointAmount(20)),
        ]);
        bounty.TerminalHistory.Single().Status.ShouldBe(BountyStatus.Completed);
        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "first")).ShouldBe("28");
        (await BalanceAsync(verify, hostId, "second")).ShouldBe("82");
        (await verify.BountyPledges.Select(value => value.State).ToListAsync()).ShouldAllBe(state =>
            state == BountyPledgeState.Consumed
        );
        var rewards = await verify
            .BountyContributorRewards.OrderBy(value => value.TwitchUserId)
            .Select(value => value.Amount)
            .ToListAsync();
        rewards.ShouldBe(["8", "2"]);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyPledgeReservation
            )
        ).ShouldBe(2);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyPledgeConsumption
            )
        ).ShouldBe(2);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyCompletionReward
            )
        ).ShouldBe(2);
    }

    [Test]
    public async Task CancelledBounty_RefundsEachPledgeOnceAcrossOperationRetry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var service = CreateService(database);
        var bounty = Success(await service.CreateAsync(hostId, Create(), default)).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        _ = Success(await service.PledgeAsync(hostId, Pledge(bounty, "1", "viewer", 40), default));
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        var operationId = Guid.NewGuid();
        var command = Transition(bounty, BountyTransitionAction.Cancel) with
        {
            OperationId = operationId,
        };

        var cancelled = Success(await service.TransitionAsync(hostId, command, default));
        var retry = Success(await service.TransitionAsync(hostId, command, default));

        cancelled.Value.Status.ShouldBe(BountyStatus.Cancelled);
        retry.WasIdempotent.ShouldBeTrue();
        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe("100");
        (await verify.BountyPledges.SingleAsync()).State.ShouldBe(BountyPledgeState.Refunded);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyPledgeRefund
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task ReservedPledge_BlocksCreditsThatWouldPreventItsRefund()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(
            database,
            hostId,
            "viewer",
            PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)
        );
        var service = CreateService(database);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create() with
                {
                    FundingTarget = new PointAmount(10),
                    CompletionReward = PointAmount.Zero,
                },
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        _ = Success(
            await service.PledgeAsync(hostId, Pledge(bounty, "viewer-id", "viewer", 10), default)
        );

        var credit = await new PointBalanceService(database)
            .Add(hostId, "viewer", new PointAmount(1), "alpha", "test")
            .ExecuteAsync(default);

        _ = credit
            .Match(
                static _ => throw new InvalidOperationException("Expected the credit to fail."),
                static failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        _ = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Cancel),
                default
            )
        );

        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe(
            PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)
        );
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyPledgeRefund
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentDistinctPledges_CannotOverspendOrOverfund()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var service = CreateService(database);
        var bounty = Success(await service.CreateAsync(hostId, Create(), default)).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;

        var results = await Task.WhenAll(
            service.PledgeAsync(hostId, Pledge(bounty, "1", "viewer", 80), default),
            service.PledgeAsync(hostId, Pledge(bounty, "1", "viewer", 80), default)
        );

        results
            .Select(Success)
            .Aggregate(
                System.Numerics.BigInteger.Zero,
                (total, value) => total + value.Value.ReservedAmount.Value
            )
            .ShouldBe(new System.Numerics.BigInteger(100));
        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe("0");
        (await verify.Bounties.SingleAsync()).PledgedAmount.ShouldBe("100");
        (await verify.BountyPledges.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task FailedBounty_AppliesItsConfiguredRefundOrSpendPolicy()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "200");
        var service = CreateService(database);

        _ = await FundAcceptAndFailAsync(
            service,
            hostId,
            BountyFailurePledgePolicy.Refund,
            "refund-viewer"
        );
        _ = await FundAcceptAndFailAsync(
            service,
            hostId,
            BountyFailurePledgePolicy.Spend,
            "spend-viewer"
        );

        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe("150");
        (
            await verify
                .BountyPledges.OrderBy(value => value.Id)
                .Select(value => value.State)
                .ToListAsync()
        ).ShouldBe([BountyPledgeState.Refunded, BountyPledgeState.Consumed]);
    }

    [Test]
    public async Task BountyIdentityAndEvents_RemainHostScoped()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHostId = await SeedHostAsync(database, "alpha");
        var secondHostId = await SeedHostAsync(database, "beta");
        var service = CreateService(database);
        var bounty = Success(await service.CreateAsync(firstHostId, Create(), default)).Value;

        (await service.GetAsync(secondHostId, bounty.PublicId, default)).ShouldBeNull();
        var rejected = Rejection(
            await service.TransitionAsync(
                secondHostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        );
        _ = rejected.ShouldBeOfType<BountyRejection.NotFound>();
        (await service.GetEventsAsync(secondHostId, 0, 100, default)).ShouldBeEmpty();
        (await service.GetEventsAsync(firstHostId, 0, 100, default)).Count.ShouldBe(1);
    }

    [Test]
    public async Task OperationIdReuse_WithDifferentPayloadIsRejectedWithoutASecondMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var service = CreateService(database);
        var create = Create();
        var bounty = Success(await service.CreateAsync(hostId, create, default)).Value;
        _ = Rejection(
                await service.CreateAsync(
                    hostId,
                    create with
                    {
                        Title = "A different bounty",
                    },
                    default
                )
            )
            .ShouldBeOfType<BountyRejection.Conflict>();
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        var pledge = Pledge(bounty, "viewer-id", "viewer", 40);
        _ = Success(await service.PledgeAsync(hostId, pledge, default));

        _ = Rejection(
                await service.PledgeAsync(
                    hostId,
                    pledge with
                    {
                        RequestedAmount = new PointAmount(50),
                    },
                    default
                )
            )
            .ShouldBeOfType<BountyRejection.Conflict>();
        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe("60");
        (await verify.BountyPledges.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task ExpiredFunding_CannotBeAcceptedAndCanBeRefundedExactlyOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create() with
                {
                    ExpiresAtUtc = _now.AddHours(1).UtcDateTime,
                },
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        _ = Success(
            await service.PledgeAsync(hostId, Pledge(bounty, "viewer-id", "viewer", 100), default)
        );
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromHours(2));

        _ = Rejection(
                await service.TransitionAsync(
                    hostId,
                    Transition(bounty, BountyTransitionAction.Accept),
                    default
                )
            )
            .ShouldBeOfType<BountyRejection.Invalid>();
        var expired = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Expire),
                default
            )
        ).Value;

        expired.Status.ShouldBe(BountyStatus.Expired);
        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe("100");
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyPledgeRefund
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task AcceptedBelowTarget_CanBeExtendedAndExpiresWithACompleteRefund()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create() with
                {
                    ExpiresAtUtc = _now.AddHours(1).UtcDateTime,
                },
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        _ = Success(
            await service.PledgeAsync(hostId, Pledge(bounty, "viewer-id", "viewer", 25), default)
        );
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Accept),
                default
            )
        ).Value;
        bounty = Success(
            await service.ExtendAsync(
                hostId,
                new ExtendBountyCommand(
                    Guid.NewGuid(),
                    bounty.PublicId,
                    bounty.Revision,
                    _now.AddHours(2).UtcDateTime,
                    Actor("host", "alpha"),
                    "More stream time"
                ),
                default
            )
        ).Value;
        clock.Advance(TimeSpan.FromHours(3));

        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Expire),
                default
            )
        ).Value;

        bounty.Status.ShouldBe(BountyStatus.Expired);
        bounty.PledgedAmount.ShouldBe(new PointAmount(25));
        await using var verify = await database.CreateDbContextAsync();
        (await BalanceAsync(verify, hostId, "viewer")).ShouldBe("100");
        (await verify.BountyPledges.SingleAsync()).State.ShouldBe(BountyPledgeState.Refunded);
    }

    [Test]
    public async Task PrivateBounty_DoesNotEnterAnyPublicProjection()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);

        _ = Success(
            await service.CreateAsync(
                hostId,
                Create() with
                {
                    Visibility = BountyVisibility.Private,
                },
                default
            )
        );

        (await service.GetEventsAsync(hostId, 0, 100, default)).ShouldBeEmpty();
        (await service.GetPublicBoardAsync("alpha", default)).ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.BountyEvents.CountAsync()).ShouldBe(1);
    }

    private static CreateBountyCommand Create() =>
        new(
            Guid.NewGuid(),
            "Choose the challenge",
            "Fund a challenge for the stream.",
            new PointAmount(100),
            _now.AddDays(1).UtcDateTime,
            new PointAmount(10),
            BountyVisibility.Public,
            BountyFailurePledgePolicy.Refund,
            BountyRewardDistribution.Proportional,
            Actor("host", "alpha")
        );

    private static async Task<BountyView> FundAcceptAndFailAsync(
        BountyService service,
        int hostId,
        BountyFailurePledgePolicy failurePolicy,
        string title
    )
    {
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create() with
                {
                    OperationId = Guid.NewGuid(),
                    Title = title,
                    FundingTarget = new PointAmount(50),
                    FailurePledgePolicy = failurePolicy,
                },
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding),
                default
            )
        ).Value;
        _ = Success(
            await service.PledgeAsync(hostId, Pledge(bounty, "viewer-id", "viewer", 50), default)
        );
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Accept),
                default
            )
        ).Value;
        return Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.Fail),
                default
            )
        ).Value;
    }

    private static PledgeBountyCommand Pledge(
        BountyView bounty,
        string userId,
        string login,
        int amount
    ) => new(Guid.NewGuid(), bounty.PublicId, Actor(userId, login), new PointAmount(amount));

    private static TransitionBountyCommand Transition(
        BountyView bounty,
        BountyTransitionAction action
    ) => new(Guid.NewGuid(), bounty.PublicId, bounty.Revision, action, Actor("host", "alpha"));

    private static BountyActor Actor(string userId, string login) => new(userId, login);

    private static BountyService CreateService(
        SqliteBlokeBotDbFactory database,
        TimeProvider? clock = null
    ) => new(database, TestEventBus.Create<AppEventKind>(), clock ?? new ManualTimeProvider(_now));

    private static BountyResult<T>.Succeeded Success<T>(BountyResult<T> result) =>
        result.Match(
            static value => value,
            static rejected =>
                throw new InvalidOperationException(
                    $"Expected success but received: {rejected.Reason.Message}"
                )
        );

    private static BountyRejection Rejection<T>(BountyResult<T> result) =>
        result.Match(
            static _ => throw new InvalidOperationException("Expected rejection."),
            static rejected => rejected.Reason
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedBalanceAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string login,
        string amount
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = hostId,
                Login = login,
                Amount = amount,
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static Task<string> BalanceAsync(
        BlokeBot.Persistence.BlokeBotDbContext db,
        int hostId,
        string login
    ) =>
        db
            .PointBalances.Where(value => value.HostId == hostId && value.Login == login)
            .Select(value => value.Amount)
            .SingleAsync();

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}

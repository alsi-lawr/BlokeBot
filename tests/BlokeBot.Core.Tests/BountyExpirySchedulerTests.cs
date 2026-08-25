using System.Collections.Concurrent;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BountyExpirySchedulerTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Startup_WithOverdueFundingAndAcceptedBounties_ExpiresEnabledHostsOnly()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var enabledHostId = await SeedHostAsync(database, "alpha");
        var disabledHostId = await SeedHostAsync(database, "beta");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var proposed = Success(
            await service.CreateAsync(
                enabledHostId,
                Create("Proposed", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        var funding = Success(
            await service.CreateAsync(
                enabledHostId,
                Create("Funding", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        funding = Success(
            await service.TransitionAsync(
                enabledHostId,
                Transition(funding, BountyTransitionAction.OpenFunding, "alpha"),
                default
            )
        ).Value;
        var accepted = Success(
            await service.CreateAsync(
                enabledHostId,
                Create("Accepted", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        accepted = Success(
            await service.TransitionAsync(
                enabledHostId,
                Transition(accepted, BountyTransitionAction.OpenFunding, "alpha"),
                default
            )
        ).Value;
        accepted = Success(
            await service.TransitionAsync(
                enabledHostId,
                Transition(accepted, BountyTransitionAction.Accept, "alpha"),
                default
            )
        ).Value;
        var disabled = Success(
            await service.CreateAsync(
                disabledHostId,
                Create("Disabled", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        disabled = Success(
            await service.TransitionAsync(
                disabledHostId,
                Transition(disabled, BountyTransitionAction.OpenFunding, "beta"),
                default
            )
        ).Value;
        await PauseBountiesAsync(database, disabledHostId, clock);
        clock.Advance(TimeSpan.FromHours(2));
        var logger = new CompletionLogger(expectedExpirations: 2);
        var scheduler = CreateScheduler(database, service, clock, logger, TimeSpan.FromHours(1));

        try
        {
            await scheduler.StartAsync(CancellationToken.None);
            await logger.ExpiryTargetReached.WaitAsync(TimeSpan.FromSeconds(5));

            await using var verify = await database.CreateDbContextAsync();
            var statuses = await verify
                .Bounties.Where(value =>
                    value.PublicId == proposed.PublicId
                    || value.PublicId == funding.PublicId
                    || value.PublicId == accepted.PublicId
                    || value.PublicId == disabled.PublicId
                )
                .ToDictionaryAsync(value => value.PublicId, value => value.Status);
            statuses[proposed.PublicId].ShouldBe(BountyStatus.Proposed);
            statuses[funding.PublicId].ShouldBe(BountyStatus.Expired);
            statuses[accepted.PublicId].ShouldBe(BountyStatus.Expired);
            statuses[disabled.PublicId].ShouldBe(BountyStatus.Funding);
        }
        finally
        {
            await StopAsync(scheduler);
        }
    }

    [Test]
    public async Task SameExpiryCandidate_Retried_IsIdempotentAndRefundsOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create("Funded", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding, "alpha"),
                default
            )
        ).Value;
        _ = Success(
            await service.PledgeAsync(
                hostId,
                new PledgeBountyCommand(
                    Guid.NewGuid(),
                    bounty.PublicId,
                    new BountyActor("viewer-id", "viewer"),
                    new PointAmount(40)
                ),
                default
            )
        );
        bounty = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        var candidate = await CandidateAsync(database, bounty.PublicId);
        clock.Advance(TimeSpan.FromHours(2));
        var scheduler = CreateScheduler(database, service, clock, new CompletionLogger(1));

        var first = Success(await scheduler.ExpireAsync(candidate, default));
        var retry = Success(await scheduler.ExpireAsync(candidate, default));

        first.WasIdempotent.ShouldBeFalse();
        retry.WasIdempotent.ShouldBeTrue();
        first.Value.Status.ShouldBe(BountyStatus.Expired);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.BountyModerationAudits.CountAsync(value =>
                value.Bounty.PublicId == bounty.PublicId
                && value.Action == BountyAuditAction.Expired
            )
        ).ShouldBe(1);
        (
            await verify.BountyEvents.CountAsync(value =>
                value.BountyPublicId == bounty.PublicId && value.Kind == BountyEventKind.Expired
            )
        ).ShouldBe(1);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BountyPledgeRefund
            )
        ).ShouldBe(1);
        (
            await verify
                .PointBalances.Where(value => value.HostId == hostId && value.Login == "viewer")
                .Select(value => value.Amount)
                .SingleAsync()
        ).ShouldBe("100");
    }

    [Test]
    public async Task ExtendedBounty_UsesNewExpiryOperationIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var firstExpiry = _now.AddHours(1).UtcDateTime;
        var bounty = Success(
            await service.CreateAsync(hostId, Create("Extend", firstExpiry), default)
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding, "alpha"),
                default
            )
        ).Value;
        var originalCandidate = await CandidateAsync(database, bounty.PublicId);
        clock.Advance(TimeSpan.FromMinutes(30));
        var extendedExpiry = _now.AddHours(2).UtcDateTime;
        bounty = Success(
            await service.ExtendAsync(
                hostId,
                new ExtendBountyCommand(
                    Guid.NewGuid(),
                    bounty.PublicId,
                    bounty.Revision,
                    extendedExpiry,
                    new BountyActor("host-id", "alpha")
                ),
                default
            )
        ).Value;
        var extendedCandidate = await CandidateAsync(database, bounty.PublicId);
        var originalOperation = BountyExpiryOperationId.Create(bounty.PublicId, firstExpiry);
        var extendedOperation = BountyExpiryOperationId.Create(bounty.PublicId, extendedExpiry);
        var scheduler = CreateScheduler(database, service, clock, new CompletionLogger(1));
        clock.Advance(TimeSpan.FromHours(2));

        BountyExpiryOperationId.Create(bounty.PublicId, firstExpiry).ShouldBe(originalOperation);
        extendedOperation.ShouldNotBe(originalOperation);
        _ = Rejection(await scheduler.ExpireAsync(originalCandidate, default))
            .ShouldBeOfType<BountyRejection.StaleRevision>();
        var expired = Success(await scheduler.ExpireAsync(extendedCandidate, default));

        expired.Value.Status.ShouldBe(BountyStatus.Expired);
        await using var verify = await database.CreateDbContextAsync();
        var expiryAudit = await verify.BountyModerationAudits.SingleAsync(value =>
            value.Bounty.PublicId == bounty.PublicId && value.Action == BountyAuditAction.Expired
        );
        expiryAudit.OperationId.ShouldBe(extendedOperation);
        (
            await verify.BountyModerationAudits.AnyAsync(value =>
                value.OperationId == originalOperation
            )
        ).ShouldBeFalse();
    }

    [Test]
    public async Task TransientCandidateLoadFailure_NextPeriodicPollRecovers()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create("Retry", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding, "alpha"),
                default
            )
        ).Value;
        clock.Advance(TimeSpan.FromHours(2));
        var failingFactory = new FailingOnceDbContextFactory(database);
        var logger = new CompletionLogger(expectedExpirations: 1);
        var scheduler = CreateScheduler(
            failingFactory,
            service,
            clock,
            logger,
            TimeSpan.FromMilliseconds(10)
        );

        try
        {
            await scheduler.StartAsync(CancellationToken.None);
            await logger.ExpiryTargetReached.WaitAsync(TimeSpan.FromSeconds(5));

            failingFactory.AsyncAttempts.ShouldBeGreaterThanOrEqualTo(2);
            logger.Entries.ShouldContain(entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains("later poll will retry", StringComparison.Ordinal)
                && entry.Message.Contains(typeof(IOException).FullName!, StringComparison.Ordinal)
            );
            (await service.GetAsync(hostId, bounty.PublicId, default))!.Status.ShouldBe(
                BountyStatus.Expired
            );
        }
        finally
        {
            await StopAsync(scheduler);
        }
    }

    [Test]
    public async Task ReenableAfterRestart_PreservesRemainingDeadlineAndDoesNotReplayExpiry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                Create("Paused", _now.AddHours(1).UtcDateTime),
                default
            )
        ).Value;
        bounty = Success(
            await service.TransitionAsync(
                hostId,
                Transition(bounty, BountyTransitionAction.OpenFunding, "alpha"),
                default
            )
        ).Value;
        await PauseBountiesAsync(database, hostId, clock);
        clock.Advance(TimeSpan.FromHours(2));
        await using (var enable = await database.CreateDbContextAsync())
        {
            var host = await enable.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures |= HostFeatureFlags.Bounties;
            _ = await enable.SaveChangesAsync();
        }

        var scheduler = CreateScheduler(database, service, clock, new CompletionLogger(1));
        await scheduler.PollOnceAsync(default);

        var resumed = (await service.GetAsync(hostId, bounty.PublicId, default)).ShouldNotBeNull();
        resumed.Status.ShouldBe(BountyStatus.Funding);
        resumed.ExpiresAtUtc.ShouldBe(_now.AddHours(3).UtcDateTime);
        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.Hosts.SingleAsync()).BountiesPausedAtUtc.ShouldBeNull();
        }

        clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        await scheduler.PollOnceAsync(default);
        (await service.GetAsync(hostId, bounty.PublicId, default))!.Status.ShouldBe(
            BountyStatus.Expired
        );
    }

    private static BountyExpiryScheduler CreateScheduler(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        BountyService service,
        TimeProvider clock,
        ILogger<BountyExpiryScheduler> logger,
        TimeSpan? pollInterval = null
    ) =>
        new(
            dbFactory,
            service,
            new BountyPauseObserver(dbFactory, clock),
            new BountyExpirySchedulerPolicy
            {
                PollInterval = pollInterval ?? TimeSpan.FromMinutes(1),
                BatchSize = 20,
            },
            clock,
            logger
        );

    private static BountyService CreateService(
        IDbContextFactory<BlokeBotDbContext> database,
        TimeProvider clock
    ) => new(database, TestEventBus.Create<AppEventKind>(), clock);

    private static CreateBountyCommand Create(string title, DateTime expiresAtUtc) =>
        new(
            Guid.NewGuid(),
            title,
            "Scheduler test bounty.",
            new PointAmount(100),
            expiresAtUtc,
            PointAmount.Zero,
            BountyVisibility.Public,
            BountyFailurePledgePolicy.Refund,
            BountyRewardDistribution.Proportional,
            new BountyActor("host-id", "alpha")
        );

    private static TransitionBountyCommand Transition(
        BountyView bounty,
        BountyTransitionAction action,
        string hostLogin
    ) =>
        new(
            Guid.NewGuid(),
            bounty.PublicId,
            bounty.Revision,
            action,
            new BountyActor("host-id", hostLogin)
        );

    private static BountyResult<T>.Succeeded Success<T>(BountyResult<T> result) =>
        result.Match(
            static succeeded => succeeded,
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

    private static async Task<BountyExpiryCandidate> CandidateAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        Guid publicId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        return await db
            .Bounties.Where(value => value.PublicId == publicId)
            .Select(value => new BountyExpiryCandidate(
                value.Id,
                value.HostId,
                value.PublicId,
                value.ExpiresAtUtc,
                value.Revision
            ))
            .SingleAsync();
    }

    private static async Task<int> SeedHostAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        string login
    )
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

    private static async Task PauseBountiesAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        int hostId,
        TimeProvider clock
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        var observer = new BountyPauseObserver(database, clock);
        var features = TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(events),
            [observer],
            clock
        );
        _ = await features.DisableAsync(hostId, HostFeatureFlags.Bounties, default);
    }

    private static async Task SeedBalanceAsync(
        IDbContextFactory<BlokeBotDbContext> database,
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

    private static async Task StopAsync(BountyExpiryScheduler scheduler)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync(timeout.Token);
        scheduler.Dispose();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class FailingOnceDbContextFactory(IDbContextFactory<BlokeBotDbContext> inner)
        : IDbContextFactory<BlokeBotDbContext>
    {
        private int _asyncAttempts;

        public int AsyncAttempts => _asyncAttempts;

        public BlokeBotDbContext CreateDbContext() => inner.CreateDbContext();

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) =>
            Interlocked.Increment(ref _asyncAttempts) == 1
                ? Task.FromException<BlokeBotDbContext>(
                    new IOException("Transient scheduler test failure.")
                )
                : inner.CreateDbContextAsync(cancellationToken);
    }

    private sealed class CompletionLogger(int expectedExpirations) : ILogger<BountyExpiryScheduler>
    {
        private readonly TaskCompletionSource _expiryTargetReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _expirations;

        public ConcurrentQueue<LogEntry> Entries { get; } = [];

        public Task ExpiryTargetReached => _expiryTargetReached.Task;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var message = formatter(state, exception);
            Entries.Enqueue(new LogEntry(logLevel, message));
            if (
                logLevel == LogLevel.Information
                && message.StartsWith("Expired overdue bounty", StringComparison.Ordinal)
                && Interlocked.Increment(ref _expirations) == expectedExpirations
            )
            {
                _ = _expiryTargetReached.TrySetResult();
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose() { }
    }
}

using System.Data.Common;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutObserverTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DisabledBelowThresholdStaleAndMissingIdentity_DoNotClaimOrDeliver()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: false, threshold: 10);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        var observer = Observer(factory, delivery);

        await observer.IncomingRaidReceivedAsync(
            Raid("disabled", _now, 20),
            CancellationToken.None
        );
        await SetEnabledAsync(factory, hostId, enabled: true);
        await observer.IncomingRaidReceivedAsync(Raid("below", _now, 9), CancellationToken.None);
        await observer.IncomingRaidReceivedAsync(
            Raid("stale", _now.AddMinutes(-2).AddTicks(-1), 20),
            CancellationToken.None
        );
        await observer.IncomingRaidReceivedAsync(Raid("", _now, 20), CancellationToken.None);

        delivery.Requests.ShouldBeEmpty();
        await using var db = await factory.CreateDbContextAsync();
        (await db.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(0);
        (await db.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ExactlyTwoMinutesOld_ClaimsBeforeOneTypedDeliveryAndPersistsMappedResult()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(
            new AutomaticRaidShoutoutDeliveryResult.NotDelivered(
                AutomaticRaidShoutoutResultCode.Rejected
            )
        );
        var observer = Observer(factory, delivery);

        await observer.IncomingRaidReceivedAsync(
            Raid("boundary", _now.AddMinutes(-2), 1),
            CancellationToken.None
        );

        delivery.Requests.ShouldHaveSingleItem().ProviderMessageId.ShouldBe("boundary");
        await using var db = await factory.CreateDbContextAsync();
        var claim = await db.AutomaticRaidProcessedEvents.SingleAsync();
        claim.ExpiresAtUtc.ShouldBe(_now.UtcDateTime);
        var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
    }

    [Test]
    public async Task SequentialAndRestartDuplicate_UsesDurableHostScopedClaimOnce()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        var raid = Raid("duplicate", _now, 1);

        await Observer(factory, delivery).IncomingRaidReceivedAsync(raid, CancellationToken.None);
        await Observer(factory, delivery).IncomingRaidReceivedAsync(raid, CancellationToken.None);

        delivery.Requests.Count.ShouldBe(1);
        await using var db = await factory.CreateDbContextAsync();
        (await db.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(1);
        (await db.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task UnrelatedWriterContention_DoesNotSilentlySuppressDistinctRaid()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: true, threshold: 1);
        await using var writer = await factory.CreateDbContextAsync();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        writer.AutomaticRaidShoutoutOutcomes.Add(
            Outcome(
                hostId,
                "held-writer",
                AutomaticRaidShoutoutOutcomeStatus.Processing,
                null,
                null
            )
        );
        await writer.SaveChangesAsync();
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        var observation = Task.Run(() =>
            Observer(factory, delivery)
                .IncomingRaidReceivedAsync(
                    Raid("distinct-under-lock", _now, 1),
                    CancellationToken.None
                )
        );
        await Task.Delay(AutomaticRaidShoutoutObserver.ClaimContentionRetryDelay * 2);
        observation.IsCompleted.ShouldBeFalse();
        await transaction.CommitAsync();
        await observation;

        delivery.Requests.ShouldHaveSingleItem().ProviderMessageId.ShouldBe("distinct-under-lock");
        await using var verification = await factory.CreateDbContextAsync();
        (
            await verification.AutomaticRaidProcessedEvents.CountAsync(value =>
                value.ProviderMessageId == "distinct-under-lock"
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task PersistentWriterContention_SurfacesFailureInsteadOfLookingLikeDuplicate()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: true, threshold: 1);
        await using var writer = await factory.CreateDbContextAsync();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        writer.AutomaticRaidShoutoutOutcomes.Add(
            Outcome(
                hostId,
                "held-writer",
                AutomaticRaidShoutoutOutcomeStatus.Processing,
                null,
                null
            )
        );
        await writer.SaveChangesAsync();
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        var observation = Task.Run(() =>
            Observer(factory, delivery)
                .IncomingRaidReceivedAsync(
                    Raid("contention-exhausted", _now, 1),
                    CancellationToken.None
                )
        );
        SqliteException exception;
        try
        {
            exception = await Should.ThrowAsync<SqliteException>(async () =>
                await observation.WaitAsync(TimeSpan.FromSeconds(6))
            );
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        exception.SqliteErrorCode.ShouldBeOneOf(
            SQLitePCL.raw.SQLITE_BUSY,
            SQLitePCL.raw.SQLITE_LOCKED
        );
        delivery.Requests.ShouldBeEmpty();
        await using var verification = await factory.CreateDbContextAsync();
        (await verification.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(0);
        (await verification.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ConcurrentDuplicate_PreCommitContention_HasOneDurableWinnerAndOneDelivery()
    {
        var coordination = new ClaimInsertCoordination();
        await using var factory = await CoordinatedSqliteDbFactory.CreateAsync(coordination);
        await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        var raid = Raid("concurrent", _now, 1);
        var first = Observer(factory, delivery)
            .IncomingRaidReceivedAsync(raid, CancellationToken.None);
        await coordination.FirstInsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondClock = new SignalingTimeProvider(_now);
        var second = Task.Run(() =>
            new AutomaticRaidShoutoutObserver(
                factory,
                delivery,
                secondClock
            ).IncomingRaidReceivedAsync(raid, CancellationToken.None)
        );
        await secondClock.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Delay(AutomaticRaidShoutoutObserver.ClaimContentionRetryDelay * 2);
            second.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            coordination.ReleaseFirstInsert.TrySetResult();
        }
        await Task.WhenAll(first, second);

        delivery.Requests.Count.ShouldBe(1);
        await using var verification = await factory.CreateDbContextAsync();
        (await verification.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(1);
        (await verification.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task CrashOrAmbiguousProcessing_IsVisibleAndNeverReplayed()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1);
        var throwing = new ThrowingDelivery();
        var raid = Raid("crash", _now, 1);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            Observer(factory, throwing).IncomingRaidReceivedAsync(raid, CancellationToken.None)
        );
        var replacement = new RecordingDelivery(
            new AutomaticRaidShoutoutDeliveryResult.Delivered()
        );

        await Observer(factory, replacement)
            .IncomingRaidReceivedAsync(raid, CancellationToken.None);

        replacement.Requests.ShouldBeEmpty();
        await using var db = await factory.CreateDbContextAsync();
        (await db.AutomaticRaidShoutoutOutcomes.SingleAsync()).Status.ShouldBe(
            AutomaticRaidShoutoutOutcomeStatus.Processing
        );
    }

    [Test]
    public async Task AmbiguousResult_IsVisibleAndSameHostRedeliveryNeverReplays()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Ambiguous());
        var observer = Observer(factory, delivery);
        var raid = Raid("ambiguous-replay", _now, 1);

        await observer.IncomingRaidReceivedAsync(raid, CancellationToken.None);
        await observer.IncomingRaidReceivedAsync(raid, CancellationToken.None);

        delivery.Requests.Count.ShouldBe(1);
        await using var verification = await factory.CreateDbContextAsync();
        var outcome = await verification.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Ambiguous);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Ambiguous);
    }

    [Test]
    [Arguments(
        DeliveryResultShape.Delivered,
        AutomaticRaidShoutoutResultCode.NotReady,
        AutomaticRaidShoutoutOutcomeStatus.Delivered,
        AutomaticRaidShoutoutResultCode.Delivered
    )]
    [Arguments(
        DeliveryResultShape.Ambiguous,
        AutomaticRaidShoutoutResultCode.NotReady,
        AutomaticRaidShoutoutOutcomeStatus.Ambiguous,
        AutomaticRaidShoutoutResultCode.Ambiguous
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.NotReady,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.NotReady
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.AuthorityRequired,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.AuthorityRequired
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.Cooldown,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.Cooldown
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.Invalid,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.Invalid
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.Rejected,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.Rejected
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.RateLimited,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.RateLimited
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.PartialFailure,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.PartialFailure
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.Unexpected,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.Unexpected
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.Delivered,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.Unexpected
    )]
    [Arguments(
        DeliveryResultShape.NotDelivered,
        AutomaticRaidShoutoutResultCode.Ambiguous,
        AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
        AutomaticRaidShoutoutResultCode.Unexpected
    )]
    public async Task DeliveryResultMapping_PersistsEveryStableTerminalDistinction(
        DeliveryResultShape shape,
        AutomaticRaidShoutoutResultCode inputCode,
        AutomaticRaidShoutoutOutcomeStatus expectedStatus,
        AutomaticRaidShoutoutResultCode expectedCode
    )
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1);
        AutomaticRaidShoutoutDeliveryResult result = shape switch
        {
            DeliveryResultShape.Delivered => new AutomaticRaidShoutoutDeliveryResult.Delivered(),
            DeliveryResultShape.Ambiguous => new AutomaticRaidShoutoutDeliveryResult.Ambiguous(),
            DeliveryResultShape.NotDelivered =>
                new AutomaticRaidShoutoutDeliveryResult.NotDelivered(inputCode),
            _ => throw new InvalidOperationException("Unsupported test delivery result."),
        };

        await Observer(factory, new RecordingDelivery(result))
            .IncomingRaidReceivedAsync(Raid("result-mapping", _now, 1), CancellationToken.None);

        await using var verification = await factory.CreateDbContextAsync();
        var outcome = await verification.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(expectedStatus);
        outcome.ResultCode.ShouldBe(expectedCode);
    }

    [Test]
    public async Task SameProviderIdentity_IsIndependentAcrossHosts()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1, login: "host");
        await SeedAsync(factory, enabled: true, threshold: 1, login: "other");
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Ambiguous());

        await Observer(factory, delivery)
            .IncomingRaidReceivedAsync(Raid("same", _now, 1, "host"), CancellationToken.None);
        await Observer(factory, delivery)
            .IncomingRaidReceivedAsync(Raid("same", _now, 1, "other"), CancellationToken.None);

        delivery.Requests.Count.ShouldBe(2);
        delivery.Requests.Select(request => request.HostLogin).ShouldBe(["host", "other"]);
    }

    [Test]
    public async Task NativeDisabled_LoadsSettingsButDoesNotClaimOrDeliver()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: true, threshold: 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures &= ~HostFeatureFlags.NativeTwitch;
            await db.SaveChangesAsync();
        }
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        await Observer(factory, delivery)
            .IncomingRaidReceivedAsync(Raid("disabled-native", _now, 1), CancellationToken.None);

        delivery.Requests.ShouldBeEmpty();
        await using var verification = await factory.CreateDbContextAsync();
        (await verification.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(0);
        (await verification.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ExpiredClaimsArePrunedOnlyOnFreshEligibleWorkAndOldReplayRemainsStale()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: true, threshold: 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AutomaticRaidProcessedEvents.Add(
                new AutomaticRaidProcessedEvent
                {
                    HostId = hostId,
                    ProviderMessageId = "expired",
                    ClaimedAtUtc = _now.AddMinutes(-4).UtcDateTime,
                    ExpiresAtUtc = _now.AddMinutes(-2).UtcDateTime,
                }
            );
            await db.SaveChangesAsync();
        }
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        var observer = Observer(factory, delivery);

        await observer.IncomingRaidReceivedAsync(
            Raid("stale-replay", _now.AddMinutes(-3), 1),
            CancellationToken.None
        );
        await using (var beforeFresh = await factory.CreateDbContextAsync())
        {
            (
                await beforeFresh.AutomaticRaidProcessedEvents.AnyAsync(value =>
                    value.ProviderMessageId == "expired"
                )
            ).ShouldBeTrue();
        }
        await observer.IncomingRaidReceivedAsync(Raid("fresh", _now, 1), CancellationToken.None);

        delivery.Requests.Select(request => request.ProviderMessageId).ShouldBe(["fresh"]);
        await using var verification = await factory.CreateDbContextAsync();
        (
            await verification.AutomaticRaidProcessedEvents.AnyAsync(value =>
                value.ProviderMessageId == "expired"
            )
        ).ShouldBeFalse();
    }

    [Test]
    public async Task RetentionEvictsOldestTerminalButKeepsClaimAndNewest100InOrder()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: true, threshold: 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AutomaticRaidShoutoutOutcomes.Add(
                Outcome(
                    hostId,
                    "processing",
                    AutomaticRaidShoutoutOutcomeStatus.Processing,
                    null,
                    null
                )
            );
            db.AutomaticRaidShoutoutOutcomes.Add(
                Outcome(
                    hostId,
                    "ambiguous",
                    AutomaticRaidShoutoutOutcomeStatus.Ambiguous,
                    AutomaticRaidShoutoutResultCode.Ambiguous,
                    _now.UtcDateTime
                )
            );
            await db.SaveChangesAsync();
        }
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        var observer = Observer(factory, delivery);
        for (var index = 0; index < 101; index++)
        {
            await observer.IncomingRaidReceivedAsync(
                Raid($"terminal-{index}", _now, 1),
                CancellationToken.None
            );
        }

        await using var verification = await factory.CreateDbContextAsync();
        var retainedTerminalIds = await verification
            .AutomaticRaidShoutoutOutcomes.Where(value =>
                value.Status == AutomaticRaidShoutoutOutcomeStatus.Delivered
                || value.Status == AutomaticRaidShoutoutOutcomeStatus.NotDelivered
            )
            .OrderByDescending(value => value.CompletedAtUtc)
            .ThenByDescending(value => value.Id)
            .Select(value => value.ProviderMessageId)
            .ToArrayAsync();
        retainedTerminalIds.ShouldBe(
            Enumerable.Range(1, 100).Reverse().Select(index => $"terminal-{index}")
        );
        retainedTerminalIds.ShouldNotContain("terminal-0");
        (
            await verification.AutomaticRaidShoutoutOutcomes.CountAsync(value =>
                value.Status == AutomaticRaidShoutoutOutcomeStatus.Processing
                || value.Status == AutomaticRaidShoutoutOutcomeStatus.Ambiguous
            )
        ).ShouldBe(2);
        (
            await verification.AutomaticRaidProcessedEvents.AnyAsync(value =>
                value.HostId == hostId && value.ProviderMessageId == "terminal-0"
            )
        ).ShouldBeTrue();
        delivery.Requests.Count.ShouldBe(101);

        await observer.IncomingRaidReceivedAsync(
            Raid("terminal-0", _now, 1),
            CancellationToken.None
        );

        delivery.Requests.Count.ShouldBe(101);
        (
            await verification.AutomaticRaidShoutoutOutcomes.AnyAsync(value =>
                value.ProviderMessageId == "terminal-0"
            )
        ).ShouldBeFalse();
    }

    private static AutomaticRaidShoutoutObserver Observer(
        IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext> factory,
        IAutomaticRaidShoutoutDelivery delivery
    )
    {
        return new(factory, delivery, new FixedTimeProvider(_now));
    }

    private static EventSubIncomingRaidEvent Raid(
        string messageId,
        DateTimeOffset timestamp,
        int viewers,
        string target = "host"
    )
    {
        return new(
            messageId,
            timestamp,
            "raider-id",
            "raider",
            "Raider",
            $"{target}-id",
            target,
            target,
            viewers
        );
    }

    private static async Task<int> SeedAsync(
        IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext> factory,
        bool enabled,
        int threshold,
        string login = "host"
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = _now.UtcDateTime,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        db.AutomaticRaidShoutoutSettings.Add(
            new AutomaticRaidShoutoutSettings
            {
                HostId = host.Id,
                Enabled = enabled,
                MinimumViewerCount = threshold,
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SetEnabledAsync(
        SqliteBlokeBotDbFactory factory,
        int hostId,
        bool enabled
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        var settings = await db.AutomaticRaidShoutoutSettings.SingleAsync(value =>
            value.HostId == hostId
        );
        settings.Enabled = enabled;
        await db.SaveChangesAsync();
    }

    private static AutomaticRaidShoutoutOutcome Outcome(
        int hostId,
        string providerMessageId,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode? resultCode,
        DateTime? completedAtUtc
    )
    {
        return new()
        {
            HostId = hostId,
            ProviderMessageId = providerMessageId,
            SourceTwitchUserId = "raider-id",
            SourceLogin = "raider",
            SourceDisplayName = "Raider",
            ViewerCount = 1,
            Status = status,
            ResultCode = resultCode,
            MessageTimestampUtc = _now.UtcDateTime,
            ClaimedAtUtc = _now.UtcDateTime,
            CompletedAtUtc = completedAtUtc,
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class SignalingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        internal TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow()
        {
            ReadStarted.TrySetResult();
            return now;
        }
    }

    private sealed class RecordingDelivery(AutomaticRaidShoutoutDeliveryResult result)
        : IAutomaticRaidShoutoutDelivery
    {
        internal List<AutomaticRaidShoutoutDeliveryRequest> Requests { get; } = [];

        public Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class ClaimInsertCoordination
    {
        private int _insertCount;

        internal TaskCompletionSource FirstInsertStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseFirstInsert { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async ValueTask BeforeInsertAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _insertCount) != 1)
            {
                return;
            }

            FirstInsertStarted.TrySetResult();
            await ReleaseFirstInsert.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingClaimInsertInterceptor(ClaimInsertCoordination coordination)
        : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains(
                    "INSERT OR IGNORE INTO automatic_raid_processed_events",
                    StringComparison.Ordinal
                )
            )
            {
                await coordination.BeforeInsertAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class CoordinatedSqliteDbFactory
        : IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext>,
            IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly DbContextOptions<BlokeBot.Persistence.BlokeBotDbContext> _options;

        private CoordinatedSqliteDbFactory(
            SqliteConnection keeper,
            DbContextOptions<BlokeBot.Persistence.BlokeBotDbContext> options
        )
        {
            _keeper = keeper;
            _options = options;
        }

        internal static async Task<CoordinatedSqliteDbFactory> CreateAsync(
            ClaimInsertCoordination coordination
        )
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"AutomaticRaidClaimTests-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 0,
            }.ToString();
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var options = new DbContextOptionsBuilder<BlokeBot.Persistence.BlokeBotDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(new BlockingClaimInsertInterceptor(coordination))
                .Options;
            var factory = new CoordinatedSqliteDbFactory(keeper, options);
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            return factory;
        }

        public BlokeBot.Persistence.BlokeBotDbContext CreateDbContext()
        {
            return new(_options);
        }

        public ValueTask<BlokeBot.Persistence.BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(CreateDbContext());
        }

        public async ValueTask DisposeAsync()
        {
            await _keeper.DisposeAsync();
        }
    }

    private sealed class ThrowingDelivery : IAutomaticRaidShoutoutDelivery
    {
        public Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("interrupted after the durable claim");
        }
    }

    public enum DeliveryResultShape
    {
        Delivered,
        Ambiguous,
        NotDelivered,
    }
}

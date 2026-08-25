using System.Data.Common;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutRunnerTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DisabledBelowThresholdStaleAndMissingIdentity_DoNotClaimOrDeliver()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var disabled = await SeedAsync(factory, enabled: false, threshold: 10);
        var enabled = disabled with
        {
            Configuration = disabled.Configuration with { Enabled = true },
        };
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        await RunAsync(factory, delivery, disabled, Raid("disabled", _now, 20));
        await RunAsync(factory, delivery, enabled, Raid("below", _now, 9));
        await RunAsync(
            factory,
            delivery,
            enabled,
            Raid("stale", _now.AddMinutes(-2).AddTicks(-1), 20)
        );
        await RunAsync(factory, delivery, enabled, Raid("", _now, 20));

        delivery.Requests.ShouldBeEmpty();
        await using var db = await factory.CreateDbContextAsync();
        (await db.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(0);
        (await db.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ExactlyTwoMinutesOld_ClaimsBeforeOneTypedDeliveryAndPersistsMappedResult()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(
            new AutomaticRaidShoutoutDeliveryResult.NotDelivered(
                AutomaticRaidShoutoutResultCode.Rejected
            )
        );

        await RunAsync(factory, delivery, seeded, Raid("boundary", _now.AddMinutes(-2), 1));

        delivery.Requests.ShouldHaveSingleItem().ProviderMessageId.ShouldBe("boundary");
        await using var db = await factory.CreateDbContextAsync();
        var claim = await db.AutomaticRaidProcessedEvents.SingleAsync();
        claim.ExpiresAtUtc.ShouldBe(_now.UtcDateTime);
        var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
    }

    [Test]
    public async Task DeliveryTerminalCallback_IsNotOverwrittenByQueueAdmissionResult()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);

        await RunAsync(
            factory,
            new TerminalCallbackDelivery(factory),
            seeded,
            Raid("terminal-callback", _now, 1)
        );

        await using var db = await factory.CreateDbContextAsync();
        var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
    }

    [Test]
    public async Task SequentialAndRestartDuplicate_UsesDurableHostScopedClaimOnce()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        var raid = Raid("duplicate", _now, 1);

        await RunAsync(factory, delivery, seeded, raid);
        await RunAsync(factory, delivery, seeded, raid);

        delivery.Requests.Count.ShouldBe(1);
        await using var db = await factory.CreateDbContextAsync();
        (await db.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(1);
        (await db.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task UnrelatedWriterContention_DoesNotSilentlySuppressDistinctRaid()
    {
        var deleteFailure = new ProcessedEventDeleteFailureObserver();
        await using var factory = await InterceptedSqliteDbFactory.CreateAsync(
            deleteFailure,
            new DeferredSqliteTransactionInterceptor()
        );
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        await using var writer = await factory.CreateDbContextAsync();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        _ = writer.AutomaticRaidShoutoutOutcomes.Add(
            Outcome(
                seeded.Host.Id,
                "held-writer",
                AutomaticRaidShoutoutOutcomeStatus.Processing,
                null,
                null
            )
        );
        _ = await writer.SaveChangesAsync();
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        var observation = Task.Run(() =>
            RunAsync(factory, delivery, seeded, Raid("distinct-under-lock", _now, 1))
        );
        try
        {
            _ = await deleteFailure.Failure.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await transaction.CommitAsync();
        }
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
        var contention = new PersistentProcessedEventDeleteContention();
        await using var factory = await InterceptedSqliteDbFactory.CreateAsync(contention);
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        var exception = await Should.ThrowAsync<SqliteException>(() =>
            RunAsync(factory, delivery, seeded, Raid("contention-exhausted", _now, 1))
        );

        exception.SqliteErrorCode.ShouldBe(SQLitePCL.raw.SQLITE_BUSY);
        contention.MatchedDispatches.ShouldBe(
            AutomaticRaidShoutoutRunner.ClaimContentionMaximumAttempts
        );
        delivery.Requests.ShouldBeEmpty();
        await using var verification = await factory.CreateDbContextAsync();
        (await verification.AutomaticRaidProcessedEvents.CountAsync()).ShouldBe(0);
        (await verification.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task CrashOrAmbiguousProcessing_IsVisibleAndNeverReplayed()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        var throwing = new ThrowingDelivery();
        var raid = Raid("crash", _now, 1);
        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
            RunAsync(factory, throwing, seeded, raid)
        );
        var replacement = new RecordingDelivery(
            new AutomaticRaidShoutoutDeliveryResult.Delivered()
        );

        await RunAsync(factory, replacement, seeded, raid);

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
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Ambiguous());
        var raid = Raid("ambiguous-replay", _now, 1);

        await RunAsync(factory, delivery, seeded, raid);
        await RunAsync(factory, delivery, seeded, raid);

        delivery.Requests.Count.ShouldBe(1);
        await using var verification = await factory.CreateDbContextAsync();
        var outcome = await verification.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Ambiguous);
        outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Ambiguous);
    }

    [Test]
    [Arguments(
        DeliveryResultShape.Queued,
        AutomaticRaidShoutoutResultCode.NotReady,
        AutomaticRaidShoutoutOutcomeStatus.Queued,
        AutomaticRaidShoutoutResultCode.Queued
    )]
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
        AutomaticRaidShoutoutResultCode.Unexpected
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
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        AutomaticRaidShoutoutDeliveryResult result = shape switch
        {
            DeliveryResultShape.Queued => new AutomaticRaidShoutoutDeliveryResult.Queued(),
            DeliveryResultShape.Delivered => new AutomaticRaidShoutoutDeliveryResult.Delivered(),
            DeliveryResultShape.Ambiguous => new AutomaticRaidShoutoutDeliveryResult.Ambiguous(),
            DeliveryResultShape.NotDelivered =>
                new AutomaticRaidShoutoutDeliveryResult.NotDelivered(inputCode),
            _ => throw new InvalidOperationException("Unsupported test delivery result."),
        };

        await RunAsync(
            factory,
            new RecordingDelivery(result),
            seeded,
            Raid("result-mapping", _now, 1)
        );

        await using var verification = await factory.CreateDbContextAsync();
        var outcome = await verification.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(expectedStatus);
        outcome.ResultCode.ShouldBe(expectedCode);
    }

    [Test]
    public async Task SameProviderIdentity_IsIndependentAcrossHosts()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var first = await SeedAsync(factory, enabled: true, threshold: 1, login: "host");
        var second = await SeedAsync(factory, enabled: true, threshold: 1, login: "other");
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Ambiguous());

        await RunAsync(factory, delivery, first, Raid("same", _now, 1, "host"));
        await RunAsync(factory, delivery, second, Raid("same", _now, 1, "other"));

        delivery.Requests.Count.ShouldBe(2);
        delivery.Requests.Select(static request => request.HostLogin).ShouldBe(["host", "other"]);
    }

    [Test]
    public async Task ExpiredClaimsArePrunedOnlyOnFreshEligibleWorkAndOldReplayRemainsStale()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            _ = db.AutomaticRaidProcessedEvents.Add(
                new AutomaticRaidProcessedEvent
                {
                    HostId = seeded.Host.Id,
                    ProviderMessageId = "expired",
                    ClaimedAtUtc = _now.AddMinutes(-4).UtcDateTime,
                    ExpiresAtUtc = _now.AddMinutes(-2).UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());

        await RunAsync(factory, delivery, seeded, Raid("stale-replay", _now.AddMinutes(-3), 1));
        await using (var beforeFresh = await factory.CreateDbContextAsync())
        {
            (
                await beforeFresh.AutomaticRaidProcessedEvents.AnyAsync(static value =>
                    value.ProviderMessageId == "expired"
                )
            ).ShouldBeTrue();
        }
        await RunAsync(factory, delivery, seeded, Raid("fresh", _now, 1));

        delivery.Requests.Select(static request => request.ProviderMessageId).ShouldBe(["fresh"]);
        await using var verification = await factory.CreateDbContextAsync();
        (
            await verification.AutomaticRaidProcessedEvents.AnyAsync(static value =>
                value.ProviderMessageId == "expired"
            )
        ).ShouldBeFalse();
    }

    [Test]
    public async Task RetentionEvictsOldestTerminalButKeepsClaimAndNewest100InOrder()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(factory, enabled: true, threshold: 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            _ = db.AutomaticRaidShoutoutOutcomes.Add(
                Outcome(
                    seeded.Host.Id,
                    "processing",
                    AutomaticRaidShoutoutOutcomeStatus.Processing,
                    null,
                    null
                )
            );
            _ = db.AutomaticRaidShoutoutOutcomes.Add(
                Outcome(
                    seeded.Host.Id,
                    "ambiguous",
                    AutomaticRaidShoutoutOutcomeStatus.Ambiguous,
                    AutomaticRaidShoutoutResultCode.Ambiguous,
                    _now.UtcDateTime
                )
            );
            _ = await db.SaveChangesAsync();
        }
        var delivery = new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered());
        for (var index = 0; index < 101; index++)
        {
            await RunAsync(factory, delivery, seeded, Raid($"terminal-{index}", _now, 1));
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
                value.HostId == seeded.Host.Id && value.ProviderMessageId == "terminal-0"
            )
        ).ShouldBeTrue();
        delivery.Requests.Count.ShouldBe(101);

        await RunAsync(factory, delivery, seeded, Raid("terminal-0", _now, 1));

        delivery.Requests.Count.ShouldBe(101);
        (
            await verification.AutomaticRaidShoutoutOutcomes.AnyAsync(value =>
                value.ProviderMessageId == "terminal-0"
            )
        ).ShouldBeFalse();
    }

    private static async Task RunAsync(
        IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext> factory,
        IAutomaticRaidShoutoutDelivery delivery,
        SeededHost seeded,
        EventSubIncomingRaidEvent raid
    ) =>
        _ = await new AutomaticRaidShoutoutRunner(
            factory,
            delivery,
            new FixedTimeProvider(_now)
        ).RunAsync(seeded.Host, seeded.Configuration, raid, CancellationToken.None);

    private sealed record SeededHost(
        BotHost Host,
        AutomaticRaidShoutoutConfiguration Configuration
    );

    private static EventSubIncomingRaidEvent Raid(
        string messageId,
        DateTimeOffset timestamp,
        int viewers,
        string target = "host"
    ) =>
        new(
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

    private static async Task<SeededHost> SeedAsync(
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var settings = new AutomaticRaidShoutoutSettings
        {
            HostId = host.Id,
            Enabled = enabled,
            MinimumViewerCount = threshold,
            UpdatedAtUtc = _now.UtcDateTime,
        };
        _ = db.AutomaticRaidShoutoutSettings.Add(settings);
        _ = await db.SaveChangesAsync();
        return new(host, AutomaticRaidShoutoutConfiguration.From(settings));
    }

    private static AutomaticRaidShoutoutOutcome Outcome(
        int hostId,
        string providerMessageId,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode? resultCode,
        DateTime? completedAtUtc
    ) =>
        new()
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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

    private sealed class TerminalCallbackDelivery(
        IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext> factory
    ) : IAutomaticRaidShoutoutDelivery
    {
        public async Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        )
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleAsync(
                value =>
                    value.HostId == request.HostId
                    && value.ProviderMessageId == request.ProviderMessageId,
                cancellationToken
            );
            outcome.Status = AutomaticRaidShoutoutOutcomeStatus.NotDelivered;
            outcome.ResultCode = AutomaticRaidShoutoutResultCode.Rejected;
            outcome.CompletedAtUtc = _now.UtcDateTime;
            _ = await db.SaveChangesAsync(cancellationToken);
            return new AutomaticRaidShoutoutDeliveryResult.Queued();
        }
    }

    private sealed class ProcessedEventDeleteFailureObserver : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<SqliteException> _failure = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task<SqliteException> Failure => _failure.Task;

        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (
                !command.CommandText.Contains(
                    "DELETE FROM automatic_raid_processed_events",
                    StringComparison.Ordinal
                )
            )
            {
                return Task.CompletedTask;
            }

            try
            {
                var exception = eventData.Exception.ShouldBeOfType<SqliteException>();
                exception.SqliteErrorCode.ShouldBeOneOf(
                    SQLitePCL.raw.SQLITE_BUSY,
                    SQLitePCL.raw.SQLITE_LOCKED
                );
                _ = _failure.TrySetResult(exception);
            }
            catch (Exception exception)
            {
                _ = _failure.TrySetException(exception);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class PersistentProcessedEventDeleteContention : DbCommandInterceptor
    {
        private const string _processedEventCleanupCommand =
            "DELETE FROM automatic_raid_processed_events WHERE ExpiresAtUtc < @p0;";
        private int _matchedDispatches;

        internal int MatchedDispatches => Volatile.Read(ref _matchedDispatches);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                !string.Equals(
                    command.CommandText,
                    _processedEventCleanupCommand,
                    StringComparison.Ordinal
                )
            )
            {
                return ValueTask.FromResult(result);
            }

            _ = Interlocked.Increment(ref _matchedDispatches);
            throw new SqliteException(
                "Injected persistent processed-event cleanup contention.",
                SQLitePCL.raw.SQLITE_BUSY
            );
        }
    }

    private sealed class DeferredSqliteTransactionInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            var transaction = ((SqliteConnection)connection).BeginTransaction(
                eventData.IsolationLevel,
                deferred: true
            );
            return ValueTask.FromResult(
                InterceptionResult<DbTransaction>.SuppressWithResult(transaction)
            );
        }
    }

    private sealed class InterceptedSqliteDbFactory
        : IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext>,
            IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly DbContextOptions<BlokeBot.Persistence.BlokeBotDbContext> _options;

        private InterceptedSqliteDbFactory(
            SqliteConnection keeper,
            DbContextOptions<BlokeBot.Persistence.BlokeBotDbContext> options
        )
        {
            _keeper = keeper;
            _options = options;
        }

        internal static async Task<InterceptedSqliteDbFactory> CreateAsync(
            params IInterceptor[] interceptors
        )
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"AutomaticRaidContentionTests-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 0,
            }.ToString();
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var options = new DbContextOptionsBuilder<BlokeBot.Persistence.BlokeBotDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(interceptors)
                .Options;
            var factory = new InterceptedSqliteDbFactory(keeper, options);
            await using var db = await factory.CreateDbContextAsync();
            _ = await db.Database.EnsureCreatedAsync();
            return factory;
        }

        public BlokeBot.Persistence.BlokeBotDbContext CreateDbContext() => new(_options);

        public ValueTask<BlokeBot.Persistence.BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(CreateDbContext());

        public async ValueTask DisposeAsync() => await _keeper.DisposeAsync();
    }

    private sealed class ThrowingDelivery : IAutomaticRaidShoutoutDelivery
    {
        public Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("interrupted after the durable claim");
    }

    public enum DeliveryResultShape
    {
        Queued,
        Delivered,
        Ambiguous,
        NotDelivered,
    }
}

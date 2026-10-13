using System.Data.Common;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    private sealed class RecentClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static DashboardRecentViewers Recent(
        Fixture fixture,
        RecentClock clock,
        EventBus<DashboardRecentViewersChanged>? events = null
    ) =>
        new(
            fixture.Database,
            new(fixture.Database, fixture.Moderator),
            clock,
            events ?? TestEventBus.CreateContinueAndRecord<DashboardRecentViewersChanged>().Events,
            NullLogger<DashboardRecentViewers>.Instance
        );

    private static ChatMessage Chat(
        string id,
        string login,
        string channel = "#dashboard-owner",
        string? display = null
    ) =>
        new(
            login,
            channel,
            "message content never retained",
            "raw payload never retained",
            new Dictionary<string, string> { ["user-id"] = id, ["display-name"] = display ?? login }
        );

    [Test]
    public async Task DashboardRecentViewers_MigrationsRestartBoundsRepeatsAndDelayedObservation() =>
        await RecentMigrationRoundTripAsync(await PointProviderFixture.SqliteFileAsync());

    [Test, Explicit]
    public async Task DashboardRecentViewers_PostgreSqlMigrationsRestartBoundsRepeatsAndDelayedObservation() =>
        await RecentMigrationRoundTripAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task RecentMigrationRoundTripAsync(PointProviderFixture database)
    {
        await using (var current = database.CreateDbContext())
        {
            var connection = current.Database.GetConnectionString()!;
            var sqlite = current.Database.IsSqlite();
            _ = await current.Database.EnsureDeletedAsync();
            var options = new DbContextOptionsBuilder<BlokeBotDbContext>();
            _ = sqlite
                ? options.UseSqlite(connection)
                : options.UseNpgsql(
                    connection,
                    postgres => postgres.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
                );
            _ = options.AddInterceptors(new WeeklyAnnouncementMigrationInterceptor());
            await using var migrating = new BlokeBotDbContext(options.Options);
            await migrating.Database.MigrateAsync();
        }
        await using var fixture = await Fixture.CreateAsync(database);
        var clock = new RecentClock();
        var observer = Recent(fixture, clock);
        for (var i = 0; i < 35; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            await observer.MessageReceivedAsync(Chat($"id-{i:00}", $"viewer{i}"), _ct);
        }
        var loaded = Value(
            await Recent(fixture, clock).ReadAsync(fixture.Owner, fixture.Host, _ct)
        );
        loaded.Length.ShouldBe(30);
        loaded[0].TwitchUserId.ShouldBe("id-34");
        loaded[^1].TwitchUserId.ShouldBe("id-05");
        clock.Now = clock.Now.AddSeconds(1);
        await observer.MessageReceivedAsync(Chat("id-05", "renamed", display: "Renamed"), _ct);
        var newest = clock.Now;
        clock.Now = clock.Now.AddMinutes(-5);
        await observer.MessageReceivedAsync(Chat("id-05", "outdated", display: "Old name"), _ct);
        await observer.MessageReceivedAsync(Chat("older-id", "older"), _ct);
        var persisted = Value(
            await Recent(fixture, clock).ReadAsync(fixture.Owner, fixture.Host, _ct)
        );
        persisted.Length.ShouldBe(30);
        persisted[0].ShouldBe(new CustomCommandViewer("id-05", "renamed", "Renamed"));
        persisted.Any(v => v.TwitchUserId == "older-id").ShouldBeFalse();
        await using var db = database.CreateDbContext();
        (
            await db.RecentChannelChatters.SingleAsync(x => x.TwitchUserId == "id-05")
        ).ObservedAtUtc.ShouldBe(newest.UtcDateTime);
        (await db.RecentChannelChatters.CountAsync()).ShouldBe(30);
        (await db.ViewerPassports.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DashboardRecentViewers_ConcurrentInstancesHaveDeterministicTiesAndHostIsolation() =>
        await ConcurrentRecentViewersAsync(await PointProviderFixture.SqliteFileAsync());

    [Test, Explicit]
    public async Task DashboardRecentViewers_PostgreSqlConcurrentInstancesHaveDeterministicTiesAndHostIsolation() =>
        await ConcurrentRecentViewersAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task ConcurrentRecentViewersAsync(PointProviderFixture database)
    {
        await using var fixture = await Fixture.CreateAsync(database);
        var clock = new RecentClock();
        await Task.WhenAll(
            Enumerable
                .Range(0, 45)
                .Select(i =>
                    Task.Run(async () =>
                        await Recent(fixture, clock)
                            .MessageReceivedAsync(Chat($"id-{i:00}", $"viewer{i}"), _ct)
                    )
                )
        );
        var selected = Value(
            await Recent(fixture, clock).ReadAsync(fixture.Owner, fixture.Host, _ct)
        );
        selected
            .Select(v => v.TwitchUserId)
            .ShouldBe(Enumerable.Range(0, 30).Select(i => $"id-{i:00}"));
        await Recent(fixture, clock)
            .MessageReceivedAsync(Chat("other-id", "other", "other-dashboard-owner"), _ct);
        Value(await Recent(fixture, clock).ReadAsync(Session(fixture.Other), fixture.Other, _ct))
            .Single()
            .TwitchUserId.ShouldBe("other-id");
        _ = (
            await Recent(fixture, clock).ReadAsync(fixture.Owner, fixture.Other, _ct)
        ).ShouldBeOfType<DashboardResult<System.Collections.Immutable.ImmutableArray<CustomCommandViewer>>.Rejected>();
        selected.Length.ShouldBe(30);
    }

    [Test]
    public async Task DashboardRecentViewers_FailedEvictionRollsBackWithoutNotification()
    {
        var failure = new RecentWriteFailure();
        await FailedRecentEvictionAsync(
            await PointProviderFixture.SqliteFileAsync(failure),
            failure
        );
    }

    [Test, Explicit]
    public async Task DashboardRecentViewers_PostgreSqlFailedEvictionRollsBackWithoutNotification()
    {
        var failure = new RecentWriteFailure();
        await FailedRecentEvictionAsync(
            await PointProviderFixture.PostgreSqlAsync(failure),
            failure
        );
    }

    private static async Task FailedRecentEvictionAsync(
        PointProviderFixture database,
        RecentWriteFailure failure
    )
    {
        await using var fixture = await Fixture.CreateAsync(database);
        var clock = new RecentClock();
        var events = TestEventBus.CreateContinueAndRecord<DashboardRecentViewersChanged>().Events;
        var observer = Recent(fixture, clock, events);
        for (var i = 0; i < 30; i++)
        {
            await observer.MessageReceivedAsync(Chat($"id-{i:00}", $"viewer{i}"), _ct);
        }
        int notifications = 0;
        using var subscription = observer.Subscribe(
            fixture.Host,
            ObserverIdentity.Named("commit-boundary"),
            (_, _) =>
            {
                notifications++;
                return ValueTask.CompletedTask;
            }
        );
        failure.Fail = true;
        clock.Now = clock.Now.AddSeconds(1);
        var exception = await Should.ThrowAsync<DbUpdateException>(() =>
            observer.MessageReceivedAsync(Chat("new-id", "new"), _ct).AsTask()
        );
        _ = exception.InnerException.ShouldBeOfType<IOException>();
        failure.Fail = false;
        var stored = Value(await observer.ReadAsync(fixture.Owner, fixture.Host, _ct));
        stored.Length.ShouldBe(30);
        stored.Any(v => v.TwitchUserId == "new-id").ShouldBeFalse();
        stored.Any(v => v.TwitchUserId == "id-29").ShouldBeTrue();
        notifications.ShouldBe(0);
    }

    [Test]
    public async Task DashboardRecentViewers_PrivacyWithoutPassportClearsOnlyStableIdentityAndSelection() =>
        await RecentViewerPrivacyAsync(await PointProviderFixture.SqliteFileAsync());

    [Test, Explicit]
    public async Task DashboardRecentViewers_PostgreSqlPrivacyWithoutPassportClearsOnlyStableIdentityAndSelection() =>
        await RecentViewerPrivacyAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task RecentViewerPrivacyAsync(PointProviderFixture database)
    {
        await using var fixture = await Fixture.CreateAsync(database);
        var clock = new RecentClock();
        var recent = Recent(fixture, clock);
        await recent.MessageReceivedAsync(Chat("subject-id", "reused"), _ct);
        await recent.MessageReceivedAsync(Chat("different-id", "reused"), _ct);
        await recent.MessageReceivedAsync(
            Chat("subject-id", "reused", "other-dashboard-owner"),
            _ct
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Score",
            CustomValueScope.User,
            CustomValueKind.Number,
            "0"
        );
        using var selected = ValueReader(fixture, changes)
            .CreateSession(() => ValueTask.CompletedTask);
        await selected.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        await selected.SelectViewerAsync(new("subject-id", "reused"), _ct);
        (await DisplayAsync(selected)).Value.Target.ViewerId.ShouldBe("subject-id");
        await using (var db = fixture.Database.CreateDbContext())
        {
            var subject = PrivacySubject.Create("subject-id", null);
            var exported = await ViewerPrivacyService.ExportAsync(
                db,
                subject,
                fixture.Host.Value,
                _ct
            );
            exported
                .Sections["recent_channel_chatters"]
                .OfType<RecentChannelChatter>()
                .Single()
                .TwitchUserId.ShouldBe("subject-id");
            (
                await ViewerPrivacyService.EraseAsync(
                    db,
                    PrivacySubject.Create(null, "reused"),
                    null,
                    _ct
                )
            ).TotalChangedRows.ShouldBe(0);
            (await ViewerPrivacyService.EraseAsync(db, subject, fixture.Host.Value, _ct))
                .ChangedRows["recent_channel_chatters"]
                .ShouldBe(1);
        }
        await selected.RefreshAsync(_ct);
        selected.Viewer.ShouldBeNull();
        selected
            .State.ShouldBeOfType<DashboardValueState.Unavailable>()
            .Reason.ShouldBe(DashboardValueUnavailableReason.ViewerNotFound);
        Value(await recent.ReadAsync(fixture.Owner, fixture.Host, _ct))
            .Single()
            .TwitchUserId.ShouldBe("different-id");
        Value(await recent.ReadAsync(Session(fixture.Other), fixture.Other, _ct))
            .Single()
            .TwitchUserId.ShouldBe("subject-id");
        await using (var db = fixture.Database.CreateDbContext())
        {
            var subject = PrivacySubject.Create("subject-id", null);
            (await ViewerPrivacyService.EraseAsync(db, subject, null, _ct))
                .ChangedRows["recent_channel_chatters"]
                .ShouldBe(1);
            (
                await ViewerPrivacyService.EraseAsync(db, subject, null, _ct)
            ).TotalChangedRows.ShouldBe(0);
            (await db.RecentChannelChatters.SingleAsync()).TwitchUserId.ShouldBe("different-id");
        }
    }

    [Test]
    public async Task DashboardRecentViewers_DelayedObserverCannotOverwriteNewerCommittedIdentity()
    {
        var window = new RecentLookupWindow();
        await DelayedRecentObservationAsync(
            await PointProviderFixture.SqliteFileAsync(window),
            window
        );
    }

    [Test, Explicit]
    public async Task DashboardRecentViewers_PostgreSqlDelayedObserverCannotOverwriteNewerCommittedIdentity()
    {
        var window = new RecentLookupWindow();
        await DelayedRecentObservationAsync(
            await PointProviderFixture.PostgreSqlAsync(window),
            window
        );
    }

    private static async Task DelayedRecentObservationAsync(
        PointProviderFixture database,
        RecentLookupWindow window
    )
    {
        await using var fixture = await Fixture.CreateAsync(database);
        var clock = new RecentClock();
        window.Arm();
        var delayed = Recent(fixture, clock)
            .MessageReceivedAsync(Chat("same-id", "outdated"), _ct)
            .AsTask();
        await window.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Now = clock.Now.AddSeconds(1);
        await Recent(fixture, clock)
            .MessageReceivedAsync(Chat("same-id", "latest", display: "Latest"), _ct);
        window.Release.TrySetResult().ShouldBeTrue();
        await delayed;
        Value(await Recent(fixture, clock).ReadAsync(fixture.Owner, fixture.Host, _ct))
            .Single()
            .ShouldBe(new CustomCommandViewer("same-id", "latest", "Latest"));
    }

    private sealed class RecentLookupWindow : DbCommandInterceptor
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            if (
                command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && (
                    command.CommandText.Contains("FROM \"hosts\"", StringComparison.Ordinal)
                    || command.CommandText.Contains("FROM hosts", StringComparison.Ordinal)
                )
                && Interlocked.CompareExchange(ref _armed, 0, 1) == 1
            )
            {
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class RecentWriteFailure : DbCommandInterceptor
    {
        internal bool Fail { get; set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData data,
            DbDataReader result,
            CancellationToken ct = default
        ) =>
            Fail
            && command.CommandText.Contains("recent_channel_chatters", StringComparison.Ordinal)
            && command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)
                ? throw new IOException("Injected failure after eviction, before commit")
                : ValueTask.FromResult(result);
    }
}

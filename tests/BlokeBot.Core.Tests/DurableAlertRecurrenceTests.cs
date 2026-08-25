using BlokeBot.Core.Features.Alerts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DurableAlertRecurrenceTests
{
    [Test]
    public async Task SameUnresolvedIssue_Recurring_RefreshesLatestDetailsAndPublishesAfterEachCommit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var firstOccurrence = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);
        var clock = new ManualTestTimeProvider(firstOccurrence);
        var events = TestEventBus.Create<AppEventKind>();
        var committedCounts = new List<int>();
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.DurableAlertRecurrence.CommitVisibility"),
            async (_, cancellationToken) =>
            {
                await using var verify = await database.CreateDbContextAsync(cancellationToken);
                committedCounts.Add(
                    await verify.DurableAlerts.SumAsync(
                        alert => alert.OccurrenceCount,
                        cancellationToken
                    )
                );
            }
        );
        var alerts = new DurableAlertService(database, clock, events);

        _ = await Report(
                alerts,
                hostId,
                DurableAlertSeverity.Info,
                "Initial title",
                "Initial detail",
                "/initial"
            )
            .RunAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        var outcome = await Report(
                alerts,
                hostId,
                DurableAlertSeverity.Critical,
                "Latest title",
                "Latest detail",
                "/latest"
            )
            .RunAsync(CancellationToken.None);

        _ = outcome.ShouldBeOfType<DurableAlertCreateOutcome.Existing>();
        var stored = await LoadSingleAsync(database);
        stored.OccurrenceCount.ShouldBe(2);
        stored.CreatedAtUtc.ShouldBe(firstOccurrence.UtcDateTime);
        stored.LastOccurredAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
        stored.Severity.ShouldBe(DurableAlertSeverity.Critical);
        stored.Title.ShouldBe("Latest title");
        stored.Message.ShouldBe("Latest detail");
        stored.LinkPath.ShouldBe("/latest");
        committedCounts.ShouldBe([1, 2]);
        (await alerts.CountActiveAsync(hostId, CancellationToken.None)).ShouldBe(1);
    }

    [Test]
    public async Task AcknowledgedIssue_Recurring_PreservesHistoryAndCreatesFreshActiveAlert()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var clock = new ManualTestTimeProvider(
            new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(database, clock, TestEventBus.Create<AppEventKind>());
        var first = await Report(
                alerts,
                hostId,
                DurableAlertSeverity.Warning,
                "First",
                "First detail",
                null
            )
            .RunAsync(CancellationToken.None);
        _ = await alerts
            .Acknowledge(hostId, first.Alert.Id, "streamer")
            .RunAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(10));

        _ = await Report(
                alerts,
                hostId,
                DurableAlertSeverity.Warning,
                "Recurred",
                "New detail",
                null
            )
            .RunAsync(CancellationToken.None);

        var state = await alerts.LoadStateAsync(hostId, CancellationToken.None);
        _ = state.Active.ShouldHaveSingleItem();
        state.Active[0].OccurrenceCount.ShouldBe(1);
        state.Active[0].Title.ShouldBe("Recurred");
        _ = state.History.ShouldHaveSingleItem();
        state.History[0].Id.ShouldBe(first.Alert.Id);
        state.History[0].Title.ShouldBe("First");
    }

    [Test]
    public async Task SameIssue_ReportedConcurrently_PersistsOneActiveAlertWithEveryOccurrence()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.DurableAlertRecurrence.Concurrent"),
            (_, _) =>
            {
                _ = Interlocked.Increment(ref notificationCount);
                return ValueTask.CompletedTask;
            }
        );
        var alerts = new DurableAlertService(database, TimeProvider.System, events);

        _ = await Task.WhenAll(
            Enumerable
                .Range(1, 8)
                .Select(index =>
                    Report(
                            alerts,
                            hostId,
                            DurableAlertSeverity.Warning,
                            $"Occurrence {index}",
                            $"Detail {index}",
                            null
                        )
                        .RunAsync(CancellationToken.None)
                        .AsTask()
                )
        );

        var stored = await LoadSingleAsync(database);
        stored.OccurrenceCount.ShouldBe(8);
        notificationCount.ShouldBe(8);
    }

    [Test]
    public async Task CancelledReport_Retrying_PersistsOnlyTheCommittedAttempt()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.DurableAlertRecurrence.Cancellation"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var alerts = new DurableAlertService(database, TimeProvider.System, events);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            Report(
                    alerts,
                    hostId,
                    DurableAlertSeverity.Warning,
                    "Cancelled",
                    "Cancelled detail",
                    null
                )
                .RunAsync(cancelled.Token)
                .AsTask()
        );
        _ = await Report(
                alerts,
                hostId,
                DurableAlertSeverity.Warning,
                "Retried",
                "Committed detail",
                null
            )
            .RunAsync(CancellationToken.None);

        var stored = await LoadSingleAsync(database);
        stored.OccurrenceCount.ShouldBe(1);
        stored.Title.ShouldBe("Retried");
        notificationCount.ShouldBe(1);
    }

    [Test]
    public async Task StagedRecurrence_CallerRollsBack_PreservesAlertAndPublishesNothing()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var alerts = new DurableAlertService(database, TimeProvider.System, events);
        _ = await Report(
                alerts,
                hostId,
                DurableAlertSeverity.Info,
                "Original",
                "Original detail",
                null
            )
            .RunAsync(CancellationToken.None);
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.DurableAlertRecurrence.Rollback"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var occurredAt = new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc);
        await using (var db = await database.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            _ = await alerts.StageReportAsync(
                db,
                new DurableAlertReport(
                    new DurableAlertIdentity(hostId, "test-source", "same-issue"),
                    DurableAlertSeverity.Warning,
                    "Rolled back",
                    "This change must not escape the transaction.",
                    null,
                    occurredAt
                ),
                CancellationToken.None
            );
            _ = await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        var stored = await LoadSingleAsync(database);
        stored.OccurrenceCount.ShouldBe(1);
        stored.Title.ShouldBe("Original");
        stored.Message.ShouldBe("Original detail");
        notificationCount.ShouldBe(0);
    }

    private static IO<DurableAlertCreateOutcome, Never> Report(
        DurableAlertService alerts,
        int hostId,
        DurableAlertSeverity severity,
        string title,
        string message,
        string? linkPath
    ) => alerts.Create(hostId, severity, "test-source", "same-issue", title, message, linkPath);

    private static async Task<DurableAlert> LoadSingleAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        return await db.DurableAlerts.SingleAsync();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}

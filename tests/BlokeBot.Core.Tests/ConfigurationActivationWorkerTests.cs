using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationActivationTests
{
    [Test]
    public async Task ConcurrentWorkers_ClaimOnePendingActivationExactlyOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Polls);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None
        );
        var observer = new RecordingObserver(TimeSpan.FromMilliseconds(100));
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var authority = Authority(observer, events, alerts);
        var first = Worker(database, queue, authority);
        var second = Worker(database, queue, authority);

        await first.StartAsync(CancellationToken.None);
        await second.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Complete);
        await first.StopAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);

        observer.Changes.ShouldBe([
            new(hostId, HostFeatureFlags.Polls, HostFeatureActivationState.Enabled),
        ]);
        await using var verify = await database.CreateDbContextAsync();
        var row = await verify.ConfigurationActivations.SingleAsync();
        row.AttemptCount.ShouldBe(1);
    }

    [Test]
    public async Task WorkerCancellation_ReturnsActivationToPendingAndRestartCompletesIt()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Polls);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None
        );
        var blocking = new BlockingObserver();
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var first = Worker(database, queue, Authority(blocking, events, alerts));
        await first.StartAsync(CancellationToken.None);
        queue.Wake();
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await first.StopAsync(CancellationToken.None);

        await using (var canceled = await database.CreateDbContextAsync())
        {
            var row = await canceled.ConfigurationActivations.SingleAsync();
            row.Status.ShouldBe(ConfigurationActivationStatus.Pending);
            var issue = PersistedIssues(row).ShouldHaveSingleItem();
            issue.Code.ShouldBe(HostFeatureActivationAuthority.CancellationCode);
            issue.Reason.ShouldNotBeNullOrWhiteSpace();
            (await canceled.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.Polls);
        }
        var complete = new RecordingObserver();
        var restarted = Worker(database, queue, Authority(complete, events, alerts));
        await restarted.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Complete);
        await restarted.StopAsync(CancellationToken.None);

        complete.Changes.Count.ShouldBe(1);
    }

    [Test]
    public async Task CancellationDuringTerminalPersistence_ReturnsActivationToPending()
    {
        var persistence = new TerminalOutcomePersistenceBarrier();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(persistence);
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Polls);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None
        );
        var observer = new RecordingObserver();
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var worker = Worker(database, queue, Authority(observer, events, alerts));
        persistence.Arm();

        await worker.StartAsync(CancellationToken.None);
        queue.Wake();
        await persistence.WaitUntilPausedAsync();
        await worker.StopAsync(CancellationToken.None);

        _ = observer.Changes.ShouldHaveSingleItem();
        await using var verify = await database.CreateDbContextAsync();
        var row = await verify.ConfigurationActivations.SingleAsync();
        row.Status.ShouldBe(ConfigurationActivationStatus.Pending);
        row.AttemptCount.ShouldBe(1);
        var issue = PersistedIssues(row).ShouldHaveSingleItem();
        issue.Code.ShouldBe(HostFeatureActivationAuthority.CancellationCode);
        issue.Reason.ShouldBe("Automatic feature activation was interrupted and will be retried.");
    }

    [Test]
    public async Task ExpiredProcessingLease_IsReclaimedOnceAfterRestart()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Polls);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None,
            DateTime.UtcNow.AddMinutes(-6)
        );
        await using (var interrupted = await database.CreateDbContextAsync())
        {
            var row = await interrupted.ConfigurationActivations.SingleAsync();
            row.Status = ConfigurationActivationStatus.Processing;
            row.AttemptCount = 1;
            row.Revision = 2;
            _ = await interrupted.SaveChangesAsync();
        }
        var observer = new RecordingObserver();
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var restarted = Worker(database, queue, Authority(observer, events, alerts));

        await restarted.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Complete);
        await restarted.StopAsync(CancellationToken.None);

        observer.Changes.Count.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ConfigurationActivations.SingleAsync()).AttemptCount.ShouldBe(2);
    }

    [Test]
    public async Task StaleLease_IsReclaimedBeforeSameHostPendingWorkWhileAnotherHostProceeds()
    {
        var claimSelection = new ClaimSelectionBarrier();
        var claimTransactions = new ClaimTransactionProbe();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            claimSelection,
            claimTransactions
        );
        var serializedHostId = await SeedHostAsync(database, "serialized", HostFeatureFlags.None);
        var otherHostId = await SeedHostAsync(database, "other", HostFeatureFlags.None);
        var staleId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            staleId,
            serializedHostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None,
            DateTime.UtcNow.AddMinutes(-6)
        );
        await SeedActivationAsync(
            database,
            pendingId,
            serializedHostId,
            HostFeatureFlags.Bingo,
            HostFeatureFlags.None,
            DateTime.UtcNow.AddMinutes(-7)
        );
        await SeedActivationAsync(
            database,
            otherId,
            otherHostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None
        );
        await using (var seed = await database.CreateDbContextAsync())
        {
            var stale = await seed.ConfigurationActivations.SingleAsync(x => x.Id == staleId);
            stale.Status = ConfigurationActivationStatus.Processing;
            stale.AttemptCount = 1;
            stale.Revision = 2;
            _ = await seed.SaveChangesAsync();
        }

        var observer = new HostLeaseObserver(serializedHostId, HostFeatureFlags.Polls);
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var authority = Authority(observer, events, alerts);
        using var first = Worker(database, queue, authority);
        using var second = Worker(database, queue, authority);
        claimSelection.Arm();
        claimTransactions.Arm();

        Exception? failure = null;
        try
        {
            await first.StartAsync(CancellationToken.None);
            await claimSelection.WaitUntilPausedAsync();
            await second.StartAsync(CancellationToken.None);
            await claimTransactions.WaitForSecondStartAsync();
            await Task.Delay(100);
            claimSelection.CandidateSelectCount.ShouldBe(1);
            claimSelection.Release();
            await observer.BlockedChangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            queue.Wake();
            await WaitForStatusAsync(database, otherId, ConfigurationActivationStatus.Complete);
            await using (var interleaved = await database.CreateDbContextAsync())
            {
                var rows = await interleaved
                    .ConfigurationActivations.Where(x =>
                        x.Id == staleId || x.Id == pendingId || x.Id == otherId
                    )
                    .ToDictionaryAsync(x => x.Id);
                rows[staleId].Status.ShouldBe(ConfigurationActivationStatus.Processing);
                rows[staleId].AttemptCount.ShouldBe(2);
                rows[pendingId].Status.ShouldBe(ConfigurationActivationStatus.Pending);
                rows[pendingId].AttemptCount.ShouldBe(0);
                rows[otherId].Status.ShouldBe(ConfigurationActivationStatus.Complete);
            }
            observer.ReleaseBlockedChange();

            await WaitForStatusAsync(database, staleId, ConfigurationActivationStatus.Complete);
            await WaitForStatusAsync(database, pendingId, ConfigurationActivationStatus.Complete);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            claimSelection.Release();
            observer.ReleaseBlockedChange();
            var stopped = Task.WhenAll(
                first.StopAsync(CancellationToken.None),
                second.StopAsync(CancellationToken.None)
            );
            await stopped.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            var completed = Task.WhenAll(
                first.ExecuteTask ?? Task.CompletedTask,
                second.ExecuteTask ?? Task.CompletedTask
            );
            await completed.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            var cleanupErrors = new[] { stopped.Exception, completed.Exception }
                .OfType<Exception>()
                .ToArray();
            if (cleanupErrors.Length > 0)
            {
                var cleanupFailure = new AggregateException(
                    "Stale-lease workers failed during cleanup.",
                    cleanupErrors
                );
                if (failure is null)
                {
                    throw cleanupFailure;
                }
                Console.Error.WriteLine(cleanupFailure);
            }
        }

        observer.MaximumSerializedHostConcurrency.ShouldBe(1);
        observer
            .Changes.Where(change => change.HostId == serializedHostId)
            .Select(change => change.Feature)
            .ShouldBe([HostFeatureFlags.Polls, HostFeatureFlags.Bingo]);
        observer.Changes.ShouldContain(change => change.HostId == otherHostId);
    }

    [Test]
    public async Task NewerOppositeChange_WaitsForProcessingChangeAndRunsAfterIt()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Bingo);
        var enabledId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            enabledId,
            hostId,
            HostFeatureFlags.Bingo,
            HostFeatureFlags.None,
            DateTime.UtcNow.AddMinutes(-1)
        );
        var observer = new GateObserver();
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var authority = Authority(observer, events, alerts);
        var first = Worker(database, queue, authority);
        var second = Worker(database, queue, authority);
        await first.StartAsync(CancellationToken.None);
        await second.StartAsync(CancellationToken.None);
        queue.Wake();
        await observer.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disabledId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            disabledId,
            hostId,
            HostFeatureFlags.None,
            HostFeatureFlags.Bingo,
            DateTime.UtcNow
        );
        queue.Wake();
        await Task.Delay(100);
        observer.Changes.Count.ShouldBe(1);
        _ = observer.ReleaseFirst.TrySetResult();

        await WaitForStatusAsync(database, enabledId, ConfigurationActivationStatus.Complete);
        await WaitForStatusAsync(database, disabledId, ConfigurationActivationStatus.Complete);
        await first.StopAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);

        observer.Changes.ShouldBe([
            new(hostId, HostFeatureFlags.Bingo, HostFeatureActivationState.Enabled),
            new(hostId, HostFeatureFlags.Bingo, HostFeatureActivationState.Disabled),
        ]);
    }
}

using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationActivationTests
{
    [Test]
    public async Task InteractiveAndImportedChanges_RunTheSameOrderedAutomaticWork()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var interactiveHost = await SeedHostAsync(
            database,
            "interactive",
            HostFeatureFlags.Automations
        );
        var importedHost = await SeedHostAsync(database, "imported", HostFeatureFlags.Automations);
        var interactiveObserver = new RecordingObserver();
        var importObserver = new RecordingObserver();
        var interactiveEvents = TestEventBus.Create<AppEventKind>();
        var importEvents = TestEventBus.Create<AppEventKind>();
        using var interactiveAlerts = new DurableAlertService(
            database,
            TimeProvider.System,
            interactiveEvents
        );
        using var importAlerts = new DurableAlertService(
            database,
            TimeProvider.System,
            importEvents
        );
        var interactiveAuthority = Authority(
            interactiveObserver,
            interactiveEvents,
            interactiveAlerts
        );
        var featureService = new HostFeatureService(database, interactiveAuthority);

        _ = await featureService.DisableAsync(
            interactiveHost,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        _ = await featureService.EnableAsync(
            interactiveHost,
            HostFeatureFlags.Polls,
            CancellationToken.None
        );
        _ = await featureService.EnableAsync(
            interactiveHost,
            HostFeatureFlags.CommunityProgression,
            CancellationToken.None
        );
        _ = await featureService.EnableAsync(
            interactiveHost,
            HostFeatureFlags.Bingo,
            CancellationToken.None
        );

        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            importedHost,
            HostFeatureFlags.Polls | HostFeatureFlags.Bingo | HostFeatureFlags.CommunityProgression,
            HostFeatureFlags.Automations
        );
        var queue = new ConfigurationActivationQueue();
        var worker = Worker(database, queue, Authority(importObserver, importEvents, importAlerts));
        await worker.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Complete);
        await worker.StopAsync(CancellationToken.None);

        var interactiveChanges = interactiveObserver
            .Changes.Select(change => (change.Feature, change.State))
            .ToArray();
        var importedChanges = importObserver
            .Changes.Select(change => (change.Feature, change.State))
            .ToArray();
        importedChanges.ShouldBe(interactiveChanges);
        importedChanges.ShouldBe([
            (HostFeatureFlags.Automations, HostFeatureActivationState.Disabled),
            (HostFeatureFlags.Polls, HostFeatureActivationState.Enabled),
            (HostFeatureFlags.CommunityProgression, HostFeatureActivationState.Enabled),
            (HostFeatureFlags.Bingo, HostFeatureActivationState.Enabled),
        ]);
    }

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
    public async Task AutomaticWorkFailure_KeepsImportedStateAndRetriesWithAStableSafeReason()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Bingo);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Bingo,
            HostFeatureFlags.None
        );
        var observer = new MutableObserver { Throw = true };
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var authority = Authority(observer, events, alerts);
        var first = Worker(database, queue, authority);

        await first.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Failed);
        await first.StopAsync(CancellationToken.None);

        await using (var failed = await database.CreateDbContextAsync())
        {
            var row = await failed.ConfigurationActivations.SingleAsync();
            var issue = PersistedIssues(row).ShouldHaveSingleItem();
            issue.Code.ShouldBe(HostFeatureActivationAuthority.AutomaticWorkFailureCode);
            issue.Reason.ShouldNotContain("planned private detail");
            issue.Reason.ShouldNotContain(nameof(InvalidOperationException));
            (await failed.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.Bingo);
        }
        var service = new ConfigurationActivationService(database, queue, TimeProvider.System);
        (await service.RetryAsync(999, activationId, CancellationToken.None)).ShouldBeFalse();
        (await service.RetryAsync(hostId, activationId, CancellationToken.None)).ShouldBeTrue();
        (await service.RetryAsync(hostId, activationId, CancellationToken.None)).ShouldBeFalse();
        observer.Throw = false;
        var retry = Worker(database, queue, authority);
        await retry.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Complete);
        await retry.StopAsync(CancellationToken.None);

        observer.Changes.Count.ShouldBe(2);
        var view = await service.LoadAsync(hostId, activationId, CancellationToken.None);
        view.ShouldNotBeNull().Issues.ShouldBeEmpty();
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
    public async Task ManualFollowUp_UsesOneStableDurableAlertAndCompletesOnlyAfterRetrySucceeds()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.Overlays);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Overlays,
            HostFeatureFlags.None
        );
        var observer = new MutableObserver { ManualFollowUp = true };
        var events = TestEventBus.Create<AppEventKind>();
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var queue = new ConfigurationActivationQueue();
        var authority = Authority(observer, events, alerts);
        var worker = Worker(database, queue, authority);
        await worker.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(
            database,
            activationId,
            ConfigurationActivationStatus.ManualFollowUp
        );
        await worker.StopAsync(CancellationToken.None);

        var service = new ConfigurationActivationService(database, queue, TimeProvider.System);
        var manual = await service.LoadAsync(hostId, activationId, CancellationToken.None);
        manual
            .ShouldNotBeNull()
            .Issues.ShouldBe([
                new ConfigurationActivationIssue(
                    MutableObserver.ManualCode,
                    "Reconnect the required provider, then retry automatic activation."
                ),
            ]);
        var firstAlert = (
            await alerts.LoadStateAsync(hostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();
        firstAlert.Source.ShouldBe(HostFeatureActivationAuthority.AlertSource);
        firstAlert.SourceKey.ShouldBe(MutableObserver.ManualKey);

        (await service.RetryAsync(hostId, activationId, CancellationToken.None)).ShouldBeTrue();
        var repeated = Worker(database, queue, authority);
        await repeated.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(
            database,
            activationId,
            ConfigurationActivationStatus.ManualFollowUp,
            minimumAttemptCount: 2
        );
        await repeated.StopAsync(CancellationToken.None);
        var recurrent = (
            await alerts.LoadStateAsync(hostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();
        recurrent.Id.ShouldBe(firstAlert.Id);
        recurrent.OccurrenceCount.ShouldBe(2);

        observer.ManualFollowUp = false;
        (await service.RetryAsync(hostId, activationId, CancellationToken.None)).ShouldBeTrue();
        var completed = Worker(database, queue, authority);
        await completed.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(database, activationId, ConfigurationActivationStatus.Complete);
        await completed.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task DistinctManualFollowUps_DoNotSuppressLaterAutomaticWorkOrNotification()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "destination", HostFeatureFlags.None);
        var activationId = Guid.NewGuid();
        await SeedActivationAsync(
            database,
            activationId,
            hostId,
            HostFeatureFlags.Polls,
            HostFeatureFlags.None
        );
        var automaticWork = new RecordingObserver();
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.ConfigurationActivation.ManualFollowUp"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        using var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var authority = Authority(
            [
                new FixedManualFollowUpObserver("provider-one", "provider:one"),
                automaticWork,
                new FixedManualFollowUpObserver("provider-two", "provider:two"),
            ],
            events,
            alerts
        );
        var queue = new ConfigurationActivationQueue();
        var worker = Worker(database, queue, authority);

        await worker.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForStatusAsync(
            database,
            activationId,
            ConfigurationActivationStatus.ManualFollowUp
        );
        await worker.StopAsync(CancellationToken.None);

        var view = await new ConfigurationActivationService(
            database,
            queue,
            TimeProvider.System
        ).LoadAsync(hostId, activationId, CancellationToken.None);
        view.ShouldNotBeNull()
            .Issues.ShouldBe([
                new("provider-one", "Complete provider-one."),
                new("provider-two", "Complete provider-two."),
            ]);
        automaticWork.Changes.ShouldBe([
            new(hostId, HostFeatureFlags.Polls, HostFeatureActivationState.Enabled),
        ]);
        notificationCount.ShouldBe(1);
        (await alerts.LoadStateAsync(hostId, CancellationToken.None))
            .Active.Select(alert => alert.SourceKey)
            .Order()
            .ShouldBe(["provider:one", "provider:two"]);
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

    private static HostFeatureActivationAuthority Authority(
        IHostFeatureActivationObserver observer,
        EventBus<AppEventKind> events,
        DurableAlertService alerts
    ) => Authority([observer], events, alerts);

    private static HostFeatureActivationAuthority Authority(
        IReadOnlyList<IHostFeatureActivationObserver> observers,
        EventBus<AppEventKind> events,
        DurableAlertService alerts
    ) =>
        new(
            observers,
            new HostedChannelChangeNotifier(events),
            alerts,
            NullLogger<HostFeatureActivationAuthority>.Instance
        );

    private static ConfigurationActivationWorker Worker(
        SqliteBlokeBotDbFactory database,
        ConfigurationActivationQueue queue,
        HostFeatureActivationAuthority authority
    ) =>
        new(
            database,
            queue,
            authority,
            TimeProvider.System,
            NullLogger<ConfigurationActivationWorker>.Instance
        );

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags enabled
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            EnabledFeatures = enabled,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedActivationAsync(
        SqliteBlokeBotDbFactory database,
        Guid activationId,
        int hostId,
        HostFeatureFlags enabled,
        HostFeatureFlags disabled,
        DateTime? updatedAt = null
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var now = updatedAt ?? DateTime.UtcNow;
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = (host.EnabledFeatures | enabled) & ~disabled;
        _ = db.ConfigurationActivations.Add(
            new()
            {
                Id = activationId,
                HostId = hostId,
                EnabledChanges = enabled,
                DisabledChanges = disabled,
                Status = ConfigurationActivationStatus.Pending,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task WaitForStatusAsync(
        SqliteBlokeBotDbFactory database,
        Guid activationId,
        ConfigurationActivationStatus expected,
        int minimumAttemptCount = 0
    )
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var db = await database.CreateDbContextAsync();
            var row = await db.ConfigurationActivations.SingleAsync(x => x.Id == activationId);
            if (row.Status == expected && row.AttemptCount >= minimumAttemptCount)
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"Activation {activationId} did not become {expected}.");
    }

    private static IReadOnlyList<ConfigurationActivationIssue> PersistedIssues(
        ConfigurationActivation activation
    ) =>
        JsonSerializer.Deserialize<ConfigurationActivationIssue[]>(
            activation.IssuesJson.ShouldNotBeNull()
        ) ?? [];

    private sealed class RecordingObserver(TimeSpan? delay = null) : IHostFeatureActivationObserver
    {
        public List<HostFeatureActivationChange> Changes { get; } = [];

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Add(change);
            if (delay is { } value)
            {
                await Task.Delay(value, cancellationToken);
            }
            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }

    private sealed class MutableObserver : IHostFeatureActivationObserver
    {
        internal const string ManualCode = "provider-reconnection-required";
        internal const string ManualKey = "provider:primary";

        public bool Throw { get; set; }
        public bool ManualFollowUp { get; set; }
        public List<HostFeatureActivationChange> Changes { get; } = [];

        public ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Add(change);
            return Throw
                ? throw new InvalidOperationException("planned private detail")
                : ValueTask.FromResult<HostFeatureAutomaticWorkResult>(
                    ManualFollowUp
                        ? new HostFeatureAutomaticWorkResult.ManualFollowUp(
                            new(
                                ManualCode,
                                "Reconnect the required provider, then retry automatic activation.",
                                ManualKey,
                                "Provider reconnection required",
                                "Reconnect the provider before retrying automatic activation.",
                                "/host"
                            )
                        )
                        : new HostFeatureAutomaticWorkResult.Complete()
                );
        }
    }

    private sealed class BlockingObserver : IHostFeatureActivationObserver
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            _ = Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }

    private sealed class FixedManualFollowUpObserver(string code, string stableKey)
        : IHostFeatureActivationObserver
    {
        public ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<HostFeatureAutomaticWorkResult>(
                new HostFeatureAutomaticWorkResult.ManualFollowUp(
                    new(
                        code,
                        $"Complete {code}.",
                        stableKey,
                        "Manual follow-up required",
                        $"Complete {code} before retrying activation.",
                        "/host"
                    )
                )
            );
    }

    private sealed class GateObserver : IHostFeatureActivationObserver
    {
        internal TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<HostFeatureActivationChange> Changes { get; } = [];

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Add(change);
            if (Changes.Count == 1)
            {
                _ = FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }
}

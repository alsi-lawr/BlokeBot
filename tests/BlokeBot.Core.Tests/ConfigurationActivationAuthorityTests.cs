using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationActivationTests
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
}

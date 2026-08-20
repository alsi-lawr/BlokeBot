using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationActivationTests
{
    [Test]
    public async Task ExplicitEnablement_DoesNotInvokeGenericCatchUpObservers()
    {
        var generic = new RecordingGenericObserver();
        var activation = new RecordingActivationObserver();
        var eventSub = new RecordingEventSubTrigger();
        var dispatcher = new ConfigurationActivationDispatcher(
            [generic],
            [activation],
            eventSub,
            new(TestEventBus.Create<AppEventKind>())
        );

        await dispatcher.ActivateAsync(
            7,
            HostFeatureFlags.Bingo | HostFeatureFlags.Polls,
            HostFeatureFlags.None,
            CancellationToken.None
        );

        generic.Changes.ShouldBeEmpty();
        activation.Features.ShouldBe([HostFeatureFlags.Polls, HostFeatureFlags.Bingo]);
        eventSub.ReconcileCount.ShouldBe(1);
    }

    [Test]
    public async Task Disablement_StillSuppressesActiveWorkAndRepairsSubscriptions()
    {
        var generic = new RecordingGenericObserver();
        var eventSub = new RecordingEventSubTrigger();
        var dispatcher = new ConfigurationActivationDispatcher(
            [generic],
            [],
            eventSub,
            new(TestEventBus.Create<AppEventKind>())
        );

        await dispatcher.ActivateAsync(
            7,
            HostFeatureFlags.None,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );

        generic.Changes.ShouldBe([(HostFeatureFlags.Automations, false)]);
        eventSub.ReconcileCount.ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentWorkers_ClaimOnePendingActivationExactlyOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = seed.ConfigurationActivations.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = hostId,
                    EnabledChanges = HostFeatureFlags.Polls,
                    Status = ConfigurationActivationStatus.Pending,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var observer = new RecordingActivationObserver(TimeSpan.FromMilliseconds(100));
        var queue = new ConfigurationActivationQueue();
        var dispatcher = new ConfigurationActivationDispatcher(
            [],
            [observer],
            null,
            new(TestEventBus.Create<AppEventKind>())
        );
        var first = Worker(database, queue, dispatcher);
        var second = Worker(database, queue, dispatcher);

        await first.StartAsync(CancellationToken.None);
        await second.StartAsync(CancellationToken.None);
        queue.Wake();
        await WaitForCompleteAsync(database);
        await first.StopAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);

        observer.Features.ShouldBe([HostFeatureFlags.Polls]);
        await using var verify = await database.CreateDbContextAsync();
        var row = await verify.ConfigurationActivations.SingleAsync();
        row.Status.ShouldBe(ConfigurationActivationStatus.Complete);
        row.AttemptCount.ShouldBe(1);
    }

    [Test]
    public async Task FailedActivation_RetryIsIdempotentAndHostScoped()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        Guid activationId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            activationId = Guid.NewGuid();
            _ = seed.ConfigurationActivations.Add(
                new()
                {
                    Id = activationId,
                    HostId = host.Id,
                    Status = ConfigurationActivationStatus.Failed,
                    FailureCode = "planned",
                    Revision = 2,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var service = new ConfigurationActivationService(
            database,
            new ConfigurationActivationQueue(),
            TimeProvider.System
        );

        (await service.RetryAsync(999, activationId, CancellationToken.None)).ShouldBeFalse();
        int hostId;
        await using (var lookup = await database.CreateDbContextAsync())
        {
            hostId = await lookup.Hosts.Select(x => x.Id).SingleAsync();
        }
        (await service.RetryAsync(hostId, activationId, CancellationToken.None)).ShouldBeTrue();
        (await service.RetryAsync(hostId, activationId, CancellationToken.None)).ShouldBeFalse();
        var view = await service.LoadAsync(hostId, activationId, CancellationToken.None);
        view.ShouldNotBeNull().Status.ShouldBe(ConfigurationActivationStatus.Pending);
        (await service.LoadAsync(999, activationId, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task ObserverFailure_IsRecordedSeparatelyFromCommittedImportState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                EnabledFeatures = HostFeatureFlags.Polls,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            _ = seed.ConfigurationActivations.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = host.Id,
                    EnabledChanges = HostFeatureFlags.Polls,
                    Status = ConfigurationActivationStatus.Pending,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var queue = new ConfigurationActivationQueue();
        var worker = Worker(
            database,
            queue,
            new(
                [],
                [new ThrowingActivationObserver()],
                null,
                new(TestEventBus.Create<AppEventKind>())
            )
        );

        await worker.StartAsync(CancellationToken.None);
        queue.Wake();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var poll = await database.CreateDbContextAsync();
            if (
                await poll.ConfigurationActivations.AnyAsync(x =>
                    x.Status == ConfigurationActivationStatus.Failed
                )
            )
            {
                break;
            }

            await Task.Delay(20);
        }
        await worker.StopAsync(CancellationToken.None);

        await using var verify = await database.CreateDbContextAsync();
        var row = await verify.ConfigurationActivations.SingleAsync();
        row.Status.ShouldBe(ConfigurationActivationStatus.Failed);
        row.FailureCode.ShouldBe(nameof(InvalidOperationException));
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.Polls);
    }

    private static ConfigurationActivationWorker Worker(
        SqliteBlokeBotDbFactory database,
        ConfigurationActivationQueue queue,
        ConfigurationActivationDispatcher dispatcher
    ) =>
        new(
            database,
            queue,
            dispatcher,
            TimeProvider.System,
            NullLogger<ConfigurationActivationWorker>.Instance
        );

    private static async Task WaitForCompleteAsync(SqliteBlokeBotDbFactory database)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var db = await database.CreateDbContextAsync();
            if (
                await db.ConfigurationActivations.AnyAsync(x =>
                    x.Status == ConfigurationActivationStatus.Complete
                )
            )
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("The activation did not complete.");
    }

    private sealed class RecordingGenericObserver : IHostFeatureChangeObserver
    {
        public List<(HostFeatureFlags Feature, bool Enabled)> Changes { get; } = [];

        public ValueTask FeatureChangedAsync(
            int hostId,
            HostFeatureFlags feature,
            bool enabled,
            CancellationToken cancellationToken
        )
        {
            Changes.Add((feature, enabled));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingActivationObserver(TimeSpan? delay = null)
        : IConfigurationActivationObserver
    {
        public List<HostFeatureFlags> Features { get; } = [];

        public async ValueTask FeatureEnabledAsync(
            int hostId,
            HostFeatureFlags feature,
            CancellationToken cancellationToken
        )
        {
            Features.Add(feature);
            if (delay is { } value)
            {
                await Task.Delay(value, cancellationToken);
            }
        }
    }

    private sealed class RecordingEventSubTrigger : IEventSubChannelReconciliationTrigger
    {
        public int ReconcileCount { get; private set; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            ReconcileCount++;
            return Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class ThrowingActivationObserver : IConfigurationActivationObserver
    {
        public ValueTask FeatureEnabledAsync(
            int hostId,
            HostFeatureFlags feature,
            CancellationToken cancellationToken
        ) => ValueTask.FromException(new InvalidOperationException("planned"));
    }
}

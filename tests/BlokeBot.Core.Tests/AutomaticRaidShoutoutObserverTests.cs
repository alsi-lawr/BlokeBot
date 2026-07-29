using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
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
    public async Task ConcurrentDuplicate_HasOneClaimWinnerAndOneDelivery()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedAsync(factory, enabled: true, threshold: 1);
        var delivery = new BlockingDelivery();
        var raid = Raid("concurrent", _now, 1);
        var first = Observer(factory, delivery)
            .IncomingRaidReceivedAsync(raid, CancellationToken.None);
        await delivery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Observer(factory, delivery)
            .IncomingRaidReceivedAsync(raid, CancellationToken.None);
        await second;
        delivery.Release.SetResult();
        await first;

        delivery.CallCount.ShouldBe(1);
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
    public async Task RetentionKeepsNewest100TerminalAndAllProcessingOrAmbiguousOutcomes()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(factory, enabled: true, threshold: 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var index = 0; index < 101; index++)
            {
                db.AutomaticRaidShoutoutOutcomes.Add(
                    Outcome(
                        hostId,
                        $"terminal-{index}",
                        AutomaticRaidShoutoutOutcomeStatus.Delivered,
                        AutomaticRaidShoutoutResultCode.Delivered,
                        _now.AddMinutes(-index - 1).UtcDateTime
                    )
                );
            }
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

        await Observer(
                factory,
                new RecordingDelivery(new AutomaticRaidShoutoutDeliveryResult.Delivered())
            )
            .IncomingRaidReceivedAsync(Raid("newest", _now, 1), CancellationToken.None);

        await using var verification = await factory.CreateDbContextAsync();
        (
            await verification.AutomaticRaidShoutoutOutcomes.CountAsync(value =>
                value.Status == AutomaticRaidShoutoutOutcomeStatus.Delivered
                || value.Status == AutomaticRaidShoutoutOutcomeStatus.NotDelivered
            )
        ).ShouldBe(100);
        (
            await verification.AutomaticRaidShoutoutOutcomes.CountAsync(value =>
                value.Status == AutomaticRaidShoutoutOutcomeStatus.Processing
                || value.Status == AutomaticRaidShoutoutOutcomeStatus.Ambiguous
            )
        ).ShouldBe(2);
    }

    private static AutomaticRaidShoutoutObserver Observer(
        SqliteBlokeBotDbFactory factory,
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
        SqliteBlokeBotDbFactory factory,
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

    private sealed class BlockingDelivery : IAutomaticRaidShoutoutDelivery
    {
        private int _callCount;
        internal int CallCount => _callCount;
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new AutomaticRaidShoutoutDeliveryResult.Delivered();
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
}

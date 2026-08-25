using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidDeliveryOutcomeMigrationTests
{
    private const string _previousMigration = "20260825142439_v0.13.0_OverlayAccessRegeneration";
    private static readonly DateTime _now = new(2026, 8, 25, 15, 30, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExistingOutcomesUpgradeAndQueuedStateDowngradesWithoutFalseDelivery()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using (var before = await database.CreateDbContextAsync())
        {
            await before.Database.MigrateAsync(_previousMigration);
            var host = new BotHost
            {
                Id = 1,
                TwitchUserId = "migration-host-id",
                Login = "migration-host",
                DisplayName = "Migration host",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = _now,
            };
            _ = before.Hosts.Add(host);
            _ = before.AutomaticRaidShoutoutOutcomes.Add(
                Outcome(
                    "already-delivered",
                    AutomaticRaidShoutoutOutcomeStatus.Delivered,
                    AutomaticRaidShoutoutResultCode.Delivered,
                    _now
                )
            );
            _ = await before.SaveChangesAsync();
        }

        await using (var upgrade = await database.CreateDbContextAsync())
        {
            await upgrade.Database.MigrateAsync();
            var delivered = await upgrade.AutomaticRaidShoutoutOutcomes.SingleAsync();
            delivered.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Delivered);
            delivered.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Delivered);
            delivered.CompletedAtUtc.ShouldBe(_now);

            _ = upgrade.AutomaticRaidShoutoutOutcomes.Add(
                Outcome(
                    "queued-after-upgrade",
                    AutomaticRaidShoutoutOutcomeStatus.Queued,
                    AutomaticRaidShoutoutResultCode.Queued,
                    null
                )
            );
            _ = upgrade.RaidCollaborationHistory.Add(
                new RaidCollaborationHistoryEntry
                {
                    HostId = 1,
                    ProviderMessageId = "queued-after-upgrade",
                    Direction = RaidDirection.Incoming,
                    OtherTwitchUserId = "raider-id",
                    OtherLogin = "raider",
                    OtherDisplayName = "Raider",
                    ViewerCount = 12,
                    OccurredAtUtc = _now,
                    WelcomeOutcome = RaidWelcomeOutcome.NotConfigured,
                    ShoutoutOutcome = RaidShoutoutOutcome.Queued,
                    RecordedAtUtc = _now,
                }
            );
            _ = await upgrade.SaveChangesAsync();
        }

        await using (var downgrade = await database.CreateDbContextAsync())
        {
            await downgrade.Database.MigrateAsync(_previousMigration);
        }

        await using var verify = await database.CreateDbContextAsync();
        var downgraded = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync(outcome =>
            outcome.ProviderMessageId == "queued-after-upgrade"
        );
        downgraded.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.Processing);
        downgraded.ResultCode.ShouldBeNull();
        downgraded.CompletedAtUtc.ShouldBeNull();
        (
            await verify.RaidCollaborationHistory.SingleAsync(history =>
                history.ProviderMessageId == "queued-after-upgrade"
            )
        ).ShoutoutOutcome.ShouldBe(RaidShoutoutOutcome.Rejected);
    }

    private static AutomaticRaidShoutoutOutcome Outcome(
        string providerMessageId,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode? resultCode,
        DateTime? completedAtUtc
    ) =>
        new()
        {
            HostId = 1,
            ProviderMessageId = providerMessageId,
            SourceTwitchUserId = "raider-id",
            SourceLogin = "raider",
            SourceDisplayName = "Raider",
            ViewerCount = 12,
            Status = status,
            ResultCode = resultCode,
            MessageTimestampUtc = _now,
            ClaimedAtUtc = _now,
            CompletedAtUtc = completedAtUtc,
        };
}

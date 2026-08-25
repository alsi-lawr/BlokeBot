using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BountyPauseAdjustedAuditMigrationTests
{
    private const string _previousMigration =
        "20260825151634_v0.13.0_AutomaticRaidDeliveryOutcomes";
    private static readonly DateTime _now = new(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExistingAuditsUpgradeAndPauseAdjustmentDowngradesWithoutLosingHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        var existingOperation = Guid.NewGuid();
        await using (var before = await database.CreateDbContextAsync())
        {
            await before.Database.MigrateAsync(_previousMigration);
            _ = before.Hosts.Add(Host());
            _ = before.Bounties.Add(Bounty());
            _ = await before.SaveChangesAsync();
            var bounty = await before.Bounties.SingleAsync();
            _ = before.BountyModerationAudits.Add(
                Audit(
                    bounty,
                    existingOperation,
                    BountyAuditAction.Extended,
                    "Existing moderator history"
                )
            );
            _ = await before.SaveChangesAsync();
        }

        var pauseOperation = Guid.NewGuid();
        await using (var upgrade = await database.CreateDbContextAsync())
        {
            await upgrade.Database.MigrateAsync();
            var existing = await upgrade.BountyModerationAudits.SingleAsync();
            existing.OperationId.ShouldBe(existingOperation);
            existing.Action.ShouldBe(BountyAuditAction.Extended);
            var bounty = await upgrade.Bounties.SingleAsync();
            _ = upgrade.BountyModerationAudits.Add(
                Audit(
                    bounty,
                    pauseOperation,
                    BountyAuditAction.PauseAdjusted,
                    "Deadline moved from OLD to NEW for PAUSE-INTERVAL"
                )
            );
            _ = await upgrade.SaveChangesAsync();
        }

        await using (var downgrade = await database.CreateDbContextAsync())
        {
            await downgrade.Database.MigrateAsync(_previousMigration);
        }

        await using var verify = await database.CreateDbContextAsync();
        var audits = await verify
            .BountyModerationAudits.AsNoTracking()
            .OrderBy(value => value.Id)
            .ToArrayAsync();
        audits.Length.ShouldBe(2);
        audits[0].OperationId.ShouldBe(existingOperation);
        audits[0].Action.ShouldBe(BountyAuditAction.Extended);
        audits[1].OperationId.ShouldBe(pauseOperation);
        audits[1].Action.ShouldBe(BountyAuditAction.Extended);
        audits[1].Reason.ShouldBe("Deadline moved from OLD to NEW for PAUSE-INTERVAL");
        audits[1].BountyRevision.ShouldBe(3);
        audits[1].ActorTwitchUserId.ShouldBe("BlokeBot.BountyPauseRecovery");
    }

    private static BotHost Host() =>
        new()
        {
            Id = 1,
            TwitchUserId = "migration-host-id",
            Login = "migration-host",
            DisplayName = "Migration host",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = _now,
        };

    private static Bounty Bounty() =>
        new()
        {
            Id = 1,
            PublicId = Guid.NewGuid(),
            HostId = 1,
            CreationOperationId = Guid.NewGuid(),
            CreationFingerprint = new string('a', 64),
            Title = "Migration bounty",
            Description = "Preserved bounty history.",
            Status = BountyStatus.Funding,
            Visibility = BountyVisibility.Public,
            FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
            RewardDistribution = BountyRewardDistribution.Proportional,
            FundingTarget = "100",
            PledgedAmount = "0",
            CompletionReward = "0",
            ExpiresAtUtc = _now.AddHours(1),
            Revision = 3,
            CreatedAtUtc = _now,
            UpdatedAtUtc = _now,
        };

    private static BountyModerationAudit Audit(
        Bounty bounty,
        Guid operationId,
        BountyAuditAction action,
        string reason
    ) =>
        new()
        {
            HostId = bounty.HostId,
            BountyId = bounty.Id,
            OperationId = operationId,
            CommandFingerprint = new string('b', 64),
            Action = action,
            FromStatus = bounty.Status,
            ToStatus = bounty.Status,
            ActorTwitchUserId =
                action == BountyAuditAction.PauseAdjusted
                    ? "BlokeBot.BountyPauseRecovery"
                    : "moderator-id",
            ActorLogin = action == BountyAuditAction.PauseAdjusted ? "blokebot" : "moderator",
            Reason = reason,
            BountyRevision = bounty.Revision,
            OccurredAtUtc = _now,
        };
}

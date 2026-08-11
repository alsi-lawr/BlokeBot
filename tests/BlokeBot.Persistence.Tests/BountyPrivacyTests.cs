using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BountyPrivacyTests
{
    [Test]
    public async Task ExportAndErasure_AnonymizeBountyIdentityAndConsumeRefundLiability()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            var bounty = Bounty(hostId);
            _ = seed.Bounties.Add(bounty);
            _ = await seed.SaveChangesAsync();
            var seededPledge = Pledge(hostId, bounty.Id, "viewer-id", "viewer");
            var seededReward = Reward(hostId, bounty.Id, "viewer-id", "viewer");
            _ = seed.BountyPledges.Add(seededPledge);
            _ = seed.BountyContributorRewards.Add(seededReward);
            _ = seed.BountyModerationAudits.Add(Audit(hostId, bounty.Id, "viewer-id", "viewer"));
            _ = seed.BountyEvents.Add(
                new BountyDomainEvent
                {
                    HostId = hostId,
                    BountyId = bounty.Id,
                    BountyPublicId = bounty.PublicId,
                    SchemaVersion = 1,
                    Kind = BountyEventKind.Pledged,
                    PublicPayload = "{\"ContributorLogin\":\"viewer\"}",
                    OccurredAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
            seed.PointLedgerEntries.AddRange(
                Ledger(hostId, PointLedgerKind.BountyPledgeReservation, seededPledge.Id, null),
                Ledger(hostId, PointLedgerKind.BountyCompletionReward, null, seededReward.Id)
            );
            _ = await seed.SaveChangesAsync();
        }

        await using (var exportDb = await factory.CreateDbContextAsync())
        {
            var export = await ViewerPrivacyService.ExportAsync(
                exportDb,
                PrivacySubject.Create("viewer-id", null),
                hostId,
                default
            );
            export.Sections.Keys.ShouldContain("bounties.pledges");
            export.Sections.Keys.ShouldContain("bounties.rewards");
            export.Sections.Keys.ShouldContain("bounties.moderation-audits");
            export.Sections.Keys.ShouldContain("points.ledger");
        }

        ViewerErasureReport report;
        await using (var eraseDb = await factory.CreateDbContextAsync())
        {
            report = await ViewerPrivacyService.EraseAsync(
                eraseDb,
                PrivacySubject.Create("viewer-id", null),
                hostId,
                default
            );
        }

        report.ChangedRows["bounties.pledges"].ShouldBe(1);
        report.ChangedRows["bounties.rewards"].ShouldBe(1);
        report.ChangedRows["bounties.moderation-audits"].ShouldBe(1);
        report.ChangedRows["bounties.ledger"].ShouldBe(2);
        report.ChangedRows["bounties.events"].ShouldBe(1);
        await using var verify = await factory.CreateDbContextAsync();
        var pledge = await verify.BountyPledges.SingleAsync();
        pledge.ContributorTwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
        pledge.ContributorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
        pledge.State.ShouldBe(BountyPledgeState.Consumed);
        var reward = await verify.BountyContributorRewards.SingleAsync();
        reward.TwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
        reward.Login.ShouldBe(ViewerPrivacyService.ErasedToken);
        var audit = await verify.BountyModerationAudits.SingleAsync();
        audit.ActorTwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
        audit.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
        audit.Reason.ShouldBeEmpty();
        (await verify.Bounties.SingleAsync()).ContributorCount.ShouldBe(1);
        var ledger = await verify.PointLedgerEntries.ToListAsync();
        ledger.ShouldAllBe(value => value.Login == ViewerPrivacyService.ErasedToken);
        ledger.ShouldAllBe(value => value.ActorLogin == null);
        (await verify.BountyEvents.CountAsync()).ShouldBe(0);
    }

    private static Bounty Bounty(int hostId)
    {
        var now = DateTime.UtcNow;
        return new Bounty
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            CreationOperationId = Guid.NewGuid(),
            Title = "Privacy bounty",
            Status = BountyStatus.Accepted,
            Visibility = BountyVisibility.Public,
            FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
            RewardDistribution = BountyRewardDistribution.Proportional,
            FundingTarget = "10",
            PledgedAmount = "10",
            ContributorCount = 1,
            CompletionReward = "5",
            ExpiresAtUtc = now.AddDays(1),
            Revision = 2,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static PointLedgerEntry Ledger(
        int hostId,
        PointLedgerKind kind,
        long? pledgeId,
        long? rewardId
    ) =>
        new()
        {
            HostId = hostId,
            CreatedAtUtc = DateTime.UtcNow,
            Kind = kind,
            Login = "viewer",
            Delta = kind == PointLedgerKind.BountyPledgeReservation ? "-10" : "5",
            BalanceAfter = kind == PointLedgerKind.BountyPledgeReservation ? "0" : "5",
            ActorLogin = "viewer",
            BountyPledgeId = pledgeId,
            BountyRewardId = rewardId,
            OperationKey = Guid.NewGuid().ToString("N"),
            Note = "private note",
        };

    private static BountyPledge Pledge(int hostId, long bountyId, string twitchUserId, string login)
    {
        var now = DateTime.UtcNow;
        return new BountyPledge
        {
            HostId = hostId,
            BountyId = bountyId,
            OperationId = Guid.NewGuid(),
            ContributorTwitchUserId = twitchUserId,
            ContributorLogin = login,
            Amount = "10",
            State = BountyPledgeState.Reserved,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static BountyContributorReward Reward(
        int hostId,
        long bountyId,
        string twitchUserId,
        string login
    ) =>
        new()
        {
            HostId = hostId,
            BountyId = bountyId,
            TwitchUserId = twitchUserId,
            Login = login,
            Amount = "5",
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static BountyModerationAudit Audit(
        int hostId,
        long bountyId,
        string twitchUserId,
        string login
    ) =>
        new()
        {
            HostId = hostId,
            BountyId = bountyId,
            OperationId = Guid.NewGuid(),
            Action = BountyAuditAction.Accepted,
            FromStatus = BountyStatus.Funding,
            ToStatus = BountyStatus.Accepted,
            ActorTwitchUserId = twitchUserId,
            ActorLogin = login,
            Reason = "Private moderation reason",
            BountyRevision = 2,
            OccurredAtUtc = DateTime.UtcNow,
        };
}

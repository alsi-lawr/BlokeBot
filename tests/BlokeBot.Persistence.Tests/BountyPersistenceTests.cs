using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BountyPersistenceTests
{
    [Test]
    public async Task OperationIdentifiers_AreUniqueWithinHost()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        var creationOperationId = Guid.NewGuid();
        var pledgeOperationId = Guid.NewGuid();
        var auditOperationId = Guid.NewGuid();
        var eventOperationKey = Guid.NewGuid().ToString("N");

        await using var seed = await factory.CreateDbContextAsync();
        var firstHostId = await SeedHostAsync(seed, "first");
        var secondHostId = await SeedHostAsync(seed, "second");
        var firstBounty = CreateBounty(firstHostId, creationOperationId);
        var secondBounty = CreateBounty(secondHostId, creationOperationId);
        seed.Bounties.AddRange(firstBounty, secondBounty);
        _ = await seed.SaveChangesAsync();

        seed.BountyPledges.AddRange(
            CreatePledge(firstHostId, firstBounty.Id, pledgeOperationId),
            CreatePledge(secondHostId, secondBounty.Id, pledgeOperationId)
        );
        seed.BountyModerationAudits.AddRange(
            CreateAudit(firstHostId, firstBounty.Id, auditOperationId),
            CreateAudit(secondHostId, secondBounty.Id, auditOperationId)
        );
        seed.BountyEvents.AddRange(
            CreateEvent(firstHostId, firstBounty, eventOperationKey),
            CreateEvent(secondHostId, secondBounty, eventOperationKey)
        );
        _ = await seed.SaveChangesAsync();

        await AssertSaveRejectedAsync(
            factory,
            db => db.Bounties.Add(CreateBounty(firstHostId, creationOperationId))
        );
        await AssertSaveRejectedAsync(
            factory,
            db => db.BountyPledges.Add(CreatePledge(firstHostId, firstBounty.Id, pledgeOperationId))
        );
        await AssertSaveRejectedAsync(
            factory,
            db =>
                db.BountyModerationAudits.Add(
                    CreateAudit(firstHostId, firstBounty.Id, auditOperationId)
                )
        );
        await AssertSaveRejectedAsync(
            factory,
            db => db.BountyEvents.Add(CreateEvent(firstHostId, firstBounty, eventOperationKey))
        );
    }

    [Test]
    public async Task CrossHostBountyReferences_AreRejected()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int firstHostId;
        int secondHostId;
        Bounty bounty;
        BountyPledge pledge;
        BountyContributorReward reward;

        await using (var seed = await factory.CreateDbContextAsync())
        {
            firstHostId = await SeedHostAsync(seed, "first");
            secondHostId = await SeedHostAsync(seed, "second");
            bounty = CreateBounty(firstHostId, Guid.NewGuid());
            _ = seed.Bounties.Add(bounty);
            _ = await seed.SaveChangesAsync();

            pledge = CreatePledge(firstHostId, bounty.Id, Guid.NewGuid());
            reward = CreateReward(firstHostId, bounty.Id, "rewarded-user");
            seed.AddRange(pledge, reward);
            _ = await seed.SaveChangesAsync();
        }

        await AssertSaveRejectedAsync(
            factory,
            db => db.BountyPledges.Add(CreatePledge(secondHostId, bounty.Id, Guid.NewGuid()))
        );
        await AssertSaveRejectedAsync(
            factory,
            db =>
                db.BountyContributorRewards.Add(
                    CreateReward(secondHostId, bounty.Id, "other-rewarded-user")
                )
        );
        await AssertSaveRejectedAsync(
            factory,
            db =>
                db.BountyModerationAudits.Add(CreateAudit(secondHostId, bounty.Id, Guid.NewGuid()))
        );
        await AssertSaveRejectedAsync(
            factory,
            db => db.BountyEvents.Add(CreateEvent(secondHostId, bounty, null))
        );
        await AssertSaveRejectedAsync(
            factory,
            db =>
                db.PointLedgerEntries.Add(
                    CreateLedgerEntry(secondHostId, pledge.Id, null, "pledge-ledger")
                )
        );
        await AssertSaveRejectedAsync(
            factory,
            db =>
                db.PointLedgerEntries.Add(
                    CreateLedgerEntry(secondHostId, null, reward.Id, "reward-ledger")
                )
        );
    }

    [Test]
    public async Task HostDeletion_CascadesTheBountyAccountingGraph()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var hostId = await SeedHostAsync(seed, "host");
            var bounty = CreateBounty(hostId, Guid.NewGuid());
            _ = seed.Bounties.Add(bounty);
            _ = await seed.SaveChangesAsync();
            var pledge = CreatePledge(hostId, bounty.Id, Guid.NewGuid());
            var reward = CreateReward(hostId, bounty.Id, "rewarded-user");
            seed.AddRange(pledge, reward);
            _ = await seed.SaveChangesAsync();
            seed.PointLedgerEntries.AddRange(
                CreateLedgerEntry(hostId, pledge.Id, null, "pledge-ledger"),
                CreateLedgerEntry(hostId, null, reward.Id, "reward-ledger")
            );
            _ = await seed.SaveChangesAsync();
            _ = seed.Hosts.Remove(await seed.Hosts.SingleAsync());
            _ = await seed.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.Bounties.CountAsync()).ShouldBe(0);
        (await verify.BountyPledges.CountAsync()).ShouldBe(0);
        (await verify.BountyContributorRewards.CountAsync()).ShouldBe(0);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    private static async Task AssertSaveRejectedAsync(
        SqliteBlokeBotDbFactory factory,
        Action<BlokeBotDbContext> addInvalidEntity
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        addInvalidEntity(db);
        _ = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Bounty CreateBounty(int hostId, Guid creationOperationId)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            CreationOperationId = creationOperationId,
            Title = "Bounty",
            Description = "Persistence invariant bounty",
            Status = BountyStatus.Funding,
            Visibility = BountyVisibility.Public,
            FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
            RewardDistribution = BountyRewardDistribution.Proportional,
            FundingTarget = "100",
            PledgedAmount = "10",
            CompletionReward = "25",
            ExpiresAtUtc = now.AddDays(7),
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static BountyPledge CreatePledge(int hostId, long bountyId, Guid operationId)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            HostId = hostId,
            BountyId = bountyId,
            OperationId = operationId,
            ContributorTwitchUserId = "contributor-id",
            ContributorLogin = "contributor",
            Amount = "10",
            State = BountyPledgeState.Reserved,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static BountyContributorReward CreateReward(
        int hostId,
        long bountyId,
        string twitchUserId
    ) =>
        new()
        {
            HostId = hostId,
            BountyId = bountyId,
            TwitchUserId = twitchUserId,
            Login = twitchUserId,
            Amount = "25",
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static BountyModerationAudit CreateAudit(int hostId, long bountyId, Guid operationId) =>
        new()
        {
            HostId = hostId,
            BountyId = bountyId,
            OperationId = operationId,
            Action = BountyAuditAction.FundingOpened,
            FromStatus = BountyStatus.Proposed,
            ToStatus = BountyStatus.Funding,
            ActorTwitchUserId = "moderator-id",
            ActorLogin = "moderator",
            Reason = "Funding opened",
            BountyRevision = 1,
            OccurredAtUtc = DateTime.UtcNow,
        };

    private static BountyDomainEvent CreateEvent(int hostId, Bounty bounty, string? operationKey) =>
        new()
        {
            HostId = hostId,
            BountyId = bounty.Id,
            BountyPublicId = bounty.PublicId,
            OperationKey = operationKey,
            SchemaVersion = 1,
            Kind = BountyEventKind.FundingOpened,
            PublicPayload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };

    private static PointLedgerEntry CreateLedgerEntry(
        int hostId,
        long? pledgeId,
        long? rewardId,
        string operationKey
    ) =>
        new()
        {
            HostId = hostId,
            CreatedAtUtc = DateTime.UtcNow,
            Kind = pledgeId.HasValue
                ? PointLedgerKind.BountyPledgeReservation
                : PointLedgerKind.BountyCompletionReward,
            Login = "viewer",
            Delta = pledgeId.HasValue ? "-10" : "25",
            BalanceAfter = "100",
            BountyPledgeId = pledgeId,
            BountyRewardId = rewardId,
            OperationKey = operationKey,
        };

    private static async Task<int> SeedHostAsync(BlokeBotDbContext db, string login)
    {
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}

using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _bountyStatuses =
        PersistedEnumTokens<BountyStatus>.Values.ToArray();
    private static readonly string[] _bountyVisibilities =
        PersistedEnumTokens<BountyVisibility>.Values.ToArray();
    private static readonly string[] _bountyFailurePledgePolicies =
        PersistedEnumTokens<BountyFailurePledgePolicy>.Values.ToArray();
    private static readonly string[] _bountyRewardDistributions =
        PersistedEnumTokens<BountyRewardDistribution>.Values.ToArray();
    private static readonly string[] _bountyPledgeStates =
        PersistedEnumTokens<BountyPledgeState>.Values.ToArray();
    private static readonly string[] _bountyAuditActions =
        PersistedEnumTokens<BountyAuditAction>.Values.ToArray();
    private static readonly string[] _bountyEventKinds =
        PersistedEnumTokens<BountyEventKind>.Values.ToArray();

    private static void ConfigureBounties(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<Bounty>(static b =>
        {
            _ = b.ToTable(
                "bounties",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_bounties_Status",
                        KindIn("Status", _bountyStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_bounties_Visibility",
                        KindIn("Visibility", _bountyVisibilities)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_bounties_FailurePledgePolicy",
                        KindIn("FailurePledgePolicy", _bountyFailurePledgePolicies)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_bounties_RewardDistribution",
                        KindIn("RewardDistribution", _bountyRewardDistributions)
                    );
                    _ = t.HasCheckConstraint("CK_bounties_Revision", "Revision > 0");
                    _ = t.HasCheckConstraint(
                        "CK_bounties_ContributorCount",
                        "ContributorCount >= 0"
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.PublicId).HasConversion<string>();
            _ = b.Property(static x => x.CreationFingerprint).HasMaxLength(64);
            _ = b.Property(static x => x.Title).HasMaxLength(160);
            _ = b.Property(static x => x.Description).HasMaxLength(2000);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyStatus>.Format(value),
                    static value => PersistedEnumTokens<BountyStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.Visibility)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyVisibility>.Format(value),
                    static value => PersistedEnumTokens<BountyVisibility>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.FailurePledgePolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyFailurePledgePolicy>.Format(value),
                    static value => PersistedEnumTokens<BountyFailurePledgePolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.RewardDistribution)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyRewardDistribution>.Format(value),
                    static value => PersistedEnumTokens<BountyRewardDistribution>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.FundingTarget).HasMaxLength(128);
            _ = b.Property(static x => x.PledgedAmount).HasMaxLength(128);
            _ = b.Property(static x => x.CompletionReward).HasMaxLength(128);
            _ = b.Property(static x => x.Revision).IsConcurrencyToken();
            _ = b.HasIndex(static x => x.PublicId).IsUnique();
            _ = b.HasIndex(static x => new { x.HostId, x.CreationOperationId }).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.Status,
                x.ExpiresAtUtc,
                x.Id,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BountyPledge>(static b =>
        {
            _ = b.ToTable(
                "bounty_pledges",
                static t =>
                    t.HasCheckConstraint(
                        "CK_bounty_pledges_State",
                        KindIn("State", _bountyPledgeStates)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.ContributorTwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.ContributorLogin).HasMaxLength(128);
            _ = b.Property(static x => x.CommandFingerprint).HasMaxLength(64);
            _ = b.Property(static x => x.Amount).HasMaxLength(128);
            _ = b.Property(static x => x.State)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyPledgeState>.Format(value),
                    static value => PersistedEnumTokens<BountyPledgeState>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.OperationId }).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.ContributorLogin,
                x.State,
            });
            _ = b.HasOne(static x => x.Bounty)
                .WithMany(static x => x.Pledges)
                .HasForeignKey(static x => new { x.HostId, x.BountyId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BountyContributorReward>(static b =>
        {
            _ = b.ToTable("bounty_contributor_rewards");
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.Amount).HasMaxLength(128);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.BountyId,
                    x.Login,
                })
                .IsUnique()
                .HasFilter("\"Login\" <> '[erased]'");
            _ = b.HasOne(static x => x.Bounty)
                .WithMany(static x => x.Rewards)
                .HasForeignKey(static x => new { x.HostId, x.BountyId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BountyModerationAudit>(static b =>
        {
            _ = b.ToTable(
                "bounty_moderation_audit",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_bounty_moderation_audit_Action",
                        KindIn("Action", _bountyAuditActions)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_bounty_moderation_audit_FromStatus",
                        KindIn("FromStatus", _bountyStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_bounty_moderation_audit_ToStatus",
                        KindIn("ToStatus", _bountyStatuses)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Action)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyAuditAction>.Format(value),
                    static value => PersistedEnumTokens<BountyAuditAction>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.FromStatus)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyStatus>.Format(value),
                    static value => PersistedEnumTokens<BountyStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.ToStatus)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyStatus>.Format(value),
                    static value => PersistedEnumTokens<BountyStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.ActorTwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(static x => x.CommandFingerprint).HasMaxLength(64);
            _ = b.Property(static x => x.Reason).HasMaxLength(1000);
            _ = b.HasIndex(static x => new { x.HostId, x.OperationId }).IsUnique();
            _ = b.HasOne(static x => x.Bounty)
                .WithMany(static x => x.Audits)
                .HasForeignKey(static x => new { x.HostId, x.BountyId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BountyDomainEvent>(static b =>
        {
            _ = b.ToTable(
                "bounty_events",
                static t =>
                    t.HasCheckConstraint("CK_bounty_events_Kind", KindIn("Kind", _bountyEventKinds))
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.BountyPublicId).HasConversion<string>();
            _ = b.Property(static x => x.OperationKey).HasMaxLength(200);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<BountyEventKind>.Format(value),
                    static value => PersistedEnumTokens<BountyEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.PublicPayload).HasMaxLength(1024);
            _ = b.HasIndex(static x => new { x.HostId, x.OperationKey })
                .IsUnique()
                .HasFilter("\"OperationKey\" IS NOT NULL");
            _ = b.HasOne(static x => x.Bounty)
                .WithMany(static x => x.Events)
                .HasForeignKey(static x => new { x.HostId, x.BountyId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

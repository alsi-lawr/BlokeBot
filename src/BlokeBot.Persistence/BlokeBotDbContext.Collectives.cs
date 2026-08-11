using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureCollectives(ModelBuilder modelBuilder)
    {
        ConfigureCollective(modelBuilder);
        ConfigureCollectiveMembership(modelBuilder);
        ConfigureCollectiveTournament(modelBuilder);
        ConfigureCollectiveRaidRelay(modelBuilder);
        ConfigureCollectiveGoal(modelBuilder);
        ConfigureCollectiveAudit(modelBuilder);
    }

    private static void ConfigureCollective(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<Collective>(static b =>
        {
            _ = b.ToTable("collectives");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Name).HasMaxLength(160);
            _ = b.HasIndex(static x => x.PublicId).IsUnique();
            _ = b.HasIndex(static x => x.CreationOperationId).IsUnique();
        });

    private static void ConfigureCollectiveMembership(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<CollectiveMembership>(static b =>
        {
            _ = b.ToTable("collective_memberships");
            _ = b.HasKey(static x => x.Id);
            ConfigureEnum(b.Property(static x => x.Role));
            ConfigureEnum(b.Property(static x => x.Status));
            _ = b.HasIndex(static x => new { x.CollectiveId, x.HostId }).IsUnique();
            _ = b.HasOne(static x => x.Collective)
                .WithMany(static x => x.Memberships)
                .HasForeignKey(static x => x.CollectiveId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.Host)
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Restrict);
        });

    private static void ConfigureCollectiveTournament(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<CollectiveTournamentReference>(static b =>
        {
            _ = b.ToTable("collective_tournament_references");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Name).HasMaxLength(160);
            ConfigureEnum(b.Property(static x => x.Format));
            ConfigureEnum(b.Property(static x => x.Status));
            _ = b.HasIndex(static x => x.CollectiveId).IsUnique();
            _ = b.HasIndex(static x => new { x.OwnerHostId, x.CompetitionPublicId });
            _ = b.HasOne(static x => x.Collective)
                .WithOne(static x => x.TournamentReference)
                .HasForeignKey<CollectiveTournamentReference>(static x => x.CollectiveId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureCollectiveRaidRelay(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CollectiveRaidRelay>(static b =>
        {
            _ = b.ToTable("collective_raid_relays");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Name).HasMaxLength(160);
            ConfigureEnum(b.Property(static x => x.Status));
            _ = b.HasIndex(static x => x.CollectiveId).IsUnique();
            _ = b.HasOne(static x => x.Collective)
                .WithOne(static x => x.RaidRelay)
                .HasForeignKey<CollectiveRaidRelay>(static x => x.CollectiveId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CollectiveRaidHandoff>(static b =>
        {
            _ = b.ToTable("collective_raid_handoffs");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.OperationId).HasMaxLength(160);
            ConfigureEnum(b.Property(static x => x.Status));
            _ = b.HasIndex(static x => new { x.CollectiveRaidRelayId, x.OperationId }).IsUnique();
            _ = b.HasOne(static x => x.RaidRelay)
                .WithMany(static x => x.Handoffs)
                .HasForeignKey(static x => x.CollectiveRaidRelayId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCollectiveGoal(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CollectiveGoal>(static b =>
        {
            _ = b.ToTable("collective_goals");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Name).HasMaxLength(160);
            _ = b.Property(static x => x.UnitName).HasMaxLength(64);
            ConfigureEnum(b.Property(static x => x.Status));
            _ = b.HasIndex(static x => x.CollectiveId).IsUnique();
            _ = b.HasOne(static x => x.Collective)
                .WithOne(static x => x.Goal)
                .HasForeignKey<CollectiveGoal>(static x => x.CollectiveId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CollectiveGoalHostTotal>(static b =>
        {
            _ = b.ToTable("collective_goal_host_totals");
            _ = b.HasKey(static x => x.Id);
            _ = b.HasIndex(static x => new { x.CollectiveGoalId, x.HostId }).IsUnique();
            _ = b.HasIndex(static x => new { x.HostId, x.SourceBountyPublicId });
            _ = b.HasOne(static x => x.Goal)
                .WithMany(static x => x.HostTotals)
                .HasForeignKey(static x => x.CollectiveGoalId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CollectiveLocalSetting>(static b =>
        {
            _ = b.ToTable("collective_local_settings");
            _ = b.HasKey(static x => x.Id);
            ConfigureEnum(b.Property(static x => x.Notification));
            _ = b.HasIndex(static x => new { x.CollectiveId, x.HostId }).IsUnique();
            _ = b.HasOne(static x => x.Collective)
                .WithMany()
                .HasForeignKey(static x => x.CollectiveId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCollectiveAudit(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<CollectiveAudit>(static b =>
        {
            _ = b.ToTable("collective_audits");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.OperationId).HasMaxLength(160);
            _ = b.Property(static x => x.ActorTwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            ConfigureEnum(b.Property(static x => x.Action));
            _ = b.HasIndex(static x => new { x.CollectiveId, x.OperationId }).IsUnique();
            _ = b.HasOne(static x => x.Collective)
                .WithMany(static x => x.Audits)
                .HasForeignKey(static x => x.CollectiveId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureEnum<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property
    )
        where T : struct, Enum =>
        _ = property
            .HasConversion(
                static value => PersistedEnumTokens<T>.Format(value),
                static value => PersistedEnumTokens<T>.Parse(value)
            )
            .HasMaxLength(48);
}

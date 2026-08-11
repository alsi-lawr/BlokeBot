using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureCommunityProgression(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CommunitySeason>(entity =>
        {
            _ = entity.ToTable("community_seasons");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity
                .HasIndex(value => new { value.HostId, value.CreationOperationId })
                .IsUnique();
            _ = entity.Property(value => value.Name).HasMaxLength(160);
            _ = entity.Property(value => value.Description).HasMaxLength(2000);
            _ = entity.Property(value => value.ModeratorNotes).HasMaxLength(2000);
            _ = entity.Property(value => value.Status).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Visibility).HasPersistedTokenConversion();
            _ = entity.HasIndex(value => new { value.HostId, value.Status });
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityDefinition>(entity =>
        {
            _ = entity.ToTable("community_definitions");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasAlternateKey(value => new { value.HostId, value.Id });
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity.HasIndex(value => new { value.HostId, value.Key }).IsUnique();
            _ = entity.Property(value => value.Key).HasMaxLength(80);
            _ = entity.Property(value => value.Name).HasMaxLength(160);
            _ = entity.Property(value => value.Description).HasMaxLength(1000);
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Scope).HasPersistedTokenConversion();
            _ = entity.Property(value => value.CompletionMode).HasPersistedTokenConversion();
            _ = entity.Property(value => value.EventRule).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Increment).HasPersistedTokenConversion();
            _ = entity.Property(value => value.FilterToken).HasMaxLength(160);
            _ = entity.Property(value => value.PointsReward).HasMaxLength(128);
            _ = entity.Property(value => value.ResetCadence).HasPersistedTokenConversion();
            _ = entity.Property(value => value.ResetLocalTime).HasMaxLength(5);
            _ = entity
                .HasOne(value => value.Season)
                .WithMany(value => value.Definitions)
                .HasForeignKey(value => value.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityRewardDefinition>(entity =>
        {
            _ = entity.ToTable("community_reward_definitions");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity.HasIndex(value => new { value.HostId, value.Key }).IsUnique();
            _ = entity.Property(value => value.Key).HasMaxLength(80);
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Name).HasMaxLength(160);
            _ = entity.Property(value => value.PresentationToken).HasMaxLength(80);
            _ = entity
                .HasOne(value => value.Season)
                .WithMany(value => value.Rewards)
                .HasForeignKey(value => value.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityDefinitionReward>(entity =>
        {
            _ = entity.ToTable("community_definition_rewards");
            _ = entity.HasKey(value => new { value.DefinitionId, value.RewardDefinitionId });
            _ = entity
                .HasOne(value => value.Definition)
                .WithMany(value => value.Rewards)
                .HasForeignKey(value => value.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne(value => value.RewardDefinition)
                .WithMany()
                .HasForeignKey(value => value.RewardDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityProgress>(entity =>
        {
            _ = entity.ToTable("community_progress");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.DefinitionId,
                    value.SubjectKey,
                })
                .IsUnique();
            _ = entity.Property(value => value.SubjectKey).HasMaxLength(160);
            _ = entity.Property(value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerLogin).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerDisplayName).HasMaxLength(160);
            _ = entity.Property(value => value.PeriodKey).HasMaxLength(160);
            _ = entity
                .HasOne<CommunityDefinition>()
                .WithMany()
                .HasForeignKey(value => value.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityCompletion>(entity =>
        {
            _ = entity.ToTable("community_completions");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.DefinitionId,
                    value.SubjectKey,
                    value.Sequence,
                })
                .IsUnique();
            _ = entity.Property(value => value.SubjectKey).HasMaxLength(160);
            _ = entity.Property(value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerLogin).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerDisplayName).HasMaxLength(160);
            _ = entity.Property(value => value.DefinitionKey).HasMaxLength(80);
            _ = entity.Property(value => value.DefinitionName).HasMaxLength(160);
            _ = entity.Property(value => value.PeriodKey).HasMaxLength(160);
            _ = entity.Property(value => value.PointsGranted).HasMaxLength(128);
            _ = entity.Property(value => value.RewardSnapshot).HasMaxLength(4000);
            _ = entity.Property(value => value.SourceOperationKey).HasMaxLength(240);
            _ = entity.HasAlternateKey(value => new { value.HostId, value.Id });
            _ = entity
                .HasOne<CommunityDefinition>()
                .WithMany()
                .HasForeignKey(value => value.DefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityRewardUnlock>(entity =>
        {
            _ = entity.ToTable("community_reward_unlocks");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.RewardDefinitionId,
                    value.ViewerTwitchUserId,
                })
                .IsUnique();
            _ = entity.Property(value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerLogin).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerDisplayName).HasMaxLength(160);
            _ = entity
                .HasOne<CommunityRewardDefinition>()
                .WithMany()
                .HasForeignKey(value => value.RewardDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<CommunityCompletion>()
                .WithMany()
                .HasForeignKey(value => new { value.HostId, value.CompletionId })
                .HasPrincipalKey(value => new { value.HostId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityEquippedReward>(entity =>
        {
            _ = entity.ToTable("community_equipped_rewards");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.ViewerTwitchUserId,
                    value.Kind,
                })
                .IsUnique();
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerLogin).HasMaxLength(128);
            _ = entity
                .HasOne<CommunityRewardDefinition>()
                .WithMany()
                .HasForeignKey(value => value.RewardDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunitySourceEventReceipt>(entity =>
        {
            _ = entity.ToTable("community_source_event_receipts");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.SourceKind,
                    value.SourceEventId,
                })
                .IsUnique();
            _ = entity.Property(value => value.SourceKind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.SourceEventId).HasMaxLength(200);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityExternalGrantReceipt>(entity =>
        {
            _ = entity.ToTable("community_external_grant_receipts");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.Source,
                    value.IdempotencyKey,
                })
                .IsUnique();
            _ = entity.Property(value => value.Source).HasMaxLength(80);
            _ = entity.Property(value => value.IdempotencyKey).HasMaxLength(200);
            _ = entity.Property(value => value.Fingerprint).HasMaxLength(128);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<CommunityCompletion>()
                .WithMany()
                .HasForeignKey(value => new { value.HostId, value.CompletionId })
                .HasPrincipalKey(value => new { value.HostId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<CommunityResetPeriod>(entity =>
        {
            _ = entity.ToTable("community_reset_periods");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.DefinitionId,
                    value.PeriodKey,
                })
                .IsUnique();
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.DefinitionId,
                    value.OperationKey,
                })
                .IsUnique();
            _ = entity.Property(value => value.PeriodKey).HasMaxLength(160);
            _ = entity.Property(value => value.RolloverKind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.OperationKey).HasMaxLength(200);
            _ = entity
                .HasOne<CommunityDefinition>()
                .WithMany()
                .HasForeignKey(value => value.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunitySeasonStanding>(entity =>
        {
            _ = entity.ToTable("community_season_standings");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.SeasonId,
                    value.ViewerTwitchUserId,
                })
                .IsUnique();
            _ = entity.Property(value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerLogin).HasMaxLength(128);
            _ = entity.Property(value => value.ViewerDisplayName).HasMaxLength(160);
            _ = entity
                .HasOne<CommunitySeason>()
                .WithMany()
                .HasForeignKey(value => value.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommunityAudit>(entity =>
        {
            _ = entity.ToTable("community_audits");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.Action,
                    value.OperationKey,
                })
                .IsUnique();
            _ = entity.Property(value => value.Action).HasMaxLength(80);
            _ = entity.Property(value => value.OperationKey).HasMaxLength(200);
            _ = entity.Property(value => value.ActorTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ActorLogin).HasMaxLength(128);
            _ = entity.Property(value => value.PrivateNote).HasMaxLength(2000);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<CommunitySeason>()
                .WithMany()
                .HasForeignKey(value => value.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<CommunityDefinition>()
                .WithMany()
                .HasForeignKey(value => value.DefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<CommunityDomainEvent>(entity =>
        {
            _ = entity.ToTable("community_events");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.Kind,
                    value.OperationKey,
                })
                .IsUnique();
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.OperationKey).HasMaxLength(240);
            _ = entity.Property(value => value.PublicPayload).HasMaxLength(2000);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<CommunitySeason>()
                .WithMany()
                .HasForeignKey(value => value.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal static class CommunityProgressionPropertyBuilderExtensions
{
    internal static PropertyBuilder<TEnum> HasPersistedTokenConversion<TEnum>(
        this PropertyBuilder<TEnum> property
    )
        where TEnum : struct, Enum =>
        property
            .HasConversion(
                value => PersistedEnumTokens<TEnum>.Format(value),
                value => PersistedEnumTokens<TEnum>.Parse(value)
            )
            .HasMaxLength(32);
}

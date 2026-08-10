using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureBingo(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<BingoTemplate>(entity =>
        {
            _ = entity.ToTable("bingo_templates");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity
                .HasIndex(value => new { value.HostId, value.CreationOperationId })
                .IsUnique();
            _ = entity.Property(value => value.PublicId).HasConversion<string>();
            _ = entity.Property(value => value.Name).HasMaxLength(160);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoTemplateRevision>(entity =>
        {
            _ = entity.ToTable(
                "bingo_template_revisions",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_bingo_template_revisions_Dimension",
                        "Dimension IN (3, 4, 5)"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_bingo_template_revisions_Revision",
                        "Revision > 0"
                    );
                }
            );
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.TemplateId, value.Revision }).IsUnique();
            _ = entity.HasIndex(value => new { value.HostId, value.OperationId }).IsUnique();
            _ = entity.Property(value => value.LinePointsReward).HasMaxLength(128);
            _ = entity.Property(value => value.LineAchievementKey).HasMaxLength(80);
            _ = entity.Property(value => value.FullCardPointsReward).HasMaxLength(128);
            _ = entity.Property(value => value.FullCardAchievementKey).HasMaxLength(80);
            _ = entity.Property(value => value.CreatedByTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.CreatedByLogin).HasMaxLength(128);
            _ = entity
                .HasOne(value => value.Template)
                .WithMany(value => value.Revisions)
                .HasForeignKey(value => value.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoSquare>(entity =>
        {
            _ = entity.ToTable("bingo_squares");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.TemplateRevisionId, value.Key }).IsUnique();
            _ = entity
                .HasIndex(value => new { value.TemplateRevisionId, value.SortOrder })
                .IsUnique();
            _ = entity.Property(value => value.Key).HasMaxLength(80);
            _ = entity.Property(value => value.Title).HasMaxLength(240);
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.FilterToken).HasMaxLength(240);
            _ = entity.Property(value => value.PrivateModeratorNote).HasMaxLength(2000);
            _ = entity
                .HasOne(value => value.TemplateRevision)
                .WithMany(value => value.Squares)
                .HasForeignKey(value => value.TemplateRevisionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoGame>(entity =>
        {
            _ = entity.ToTable(
                "bingo_games",
                table =>
                    table.HasCheckConstraint("CK_bingo_games_Dimension", "Dimension IN (3, 4, 5)")
            );
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity
                .HasIndex(value => new { value.HostId, value.CreationOperationId })
                .IsUnique();
            _ = entity
                .HasIndex(value => value.HostId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Joining', 'Issued')");
            _ = entity.HasIndex(value => new { value.HostId, value.Status });
            _ = entity.Property(value => value.PublicId).HasConversion<string>();
            _ = entity.Property(value => value.TemplateName).HasMaxLength(160);
            _ = entity.Property(value => value.Seed).HasMaxLength(160);
            _ = entity.Property(value => value.Mode).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Status).HasPersistedTokenConversion();
            _ = entity.Property(value => value.RosterRevision).IsConcurrencyToken();
            _ = entity.Property(value => value.LinePointsReward).HasMaxLength(128);
            _ = entity.Property(value => value.LineAchievementKey).HasMaxLength(80);
            _ = entity.Property(value => value.FullCardPointsReward).HasMaxLength(128);
            _ = entity.Property(value => value.FullCardAchievementKey).HasMaxLength(80);
            _ = entity
                .HasOne(value => value.TemplateRevision)
                .WithMany()
                .HasForeignKey(value => value.TemplateRevisionId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoTeam>(entity =>
        {
            _ = entity.ToTable("bingo_teams");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.GameId, value.PublicId }).IsUnique();
            _ = entity.HasIndex(value => new { value.GameId, value.Name }).IsUnique();
            _ = entity.Property(value => value.PublicId).HasConversion<string>();
            _ = entity.Property(value => value.Name).HasMaxLength(160);
            _ = entity
                .HasOne(value => value.Game)
                .WithMany(value => value.Teams)
                .HasForeignKey(value => value.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoParticipant>(entity =>
        {
            _ = entity.ToTable("bingo_participants");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.GameId, value.TwitchUserId }).IsUnique();
            _ = entity.Property(value => value.TwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.Login).HasMaxLength(128);
            _ = entity.Property(value => value.DisplayName).HasMaxLength(160);
            _ = entity
                .HasOne(value => value.Game)
                .WithMany(value => value.Participants)
                .HasForeignKey(value => value.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne(value => value.Team)
                .WithMany(value => value.Participants)
                .HasForeignKey(value => value.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = entity
                .HasOne(value => value.Card)
                .WithMany(value => value.Participants)
                .HasForeignKey(value => value.CardId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        _ = modelBuilder.Entity<BingoCard>(entity =>
        {
            _ = entity.ToTable("bingo_cards");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.GameId, value.PublicId }).IsUnique();
            _ = entity.HasIndex(value => new { value.GameId, value.AssignmentKey }).IsUnique();
            _ = entity.Property(value => value.PublicId).HasConversion<string>();
            _ = entity.Property(value => value.AssignmentKey).HasMaxLength(240);
            _ = entity.Property(value => value.AssignmentName).HasMaxLength(160);
            _ = entity
                .HasOne(value => value.Game)
                .WithMany(value => value.Cards)
                .HasForeignKey(value => value.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoMark>(entity =>
        {
            _ = entity.ToTable("bingo_marks");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.CardId, value.SquareKey }).IsUnique();
            _ = entity.Property(value => value.SquareKey).HasMaxLength(80);
            _ = entity
                .HasOne(value => value.Card)
                .WithMany(value => value.Marks)
                .HasForeignKey(value => value.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoEvidence>(entity =>
        {
            _ = entity.ToTable("bingo_evidence");
            _ = entity.HasKey(value => value.Id);
            _ = entity.Property(value => value.Action).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Source).HasPersistedTokenConversion();
            _ = entity.Property(value => value.EventKind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.Summary).HasMaxLength(500);
            _ = entity.Property(value => value.ParticipantTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ParticipantLogin).HasMaxLength(128);
            _ = entity.Property(value => value.ParticipantDisplayName).HasMaxLength(160);
            _ = entity
                .HasOne(value => value.Mark)
                .WithMany(value => value.Evidence)
                .HasForeignKey(value => value.MarkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoModerationAudit>(entity =>
        {
            _ = entity.ToTable("bingo_moderation_audit");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.OperationId }).IsUnique();
            _ = entity.Property(value => value.Action).HasMaxLength(80);
            _ = entity.Property(value => value.ActorTwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.ActorLogin).HasMaxLength(128);
            _ = entity.Property(value => value.PrivateNote).HasMaxLength(2000);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoEventReceipt>(entity =>
        {
            _ = entity.ToTable("bingo_event_receipts");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.Kind,
                    value.SourceEventId,
                })
                .IsUnique();
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.SourceEventId).HasMaxLength(240);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoWin>(entity =>
        {
            _ = entity.ToTable("bingo_wins");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.CardId, value.RuleKey }).IsUnique();
            _ = entity.HasIndex(value => new { value.HostId, value.PublicId }).IsUnique();
            _ = entity.Property(value => value.PublicId).HasConversion<string>();
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.RuleKey).HasMaxLength(80);
            _ = entity.Property(value => value.PointsReward).HasMaxLength(128);
            _ = entity.Property(value => value.AchievementKey).HasMaxLength(80);
            _ = entity
                .HasOne(value => value.Game)
                .WithMany(value => value.Wins)
                .HasForeignKey(value => value.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne(value => value.Card)
                .WithMany(value => value.Wins)
                .HasForeignKey(value => value.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoWinRecipient>(entity =>
        {
            _ = entity.ToTable("bingo_win_recipients");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.WinId, value.TwitchUserId }).IsUnique();
            _ = entity.Property(value => value.TwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.Login).HasMaxLength(128);
            _ = entity.Property(value => value.DisplayName).HasMaxLength(160);
            _ = entity
                .HasOne(value => value.Win)
                .WithMany(value => value.Recipients)
                .HasForeignKey(value => value.WinId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BingoDomainEvent>(entity =>
        {
            _ = entity.ToTable("bingo_events");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.OperationKey }).IsUnique();
            _ = entity.Property(value => value.Kind).HasPersistedTokenConversion();
            _ = entity.Property(value => value.OperationKey).HasMaxLength(240);
            _ = entity.Property(value => value.PublicPayload).HasMaxLength(2000);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

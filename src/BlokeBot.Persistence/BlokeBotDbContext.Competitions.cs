using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureCompetitions(ModelBuilder modelBuilder)
    {
        ConfigureCompetition(modelBuilder);
        ConfigureCompetitionEntrants(modelBuilder);
        ConfigureCompetitionMatches(modelBuilder);
        ConfigureCompetitionMilestoneRewards(modelBuilder);
        ConfigureCompetitionHistory(modelBuilder);
    }

    private static void ConfigureCompetition(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<Competition>(b =>
        {
            _ = b.ToTable(
                "competitions",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_competitions_Capacity",
                        "Capacity BETWEEN 2 AND 128"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_competitions_TeamSize",
                        "TeamSize BETWEEN 1 AND 32"
                    );
                    _ = t.HasCheckConstraint("CK_competitions_Revision", "Revision > 0");
                    _ = t.HasCheckConstraint("CK_competitions_WinPoints", "WinPoints >= 0");
                    _ = t.HasCheckConstraint("CK_competitions_DrawPoints", "DrawPoints >= 0");
                    _ = t.HasCheckConstraint("CK_competitions_LossPoints", "LossPoints >= 0");
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.PublicId).HasConversion<string>();
            _ = b.Property(x => x.Name).HasMaxLength(160);
            _ = b.Property(x => x.Description).HasMaxLength(2000);
            _ = b.Property(x => x.Format).HasPersistedTokenConversion();
            _ = b.Property(x => x.EntryKind).HasPersistedTokenConversion();
            _ = b.Property(x => x.Status).HasPersistedTokenConversion();
            _ = b.Property(x => x.Seeding).HasPersistedTokenConversion();
            _ = b.Property(x => x.Tiebreak).HasPersistedTokenConversion();
            _ = b.Property(x => x.MinimumPoints).HasMaxLength(128);
            _ = b.Property(x => x.Seed).HasMaxLength(128);
            _ = b.Property(x => x.AlgorithmVersion).HasMaxLength(64);
            _ = b.Property(x => x.WinnerPoints).HasMaxLength(128);
            _ = b.Property(x => x.RunnerUpPoints).HasMaxLength(128);
            _ = b.Property(x => x.WinnerAchievementKey).HasMaxLength(80);
            _ = b.Property(x => x.RunnerUpAchievementKey).HasMaxLength(80);
            _ = b.Property(x => x.PrivateLobbyInformation).HasMaxLength(1000);
            _ = b.Property(x => x.ReminderMessage).HasMaxLength(500);
            _ = b.Property(x => x.Revision).IsConcurrencyToken();
            _ = b.HasIndex(x => x.PublicId).IsUnique();
            _ = b.HasIndex(x => new { x.HostId, x.CreationOperationId }).IsUnique();
            _ = b.HasIndex(x => new { x.HostId, x.Status });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureCompetitionEntrants(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CompetitionEntrant>(b =>
        {
            _ = b.ToTable("competition_entrants");
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.PublicId).HasConversion<string>();
            _ = b.Property(x => x.Name).HasMaxLength(160);
            _ = b.HasIndex(x => x.PublicId).IsUnique();
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CompetitionId,
                    x.RegistrationOperationId,
                })
                .IsUnique();
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CompetitionId,
                    x.Name,
                })
                .IsUnique()
                .HasFilter("\"Name\" <> '[erased]'");
            _ = b.HasOne(x => x.Competition)
                .WithMany(x => x.Entrants)
                .HasForeignKey(x => new { x.HostId, x.CompetitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CompetitionEntrantMember>(b =>
        {
            _ = b.ToTable("competition_entrant_members");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(x => x.Login).HasMaxLength(128);
            _ = b.Property(x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(x => x.PrivateContact).HasMaxLength(500);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CompetitionEntrantId,
                    x.Login,
                })
                .IsUnique()
                .HasFilter("\"Login\" <> '[erased]'");
            _ = b.HasOne(x => x.Entrant)
                .WithMany(x => x.Members)
                .HasForeignKey(x => new { x.HostId, x.CompetitionEntrantId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCompetitionMatches(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<CompetitionMatch>(b =>
        {
            _ = b.ToTable(
                "competition_matches",
                t =>
                {
                    _ = t.HasCheckConstraint("CK_competition_matches_Round", "Round > 0");
                    _ = t.HasCheckConstraint("CK_competition_matches_Position", "Position >= 0");
                    _ = t.HasCheckConstraint(
                        "CK_competition_matches_ScoreA",
                        "ScoreA IS NULL OR ScoreA >= 0"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_competition_matches_ScoreB",
                        "ScoreB IS NULL OR ScoreB >= 0"
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.PublicId).HasConversion<string>();
            _ = b.Property(x => x.Status).HasPersistedTokenConversion();
            _ = b.HasIndex(x => x.PublicId).IsUnique();
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CompetitionId,
                    x.Round,
                    x.Position,
                })
                .IsUnique();
            _ = b.HasIndex(x => new
            {
                x.ReminderDueAtUtc,
                x.ReminderDeliveredAtUtc,
                x.ReminderSuppressedAtUtc,
            });
            _ = b.HasOne(x => x.Competition)
                .WithMany(x => x.Matches)
                .HasForeignKey(x => new { x.HostId, x.CompetitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.EntrantA)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.EntrantAId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(x => x.EntrantB)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.EntrantBId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(x => x.WinnerEntrant)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.WinnerEntrantId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

    private static void ConfigureCompetitionMilestoneRewards(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<CompetitionMilestoneRewardRule>(b =>
        {
            _ = b.ToTable(
                "competition_milestone_reward_rules",
                t =>
                    t.HasCheckConstraint(
                        "CK_competition_milestone_reward_rules_WinsRequired",
                        "WinsRequired > 0"
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Points).HasMaxLength(128);
            _ = b.Property(x => x.AchievementKey).HasMaxLength(80);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CompetitionId,
                    x.WinsRequired,
                })
                .IsUnique();
            _ = b.HasOne(x => x.Competition)
                .WithMany(x => x.MilestoneRewards)
                .HasForeignKey(x => new { x.HostId, x.CompetitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureCompetitionHistory(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CompetitionAudit>(b =>
        {
            _ = b.ToTable("competition_audits");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Action).HasPersistedTokenConversion();
            _ = b.Property(x => x.ActorTwitchUserId).HasMaxLength(128);
            _ = b.Property(x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(x => x.PrivateReason).HasMaxLength(1000);
            _ = b.HasIndex(x => new { x.HostId, x.OperationId }).IsUnique();
            _ = b.HasOne(x => x.Competition)
                .WithMany(x => x.Audits)
                .HasForeignKey(x => new { x.HostId, x.CompetitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CompetitionDomainEvent>(b =>
        {
            _ = b.ToTable("competition_events");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.CompetitionPublicId).HasConversion<string>();
            _ = b.Property(x => x.OperationKey).HasMaxLength(200);
            _ = b.Property(x => x.Kind).HasPersistedTokenConversion();
            _ = b.Property(x => x.PublicPayload).HasMaxLength(2000);
            _ = b.HasIndex(x => new { x.HostId, x.OperationKey }).IsUnique();
            _ = b.HasOne(x => x.Competition)
                .WithMany(x => x.Events)
                .HasForeignKey(x => new { x.HostId, x.CompetitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CompetitionRewardReceipt>(b =>
        {
            _ = b.ToTable(
                "competition_reward_receipts",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_competition_reward_receipts_Placement",
                        "Placement IS NULL OR Placement > 0"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_competition_reward_receipts_WinsRequired",
                        "WinsRequired IS NULL OR WinsRequired > 0"
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(x => x.Login).HasMaxLength(128);
            _ = b.Property(x => x.Kind).HasPersistedTokenConversion();
            _ = b.Property(x => x.RewardKey).HasMaxLength(80);
            _ = b.Property(x => x.PointsGranted).HasMaxLength(128);
            _ = b.Property(x => x.AchievementKey).HasMaxLength(80);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CompetitionId,
                    x.EntrantId,
                    x.Login,
                    x.RewardKey,
                })
                .IsUnique();
            _ = b.HasOne(x => x.Competition)
                .WithMany(x => x.Rewards)
                .HasForeignKey(x => new { x.HostId, x.CompetitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

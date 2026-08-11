using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureViewerPassports(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<ViewerPassport>(entity =>
        {
            _ = entity.ToTable("viewer_passports");
            _ = entity.HasKey(value => value.Id);
            _ = entity.HasIndex(value => new { value.HostId, value.TwitchUserId }).IsUnique();
            _ = entity
                .HasIndex(value => new { value.HostId, value.Login })
                .IsUnique()
                .HasFilter("\"Login\" <> ''");
            _ = entity.Property(value => value.TwitchUserId).HasMaxLength(128);
            _ = entity.Property(value => value.Login).HasMaxLength(128);
            _ = entity.Property(value => value.DisplayName).HasMaxLength(160);
            _ = entity.Property(value => value.ProfileLine).HasMaxLength(160);
            _ = entity.Property(value => value.Visibility).HasPersistedTokenConversion();
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<CommunityRewardDefinition>()
                .WithMany()
                .HasForeignKey(value => value.SelectedTitleRewardDefinitionId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = entity
                .HasOne<CommunityRewardDefinition>()
                .WithMany()
                .HasForeignKey(value => value.SelectedBadgeRewardDefinitionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        _ = modelBuilder.Entity<ViewerPassportLogin>(entity =>
        {
            _ = entity.ToTable("viewer_passport_logins");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.PassportId,
                    value.Login,
                })
                .IsUnique();
            _ = entity.HasIndex(value => new { value.HostId, value.Login });
            _ = entity.Property(value => value.Login).HasMaxLength(128);
            _ = entity
                .HasOne(value => value.Passport)
                .WithMany()
                .HasForeignKey(value => value.PassportId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<ViewerPassportAttendanceDay>(entity =>
        {
            _ = entity.ToTable("viewer_passport_attendance_days");
            _ = entity.HasKey(value => value.Id);
            _ = entity
                .HasIndex(value => new
                {
                    value.HostId,
                    value.PassportId,
                    value.DateUtc,
                })
                .IsUnique();
            _ = entity
                .HasOne<ViewerPassport>()
                .WithMany()
                .HasForeignKey(value => value.PassportId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

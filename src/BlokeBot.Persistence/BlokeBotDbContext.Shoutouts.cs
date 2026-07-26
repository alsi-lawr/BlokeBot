using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureShoutouts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShoutoutHistoryEntry>(b =>
        {
            b.ToTable("shoutout_history");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.Property(x => x.SourceTwitchUserId).HasMaxLength(64);
            b.Property(x => x.SourceLogin).HasMaxLength(128);
            b.Property(x => x.TargetTwitchUserId).HasMaxLength(64);
            b.Property(x => x.TargetLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.OccurredAtUtc });
            b.HasIndex(x => new { x.HostId, x.ProviderMessageId }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShoutoutCooldownState>(b =>
        {
            b.ToTable("shoutout_cooldowns");
            b.HasKey(x => x.Id);
            b.Property(x => x.TargetTwitchUserId).HasMaxLength(64);
            b.Property(x => x.TargetLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.TargetTwitchUserId }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

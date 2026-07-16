using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureHosts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BotHost>(b =>
        {
            b.ToTable("hosts");
            b.HasKey(x => x.Id);
            b.Property(x => x.BotRuntimeState);
            b.Property(x => x.BotRuntimeStateChangedAtUtc);
            b.Property(x => x.ChannelBotAuthorizedAtUtc);
            b.Property(x => x.ChannelBotAuthorizedScopes).HasMaxLength(512);
            b.Property(x => x.EnabledFeatures)
                .HasConversion(features => (long)features, value => (HostFeatureFlags)(ulong)value)
                .HasDefaultValue(HostFeatureFlags.All);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            b.Property(x => x.TimeZoneId).HasMaxLength(128).HasDefaultValue("UTC");
            b.Property(x => x.TwitchUserId).HasMaxLength(64);
            b.HasIndex(x => x.Login).IsUnique();
        });

        modelBuilder.Entity<HostBotAccountSettings>(b =>
        {
            b.ToTable("host_bot_account_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.AccessToken).HasMaxLength(4096);
            b.Property(x => x.AuthorizedScopes).HasMaxLength(512);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            b.Property(x => x.RefreshToken).HasMaxLength(4096);
            b.Property(x => x.TwitchUserId).HasMaxLength(64);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WhisperQuotaBucket>(b =>
        {
            b.ToTable("whisper_quota_buckets");
            b.HasKey(x => x.Id);
            b.Property(x => x.BotTwitchUserId).HasMaxLength(64);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.BotTwitchUserId,
                    x.DayUtc,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Recipients)
                .WithOne(x => x.WhisperQuotaBucket)
                .HasForeignKey(x => x.WhisperQuotaBucketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WhisperQuotaRecipient>(b =>
        {
            b.ToTable("whisper_quota_recipients");
            b.HasKey(x => x.Id);
            b.Property(x => x.RecipientLogin).HasMaxLength(128);
            b.Property(x => x.RecipientTwitchUserId).HasMaxLength(64);
            b.HasIndex(x => new { x.WhisperQuotaBucketId, x.RecipientTwitchUserId }).IsUnique();
        });
    }
}

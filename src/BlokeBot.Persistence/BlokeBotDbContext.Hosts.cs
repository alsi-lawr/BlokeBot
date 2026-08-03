using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureHosts(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<BotHost>(b =>
        {
            _ = b.ToTable("hosts");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.BotRuntimeState);
            _ = b.Property(x => x.BotRuntimeStateChangedAtUtc);
            _ = b.Property(x => x.ChannelBotAuthorizedAtUtc);
            _ = b.Property(x => x.ChannelBotAuthorizedScopes).HasMaxLength(512);
            _ = b.Property(x => x.EnabledFeatures)
                .HasConversion(features => (long)features, value => (HostFeatureFlags)(ulong)value)
                .HasDefaultValue(HostFeatureFlags.None);
            _ = b.Property(x => x.Login).HasMaxLength(128);
            _ = b.Property(x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            _ = b.Property(x => x.StartupMessageText).HasMaxLength(500);
            _ = b.Property(x => x.CommandsDefaultConflictAlias).HasMaxLength(64);
            _ = b.Property(x => x.TimeZoneId).HasMaxLength(128).HasDefaultValue("UTC");
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(x => x.Login).IsUnique();
        });

        _ = modelBuilder.Entity<HostBotAccountSettings>(b =>
        {
            _ = b.ToTable("host_bot_account_settings");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.AuthorizedScopes).HasMaxLength(512);
            _ = b.Property(x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(x => x.Login).HasMaxLength(128);
            _ = b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<WhisperQuotaBucket>(b =>
        {
            _ = b.ToTable("whisper_quota_buckets");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.BotTwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.BotTwitchUserId,
                    x.DayUtc,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Recipients)
                .WithOne(x => x.WhisperQuotaBucket)
                .HasForeignKey(x => x.WhisperQuotaBucketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<WhisperQuotaRecipient>(b =>
        {
            _ = b.ToTable("whisper_quota_recipients");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.RecipientLogin).HasMaxLength(128);
            _ = b.Property(x => x.RecipientTwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(x => new { x.WhisperQuotaBucketId, x.RecipientTwitchUserId }).IsUnique();
        });
    }
}

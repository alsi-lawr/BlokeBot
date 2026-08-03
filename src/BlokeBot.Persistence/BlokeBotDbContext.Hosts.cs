using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureHosts(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<BotHost>(static b =>
        {
            _ = b.ToTable("hosts");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.BotRuntimeState);
            _ = b.Property(static x => x.BotRuntimeStateChangedAtUtc);
            _ = b.Property(static x => x.ChannelBotAuthorizedAtUtc);
            _ = b.Property(static x => x.ChannelBotAuthorizedScopes).HasMaxLength(512);
            _ = b.Property(static x => x.EnabledFeatures)
                .HasConversion(
                    static features => (long)features,
                    static value => (HostFeatureFlags)(ulong)value
                )
                .HasDefaultValue(HostFeatureFlags.None);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(static x => x.ProfileImageUrl).HasMaxLength(512);
            _ = b.Property(static x => x.StartupMessageText).HasMaxLength(500);
            _ = b.Property(static x => x.CommandsDefaultConflictAlias).HasMaxLength(64);
            _ = b.Property(static x => x.TimeZoneId).HasMaxLength(128).HasDefaultValue("UTC");
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(static x => x.Login).IsUnique();
        });

        _ = modelBuilder.Entity<HostBotAccountSettings>(static b =>
        {
            _ = b.ToTable("host_bot_account_settings");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.AuthorizedScopes).HasMaxLength(512);
            _ = b.Property(static x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.ProfileImageUrl).HasMaxLength(512);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(static x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<WhisperQuotaBucket>(static b =>
        {
            _ = b.ToTable("whisper_quota_buckets");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.BotTwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.BotTwitchUserId,
                    x.DayUtc,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Recipients)
                .WithOne(static x => x.WhisperQuotaBucket)
                .HasForeignKey(static x => x.WhisperQuotaBucketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<WhisperQuotaRecipient>(static b =>
        {
            _ = b.ToTable("whisper_quota_recipients");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.RecipientLogin).HasMaxLength(128);
            _ = b.Property(static x => x.RecipientTwitchUserId).HasMaxLength(64);
            _ = b.HasIndex(static x => new { x.WhisperQuotaBucketId, x.RecipientTwitchUserId })
                .IsUnique();
        });
    }
}

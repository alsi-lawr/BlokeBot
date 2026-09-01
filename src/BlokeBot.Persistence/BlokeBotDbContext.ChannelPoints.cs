using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchRewardRedemptionStatusKinds =
        PersistedEnumTokens<TwitchRewardRedemptionStatus>.Values.ToArray();

    private static void ConfigureChannelPoints(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<TwitchCustomReward>(b =>
        {
            _ = b.ToTable("twitch_custom_rewards");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.ProviderRewardId).HasMaxLength(128);
            _ = b.Property(static x => x.Title).HasMaxLength(45);
            _ = b.Property(static x => x.Prompt).HasMaxLength(200);
            _ = b.Property(static x => x.BackgroundColor).HasMaxLength(16);
            _ = b.HasIndex(static x => new { x.HostId, x.ProviderRewardId }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchRewardRedemption>(b =>
        {
            _ = b.ToTable(
                "twitch_reward_redemptions",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_reward_redemptions_Status",
                        KindIn(modelBuilder, "Status", _twitchRewardRedemptionStatusKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.ProviderRedemptionId).HasMaxLength(128);
            _ = b.Property(static x => x.ProviderRewardId).HasMaxLength(128);
            _ = b.Property(static x => x.RewardTitle).HasMaxLength(45);
            _ = b.Property(static x => x.UserId).HasMaxLength(64);
            _ = b.Property(static x => x.UserLogin).HasMaxLength(128);
            _ = b.Property(static x => x.UserInput).HasMaxLength(500);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static status =>
                        PersistedEnumTokens<TwitchRewardRedemptionStatus>.Format(status),
                    static token => PersistedEnumTokens<TwitchRewardRedemptionStatus>.Parse(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.ProviderRedemptionId }).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.Status,
                x.UpdatedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

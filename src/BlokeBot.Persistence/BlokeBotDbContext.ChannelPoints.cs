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
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderRewardId).HasMaxLength(128);
            _ = b.Property(x => x.Title).HasMaxLength(45);
            _ = b.Property(x => x.Prompt).HasMaxLength(200);
            _ = b.Property(x => x.BackgroundColor).HasMaxLength(16);
            _ = b.HasIndex(x => new { x.HostId, x.ProviderRewardId }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchRewardRedemption>(b =>
        {
            _ = b.ToTable(
                "twitch_reward_redemptions",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_reward_redemptions_Status",
                        KindIn("Status", _twitchRewardRedemptionStatusKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderRedemptionId).HasMaxLength(128);
            _ = b.Property(x => x.ProviderRewardId).HasMaxLength(128);
            _ = b.Property(x => x.RewardTitle).HasMaxLength(45);
            _ = b.Property(x => x.UserId).HasMaxLength(64);
            _ = b.Property(x => x.UserLogin).HasMaxLength(128);
            _ = b.Property(x => x.UserInput).HasMaxLength(500);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchRewardRedemptionStatus>.Format(status),
                    token => PersistedEnumTokens<TwitchRewardRedemptionStatus>.Parse(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.ProviderRedemptionId }).IsUnique();
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.Status,
                x.UpdatedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

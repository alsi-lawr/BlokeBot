using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchRewardRedemptionStatusKinds =
        PersistedEnumTokens<TwitchRewardRedemptionStatus>.Values.ToArray();

    private static void ConfigureChannelPoints(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TwitchCustomReward>(b =>
        {
            b.ToTable("twitch_custom_rewards");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderRewardId).HasMaxLength(128);
            b.Property(x => x.Title).HasMaxLength(45);
            b.Property(x => x.Prompt).HasMaxLength(200);
            b.Property(x => x.BackgroundColor).HasMaxLength(16);
            b.HasIndex(x => new { x.HostId, x.ProviderRewardId }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwitchRewardRedemption>(b =>
        {
            b.ToTable(
                "twitch_reward_redemptions",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_reward_redemptions_Status",
                        KindIn("Status", _twitchRewardRedemptionStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderRedemptionId).HasMaxLength(128);
            b.Property(x => x.ProviderRewardId).HasMaxLength(128);
            b.Property(x => x.RewardTitle).HasMaxLength(45);
            b.Property(x => x.UserId).HasMaxLength(64);
            b.Property(x => x.UserLogin).HasMaxLength(128);
            b.Property(x => x.UserInput).HasMaxLength(500);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchRewardRedemptionStatus>.Format(status),
                    token => PersistedEnumTokens<TwitchRewardRedemptionStatus>.Parse(token)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.ProviderRedemptionId }).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.Status,
                x.UpdatedAtUtc,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

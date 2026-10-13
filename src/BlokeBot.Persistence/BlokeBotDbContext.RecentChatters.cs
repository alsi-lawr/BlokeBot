using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<RecentChannelChatter> RecentChannelChatters => Set<RecentChannelChatter>();

    private static void ConfigureRecentChatters(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<RecentChannelChatter>(b =>
        {
            _ = b.ToTable("recent_channel_chatters");
            _ = b.HasKey(x => new { x.HostId, x.TwitchUserId });
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.ObservedAtUtc,
                x.TwitchUserId,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}

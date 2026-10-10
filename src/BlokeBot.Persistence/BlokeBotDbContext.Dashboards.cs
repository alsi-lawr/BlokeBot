using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<ChannelDashboard> ChannelDashboards => Set<ChannelDashboard>();

    private static void ConfigureDashboards(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<ChannelDashboard>(b =>
        {
            _ = b.ToTable("channel_dashboards");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Id).HasConversion<string>();
            _ = b.Property(x => x.Revision).IsConcurrencyToken();
            _ = b.HasIndex(x => x.HostId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}

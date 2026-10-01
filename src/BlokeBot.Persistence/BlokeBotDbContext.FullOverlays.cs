using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<FullOverlay> FullOverlays => Set<FullOverlay>();
    public DbSet<FullOverlayPublication> FullOverlayPublications => Set<FullOverlayPublication>();

    private static void ConfigureFullOverlays(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<FullOverlay>(b =>
        {
            _ = b.ToTable("full_overlays");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.PublicId).HasConversion<string>();
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.Property(x => x.AccessKeyDigest).HasMaxLength(32);
            _ = b.Property(x => x.Revision).IsConcurrencyToken();
            _ = b.HasIndex(x => x.PublicId).IsUnique();
            _ = b.HasIndex(x => x.AccessKeyDigest).IsUnique();
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.IsArchived,
                x.PublicId,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<FullOverlayPublication>(b =>
        {
            _ = b.ToTable("full_overlay_publications");
            _ = b.HasKey(x => new { x.OverlayId, x.Version });
            _ = b.Property(x => x.AuthorUserId).HasMaxLength(128);
            _ = b.Property(x => x.AuthorLogin).HasMaxLength(128);
            _ = b.HasOne<FullOverlay>()
                .WithMany()
                .HasForeignKey(x => x.OverlayId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

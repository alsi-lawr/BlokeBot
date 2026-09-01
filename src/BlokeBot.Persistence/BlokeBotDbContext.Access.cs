using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _accessKinds =
        PersistedEnumTokens<AccessListEntryKind>.Values.ToArray();

    private static void ConfigureAccess(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<SiteAccessSettings>(b =>
        {
            _ = b.ToTable("site_access_settings");
            _ = b.HasKey(static x => x.Id);
        });

        _ = modelBuilder.Entity<SiteAccessEntry>(b =>
        {
            _ = b.ToTable(
                "site_access_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_site_access_entries_Kind",
                        KindIn(modelBuilder, "Kind", _accessKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static kind => PersistedEnumTokens<AccessListEntryKind>.Format(kind),
                    static value => PersistedEnumTokens<AccessListEntryKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.Kind, x.Login }).IsUnique();
        });

        _ = modelBuilder.Entity<HostModAccessSettings>(b =>
        {
            _ = b.ToTable("host_mod_access_settings");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.AllowModsByDefault).HasDefaultValue(true);
            _ = b.HasIndex(static x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<HostModAccessEntry>(b =>
        {
            _ = b.ToTable(
                "host_mod_access_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_host_mod_access_entries_Kind",
                        KindIn(modelBuilder, "Kind", _accessKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static kind => PersistedEnumTokens<AccessListEntryKind>.Format(kind),
                    static value => PersistedEnumTokens<AccessListEntryKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.Kind,
                    x.Login,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

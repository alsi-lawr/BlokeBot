using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _accessKinds =
        PersistedEnumTokens<AccessListEntryKind>.Values.ToArray();

    private static void ConfigureAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SiteAccessSettings>(b =>
        {
            b.ToTable("site_access_settings");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SiteAccessEntry>(b =>
        {
            b.ToTable(
                "site_access_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_site_access_entries_Kind",
                        KindIn("Kind", _accessKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AccessListEntryKind>.Format(kind),
                    value => PersistedEnumTokens<AccessListEntryKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.Kind, x.Login }).IsUnique();
        });

        modelBuilder.Entity<HostModAccessSettings>(b =>
        {
            b.ToTable("host_mod_access_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.AllowModsByDefault).HasDefaultValue(true);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HostModAccessEntry>(b =>
        {
            b.ToTable(
                "host_mod_access_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_host_mod_access_entries_Kind",
                        KindIn("Kind", _accessKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AccessListEntryKind>.Format(kind),
                    value => PersistedEnumTokens<AccessListEntryKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Kind,
                    x.Login,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

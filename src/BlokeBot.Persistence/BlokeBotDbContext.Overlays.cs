using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _overlayTypes =
        PersistedEnumTokens<OverlayType>.Values.ToArray();
    private static readonly string[] _overlayInstanceEventKinds =
        PersistedEnumTokens<OverlayInstanceEventKind>.Values.ToArray();

    private static void ConfigureOverlays(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OverlayInstance>(b =>
        {
            b.ToTable(
                "overlay_instances",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_overlay_instances_Type",
                        KindIn("Type", _overlayTypes)
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_instances_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_instances_ConfigurationJson",
                        "length(ConfigurationJson) BETWEEN 1 AND 4096 "
                            + "AND json_valid(ConfigurationJson) "
                            + "AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' "
                            + "AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_instances_AccessKeyDigest",
                        "length(AccessKeyDigest) = 32"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_instances_Versions",
                        "KeyVersion > 0 AND Revision > 0"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.PublicId).HasConversion<string>();
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.Type)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayType>.Format(value),
                    value => PersistedEnumTokens<OverlayType>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.ConfigurationJson).HasMaxLength(4096);
            b.Property(x => x.AccessKeyDigest).HasMaxLength(32);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasIndex(x => x.PublicId).IsUnique();
            b.HasIndex(x => x.AccessKeyDigest).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.UpdatedAtUtc,
                x.PublicId,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OverlayInstanceDomainEvent>(b =>
        {
            b.ToTable(
                "overlay_instance_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_overlay_instance_events_Kind",
                        KindIn("Kind", _overlayInstanceEventKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.OverlayPublicId).HasConversion<string>();
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayInstanceEventKind>.Format(value),
                    value => PersistedEnumTokens<OverlayInstanceEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.ActorUserId).HasMaxLength(128);
            b.Property(x => x.ActorLogin).HasMaxLength(128);
            b.HasIndex(x => new
            {
                x.HostId,
                x.OverlayPublicId,
                x.Id,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

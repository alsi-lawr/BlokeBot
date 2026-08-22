using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureOverlayInstances(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<OverlayInstance>(static b =>
        {
            _ = b.ToTable(
                "overlay_instances",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_instances_Type",
                        KindIn("Type", _overlayTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_instances_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_instances_ConfigurationJson",
                        "length(ConfigurationJson) BETWEEN 1 AND 8192 "
                            + "AND json_valid(ConfigurationJson) "
                            + "AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' "
                            + "AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_instances_AccessKeyDigest",
                        "length(AccessKeyDigest) = 32"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_instances_Versions",
                        "KeyVersion > 0 AND Revision > 0"
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.PublicId).HasConversion<string>();
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.Type)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayType>.Format(value),
                    static value => PersistedEnumTokens<OverlayType>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.ConfigurationJson).HasMaxLength(8192);
            _ = b.Property(static x => x.AccessKeyDigest).HasMaxLength(32);
            _ = b.Property(static x => x.Revision).IsConcurrencyToken();
            _ = b.HasIndex(static x => x.PublicId).IsUnique();
            _ = b.HasIndex(static x => x.AccessKeyDigest).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.UpdatedAtUtc,
                x.PublicId,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<OverlayInstanceDomainEvent>(static b =>
        {
            _ = b.ToTable(
                "overlay_instance_events",
                static t =>
                    t.HasCheckConstraint(
                        "CK_overlay_instance_events_Kind",
                        KindIn("Kind", _overlayInstanceEventKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.OverlayPublicId).HasConversion<string>();
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayInstanceEventKind>.Format(value),
                    static value => PersistedEnumTokens<OverlayInstanceEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.ActorUserId).HasMaxLength(128);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.OverlayPublicId,
                x.Id,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

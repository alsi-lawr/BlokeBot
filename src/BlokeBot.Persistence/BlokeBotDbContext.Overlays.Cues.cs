using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureOverlayCues(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<OverlayCue>(static b =>
        {
            _ = b.ToTable(
                "overlay_cues",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_Duration",
                        "DurationMilliseconds BETWEEN 100 AND 300000"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_QueuePolicy",
                        KindIn("QueuePolicy", _overlayCueQueuePolicies)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_ConfigurationJson",
                        "length(ConfigurationJson) BETWEEN 1 AND 32768 "
                            + "AND json_valid(ConfigurationJson) "
                            + "AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' "
                            + "AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
                    );
                    _ = t.HasCheckConstraint("CK_overlay_cues_Revision", "Revision > 0");
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.Id, x.HostId });
            _ = b.Property(static x => x.PublicId).HasConversion<string>();
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.QueuePolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayCueQueuePolicy>.Format(value),
                    static value => PersistedEnumTokens<OverlayCueQueuePolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.ConfigurationJson).HasMaxLength(32768);
            _ = b.Property(static x => x.Revision).IsConcurrencyToken();
            _ = b.HasIndex(static x => x.PublicId).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.Name,
                x.PublicId,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}

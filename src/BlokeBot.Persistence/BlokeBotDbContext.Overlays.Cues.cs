using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private void ConfigureOverlayCues(ModelBuilder modelBuilder)
    {
        var configurationJsonConstraint = VersionedJsonObjectConstraint("ConfigurationJson", 32768);
        _ = modelBuilder.Entity<OverlayCue>(b =>
        {
            _ = b.ToTable(
                "overlay_cues",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_Name",
                        ProviderSql(
                            modelBuilder,
                            "length(Name) BETWEEN 1 AND 128",
                            "length(\"Name\") BETWEEN 1 AND 128"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_Duration",
                        ProviderSql(
                            modelBuilder,
                            "DurationMilliseconds BETWEEN 100 AND 300000",
                            "\"DurationMilliseconds\" BETWEEN 100 AND 300000"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_QueuePolicy",
                        KindIn(modelBuilder, "QueuePolicy", _overlayCueQueuePolicies)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_ConfigurationJson",
                        configurationJsonConstraint
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_cues_Revision",
                        ProviderSql(modelBuilder, "Revision > 0", "\"Revision\" > 0")
                    );
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
}

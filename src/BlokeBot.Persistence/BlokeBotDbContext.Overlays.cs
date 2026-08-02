using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _overlayTypes =
        PersistedEnumTokens<OverlayType>.Values.ToArray();
    private static readonly string[] _overlayInstanceEventKinds =
        PersistedEnumTokens<OverlayInstanceEventKind>.Values.ToArray();
    private static readonly string[] _overlayCueQueuePolicies =
        PersistedEnumTokens<OverlayCueQueuePolicy>.Values.ToArray();
    private static readonly string[] _overlayEventFeedKinds =
        PersistedEnumTokens<OverlayEventFeedKind>.Values.ToArray();
    private static readonly string[] _overlayEventFeedPriorities =
        PersistedEnumTokens<OverlayEventFeedPriority>.Values.ToArray();
    private static readonly string[] _overlayEventFeedLifecycles =
        PersistedEnumTokens<OverlayEventFeedLifecycle>.Values.ToArray();

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
                        "length(ConfigurationJson) BETWEEN 1 AND 8192 "
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
            b.Property(x => x.ConfigurationJson).HasMaxLength(8192);
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

        modelBuilder.Entity<OverlayCue>(b =>
        {
            b.ToTable(
                "overlay_cues",
                t =>
                {
                    t.HasCheckConstraint("CK_overlay_cues_Name", "length(Name) BETWEEN 1 AND 128");
                    t.HasCheckConstraint(
                        "CK_overlay_cues_Duration",
                        "DurationMilliseconds BETWEEN 100 AND 300000"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_cues_QueuePolicy",
                        KindIn("QueuePolicy", _overlayCueQueuePolicies)
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_cues_ConfigurationJson",
                        "length(ConfigurationJson) BETWEEN 1 AND 32768 "
                            + "AND json_valid(ConfigurationJson) "
                            + "AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' "
                            + "AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
                    );
                    t.HasCheckConstraint("CK_overlay_cues_Revision", "Revision > 0");
                }
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.Id, x.HostId });
            b.Property(x => x.PublicId).HasConversion<string>();
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.QueuePolicy)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayCueQueuePolicy>.Format(value),
                    value => PersistedEnumTokens<OverlayCueQueuePolicy>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.ConfigurationJson).HasMaxLength(32768);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasIndex(x => x.PublicId).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.Name,
                x.PublicId,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OverlayMediaAsset>(b =>
        {
            b.ToTable(
                "overlay_media_assets",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_overlay_media_assets_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_media_assets_ContentType",
                        "ContentType LIKE 'image/%' OR ContentType LIKE 'audio/%' OR ContentType LIKE 'video/%'"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_media_assets_Length",
                        "ByteLength > 0 AND ContentRevision > 0"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_media_assets_StorageKey",
                        "length(StorageKey) = 32"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.Id, x.HostId });
            b.Property(x => x.PublicId).HasConversion<string>();
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.ContentType).HasMaxLength(32);
            b.Property(x => x.StorageKey).HasMaxLength(32);
            b.HasIndex(x => x.PublicId).IsUnique();
            b.HasIndex(x => x.StorageKey).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.Name,
                x.PublicId,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OverlayCueMediaAssetReference>(b =>
        {
            b.ToTable("overlay_cue_media_asset_references");
            b.HasKey(x => new { x.CueId, x.AssetId });
            b.HasIndex(x => new { x.HostId, x.AssetId });
            b.HasOne(x => x.Cue)
                .WithMany()
                .HasForeignKey(x => new { x.CueId, x.HostId })
                .HasPrincipalKey(x => new { x.Id, x.HostId })
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Asset)
                .WithMany()
                .HasForeignKey(x => new { x.AssetId, x.HostId })
                .HasPrincipalKey(x => new { x.Id, x.HostId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OverlayEventFeedItem>(b =>
        {
            b.ToTable(
                "overlay_event_feed_items",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Kind",
                        KindIn("Kind", _overlayEventFeedKinds)
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Priority",
                        KindIn("Priority", _overlayEventFeedPriorities)
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Lifecycle",
                        KindIn("Lifecycle", _overlayEventFeedLifecycles)
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_SourceKey",
                        "length(SourceKey) BETWEEN 1 AND 160"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Text",
                        "length(Title) BETWEEN 1 AND 160 AND length(Body) >= 1"
                    );
                    t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Duration",
                        "DurationSeconds BETWEEN 1 AND 30"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayEventFeedKind>.Format(value),
                    value => PersistedEnumTokens<OverlayEventFeedKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.Priority)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayEventFeedPriority>.Format(value),
                    value => PersistedEnumTokens<OverlayEventFeedPriority>.Parse(value)
                )
                .HasMaxLength(16);
            b.Property(x => x.Lifecycle)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayEventFeedLifecycle>.Format(value),
                    value => PersistedEnumTokens<OverlayEventFeedLifecycle>.Parse(value)
                )
                .HasMaxLength(16);
            b.Property(x => x.SourceKey).HasMaxLength(160);
            b.Property(x => x.Title).HasMaxLength(160);
            b.HasIndex(x => new
                {
                    x.OverlayInstanceId,
                    x.Kind,
                    x.SourceKey,
                })
                .IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.OverlayInstanceId,
                x.Lifecycle,
                x.EnqueuedAtUtc,
            });
            b.HasOne(x => x.OverlayInstance)
                .WithMany()
                .HasForeignKey(x => x.OverlayInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

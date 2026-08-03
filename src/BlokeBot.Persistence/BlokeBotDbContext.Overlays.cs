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

        _ = modelBuilder.Entity<OverlayMediaAsset>(static b =>
        {
            _ = b.ToTable(
                "overlay_media_assets",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_assets_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_assets_ContentType",
                        "ContentType LIKE 'image/%' OR ContentType LIKE 'audio/%' OR ContentType LIKE 'video/%'"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_assets_Length",
                        "ByteLength > 0 AND ContentRevision > 0"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_assets_StorageKey",
                        "length(StorageKey) = 32"
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.Id, x.HostId });
            _ = b.Property(static x => x.PublicId).HasConversion<string>();
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.ContentType).HasMaxLength(32);
            _ = b.Property(static x => x.StorageKey).HasMaxLength(32);
            _ = b.HasIndex(static x => x.PublicId).IsUnique();
            _ = b.HasIndex(static x => x.StorageKey).IsUnique();
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

        _ = modelBuilder.Entity<OverlayCueMediaAssetReference>(static b =>
        {
            _ = b.ToTable("overlay_cue_media_asset_references");
            _ = b.HasKey(static x => new { x.CueId, x.AssetId });
            _ = b.HasIndex(static x => new { x.HostId, x.AssetId });
            _ = b.HasOne(static x => x.Cue)
                .WithMany()
                .HasForeignKey(static x => new { x.CueId, x.HostId })
                .HasPrincipalKey(static x => new { x.Id, x.HostId })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.Asset)
                .WithMany()
                .HasForeignKey(static x => new { x.AssetId, x.HostId })
                .HasPrincipalKey(static x => new { x.Id, x.HostId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<OverlayEventFeedItem>(static b =>
        {
            _ = b.ToTable(
                "overlay_event_feed_items",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Kind",
                        KindIn("Kind", _overlayEventFeedKinds)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Priority",
                        KindIn("Priority", _overlayEventFeedPriorities)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Lifecycle",
                        KindIn("Lifecycle", _overlayEventFeedLifecycles)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_SourceKey",
                        "length(SourceKey) BETWEEN 1 AND 160"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Text",
                        "length(Title) BETWEEN 1 AND 160 AND length(Body) >= 1"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Duration",
                        "DurationSeconds BETWEEN 1 AND 30"
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayEventFeedKind>.Format(value),
                    static value => PersistedEnumTokens<OverlayEventFeedKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.Priority)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayEventFeedPriority>.Format(value),
                    static value => PersistedEnumTokens<OverlayEventFeedPriority>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.Lifecycle)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayEventFeedLifecycle>.Format(value),
                    static value => PersistedEnumTokens<OverlayEventFeedLifecycle>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.SourceKey).HasMaxLength(160);
            _ = b.Property(static x => x.Title).HasMaxLength(160);
            _ = b.HasIndex(static x => new
                {
                    x.OverlayInstanceId,
                    x.Kind,
                    x.SourceKey,
                })
                .IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.OverlayInstanceId,
                x.Lifecycle,
                x.EnqueuedAtUtc,
            });
            _ = b.HasOne(static x => x.OverlayInstance)
                .WithMany()
                .HasForeignKey(static x => x.OverlayInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

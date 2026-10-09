using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureOverlayEventFeed(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<FullOverlayEventFeedBinding>(binding =>
        {
            _ = binding.ToTable("full_overlay_event_feed_bindings");
            _ = binding.HasKey(value => value.Id);
            _ = binding.HasIndex(value => new { value.FullOverlayId, value.BindingId }).IsUnique();
            _ = binding
                .HasOne(value => value.FullOverlay)
                .WithMany()
                .HasForeignKey(value => value.FullOverlayId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = binding
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<OverlayEventFeedItem>(b =>
        {
            _ = b.ToTable(
                "overlay_event_feed_items",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Owner",
                        ProviderSql(
                            modelBuilder,
                            "(OverlayInstanceId IS NULL) <> (FullOverlayEventFeedBindingId IS NULL)",
                            "(\"OverlayInstanceId\" IS NULL) <> (\"FullOverlayEventFeedBindingId\" IS NULL)"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Kind",
                        KindIn(modelBuilder, "Kind", _overlayEventFeedKinds)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Priority",
                        KindIn(modelBuilder, "Priority", _overlayEventFeedPriorities)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Lifecycle",
                        KindIn(modelBuilder, "Lifecycle", _overlayEventFeedLifecycles)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_SourceKey",
                        ProviderSql(
                            modelBuilder,
                            "length(SourceKey) BETWEEN 1 AND 160",
                            "length(\"SourceKey\") BETWEEN 1 AND 160"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Text",
                        ProviderSql(
                            modelBuilder,
                            "length(Title) BETWEEN 1 AND 160 AND length(Body) >= 1",
                            "length(\"Title\") BETWEEN 1 AND 160 AND length(\"Body\") >= 1"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_event_feed_items_Duration",
                        ProviderSql(
                            modelBuilder,
                            "DurationSeconds BETWEEN 1 AND 30",
                            "\"DurationSeconds\" BETWEEN 1 AND 30"
                        )
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
            _ = b.HasIndex(x => new
                {
                    x.FullOverlayEventFeedBindingId,
                    x.Kind,
                    x.SourceKey,
                })
                .IsUnique();
            _ = b.HasIndex(x => new
            {
                x.FullOverlayEventFeedBindingId,
                x.Lifecycle,
                x.EnqueuedAtUtc,
            });
            _ = b.HasOne(x => x.FullOverlayEventFeedBinding)
                .WithMany()
                .HasForeignKey(x => x.FullOverlayEventFeedBindingId)
                .OnDelete(DeleteBehavior.Cascade);
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

using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureOverlayMedia(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<OverlayMediaAsset>(b =>
        {
            _ = b.ToTable(
                "overlay_media_assets",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_assets_Name",
                        ProviderSql(
                            modelBuilder,
                            "length(Name) BETWEEN 1 AND 128",
                            "length(\"Name\") BETWEEN 1 AND 128"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_assets_Length",
                        ProviderSql(modelBuilder, "ContentRevision > 0", "\"ContentRevision\" > 0")
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.Id, x.HostId });
            _ = b.Property(static x => x.PublicId).HasConversion<string>();
            _ = b.Property(static x => x.DocumentId).HasConversion<string>();
            _ = b.Property(static x => x.Name).HasMaxLength(128);
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
            _ = b.HasOne(static x => x.Document)
                .WithMany(static x => x.References)
                .HasForeignKey(static x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<OverlayMediaDocument>(b =>
        {
            _ = b.ToTable(
                "overlay_media_documents",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_documents_ContentType",
                        ProviderSql(
                            modelBuilder,
                            "ContentType LIKE 'image/%' OR ContentType LIKE 'audio/%' OR ContentType LIKE 'video/%'",
                            "\"ContentType\" LIKE 'image/%' OR \"ContentType\" LIKE 'audio/%' OR \"ContentType\" LIKE 'video/%'"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_documents_Length",
                        ProviderSql(modelBuilder, "ByteLength > 0", "\"ByteLength\" > 0")
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_documents_StorageKey",
                        ProviderSql(
                            modelBuilder,
                            "length(StorageKey) = 32",
                            "length(\"StorageKey\") = 32"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_documents_State",
                        KindIn(modelBuilder, "State", _overlayMediaDocumentStates)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_overlay_media_documents_Legacy",
                        ProviderSql(
                            modelBuilder,
                            "(LegacyHostId IS NULL AND LegacyStorageKey IS NULL) OR (LegacyHostId IS NOT NULL AND length(LegacyStorageKey) = 32)",
                            "(\"LegacyHostId\" IS NULL AND \"LegacyStorageKey\" IS NULL) OR (\"LegacyHostId\" IS NOT NULL AND length(\"LegacyStorageKey\") = 32)"
                        )
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Id).HasConversion<string>();
            _ = b.Property(static x => x.ContentType).HasMaxLength(32);
            _ = b.Property(static x => x.StorageKey).HasMaxLength(32);
            _ = b.Property(static x => x.State)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayMediaDocumentState>.Format(value),
                    static value => PersistedEnumTokens<OverlayMediaDocumentState>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.LegacyStorageKey).HasMaxLength(32);
            _ = b.HasIndex(static x => x.StorageKey).IsUnique();
            _ = b.HasIndex(static x => new { x.State, x.UpdatedAtUtc });
        });

        _ = modelBuilder.Entity<OverlayCueMediaAssetReference>(b =>
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
    }
}

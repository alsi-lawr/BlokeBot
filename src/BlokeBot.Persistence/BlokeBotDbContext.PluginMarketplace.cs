using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<PluginMarketplaceReceiptRecord> PluginMarketplaceReceipts =>
        Set<PluginMarketplaceReceiptRecord>();

    public DbSet<PluginMarketplaceCatalogStateRecord> PluginMarketplaceCatalogState =>
        Set<PluginMarketplaceCatalogStateRecord>();

    public DbSet<PluginMarketplaceCatalogEntryRecord> PluginMarketplaceCatalogEntries =>
        Set<PluginMarketplaceCatalogEntryRecord>();

    private static void ConfigurePluginMarketplace(ModelBuilder modelBuilder)
    {
        ConfigureMarketplaceReceipts(modelBuilder);
        ConfigureMarketplaceCatalogState(modelBuilder);
        ConfigureMarketplaceCatalogEntries(modelBuilder);
        ConfigureCatalogValues<PluginMarketplaceCatalogTagRecord>(
            modelBuilder,
            "plugin_marketplace_catalog_tags",
            "Value",
            40
        );
        ConfigureCatalogValues<PluginMarketplaceCatalogMediaRecord>(
            modelBuilder,
            "plugin_marketplace_catalog_media",
            "Url",
            2_048
        );
        ConfigureCatalogValues<PluginMarketplaceCatalogTargetRecord>(
            modelBuilder,
            "plugin_marketplace_catalog_targets",
            "Value",
            16
        );
    }

    private static void ConfigureMarketplaceReceipts(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PluginMarketplaceReceiptRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_marketplace_receipts",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_marketplace_receipts_Operation",
                        "\"Operation\" IN ('Install', 'Update', 'Remove', 'Restart')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_marketplace_receipts_Release",
                        "(\"DeclaredVersion\" IS NULL AND \"MutableTag\" IS NULL) OR "
                            + "(\"DeclaredVersion\" IS NOT NULL AND \"MutableTag\" IS NOT NULL)"
                    );
                }
            );
            _ = entity.HasKey(value => value.PluginId);
            _ = entity.Property(value => value.PluginId).HasMaxLength(128);
            _ = entity.Property(value => value.Operation).HasConversion<string>().HasMaxLength(16);
            _ = entity.Property(value => value.DeclaredVersion).HasMaxLength(128);
            _ = entity.Property(value => value.MutableTag).HasMaxLength(128);
            _ = entity.Property(value => value.OutcomeCode).HasMaxLength(64);
            _ = entity.Property(value => value.SafeDetail).HasMaxLength(1_000);
        });

    private static void ConfigureMarketplaceCatalogState(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PluginMarketplaceCatalogStateRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_marketplace_catalog_state",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_marketplace_catalog_state_Id",
                        "\"Id\" = 1"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_marketplace_catalog_state_Success",
                        "(\"SchemaVersion\" IS NULL AND \"FetchedAtUtc\" IS NULL AND "
                            + "\"SourceETag\" IS NULL AND \"SourceModifiedAtUtc\" IS NULL) OR "
                            + "(\"SchemaVersion\" = 1 AND \"FetchedAtUtc\" IS NOT NULL)"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_marketplace_catalog_state_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN "
                            + "('DownloadFailed', 'RepositoryInvalid', "
                            + "'InvalidManifest', 'DuplicatePlugin')"
                    );
                }
            );
            _ = entity.HasKey(value => value.Id);
            _ = entity.Property(value => value.Id).ValueGeneratedNever();
            _ = entity.Property(value => value.SourceETag).HasMaxLength(1_024);
            _ = entity
                .Property(value => value.FailureCode)
                .HasConversion<string>()
                .HasMaxLength(32);
        });

    private static void ConfigureMarketplaceCatalogEntries(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PluginMarketplaceCatalogEntryRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_marketplace_catalog_entries",
                table =>
                    table.HasCheckConstraint(
                        "CK_plugin_marketplace_catalog_entries_SnapshotId",
                        "\"SnapshotId\" = 1"
                    )
            );
            _ = entity.HasKey(value => new
            {
                value.PluginId,
                value.DeclaredVersion,
                value.MutableTag,
            });
            _ = entity.Property(value => value.PluginId).HasMaxLength(100);
            _ = entity.Property(value => value.DeclaredVersion).HasMaxLength(128);
            _ = entity.Property(value => value.MutableTag).HasMaxLength(128);
            _ = entity.Property(value => value.Name).HasMaxLength(120);
            _ = entity.Property(value => value.Summary).HasMaxLength(1_000);
            _ = entity.Property(value => value.Author).HasMaxLength(100);
            _ = entity.Property(value => value.IconUrl).HasMaxLength(2_048);
            _ = entity.Property(value => value.RepositoryUrl).HasMaxLength(2_048);
            _ = entity.Property(value => value.PackagePath).HasMaxLength(240);
            _ = entity.Property(value => value.CompatibilityBlokeBot).HasMaxLength(100);
            _ = entity.Property(value => value.CompatibilityPluginApi).HasMaxLength(100);
            _ = entity.Property(value => value.CompatibilityLua).HasMaxLength(8);
            _ = entity
                .HasOne<PluginMarketplaceCatalogStateRecord>()
                .WithMany()
                .HasForeignKey(value => value.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureCatalogValues<TRecord>(
        ModelBuilder modelBuilder,
        string tableName,
        string valueProperty,
        int maximumLength
    )
        where TRecord : class =>
        _ = modelBuilder.Entity<TRecord>(entity =>
        {
            _ = entity.ToTable(tableName);
            _ = entity.HasKey("PluginId", "DeclaredVersion", "MutableTag", "Position");
            _ = entity.Property<string>("PluginId").HasMaxLength(100);
            _ = entity.Property<string>("DeclaredVersion").HasMaxLength(128);
            _ = entity.Property<string>("MutableTag").HasMaxLength(128);
            _ = entity.Property<string>(valueProperty).HasMaxLength(maximumLength);
            _ = entity
                .HasOne<PluginMarketplaceCatalogEntryRecord>()
                .WithMany()
                .HasForeignKey("PluginId", "DeclaredVersion", "MutableTag")
                .OnDelete(DeleteBehavior.Cascade);
        });
}

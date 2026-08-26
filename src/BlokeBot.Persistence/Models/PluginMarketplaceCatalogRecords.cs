using BlokeBot.Plugins.Features;

namespace BlokeBot.Persistence.Models;

public sealed class PluginMarketplaceCatalogStateRecord
{
    public int Id { get; set; }
    public int? SchemaVersion { get; set; }
    public DateTime? FetchedAtUtc { get; set; }
    public DateTime LastAttemptAtUtc { get; set; }
    public string? SourceETag { get; set; }
    public DateTime? SourceModifiedAtUtc { get; set; }
    public PluginMarketplaceRefreshFailureCode? FailureCode { get; set; }
}

public sealed class PluginMarketplaceCatalogEntryRecord
{
    public int SnapshotId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string DeclaredVersion { get; set; } = string.Empty;
    public string MutableTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string RepositoryUrl { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string CompatibilityBlokeBot { get; set; } = string.Empty;
    public string CompatibilityPluginApi { get; set; } = string.Empty;
    public string CompatibilityLua { get; set; } = string.Empty;
}

public sealed class PluginMarketplaceCatalogTagRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string DeclaredVersion { get; set; } = string.Empty;
    public string MutableTag { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class PluginMarketplaceCatalogMediaRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string DeclaredVersion { get; set; } = string.Empty;
    public string MutableTag { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Url { get; set; } = string.Empty;
}

public sealed class PluginMarketplaceCatalogTargetRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string DeclaredVersion { get; set; } = string.Empty;
    public string MutableTag { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Value { get; set; } = string.Empty;
}

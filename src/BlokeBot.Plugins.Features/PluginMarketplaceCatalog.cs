using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

internal static class PluginMarketplaceRepositoryAuthority
{
    internal static Uri RepositoryUrl { get; } =
        new("https://github.com/alsi-lawr/blokebot-plugins");

    internal static string PackagePath(PluginId pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        return $"plugins/{pluginId.Value}";
    }
}

public sealed record PluginMarketplaceCatalogEntry(
    PluginId PluginId,
    string Name,
    string Summary,
    string Author,
    ImmutableArray<string> Tags,
    Uri? IconUrl,
    ImmutableArray<Uri> MediaUrls,
    Uri RepositoryUrl,
    string PackagePath,
    PluginReleaseIdentity Release,
    PluginCompatibilityDeclaration Compatibility
);

public sealed record PluginMarketplaceCatalogSnapshot(
    int SchemaVersion,
    DateTimeOffset RefreshedAt,
    ImmutableArray<PluginMarketplaceCatalogEntry> Entries
);

public sealed record PluginMarketplaceCatalogState(
    PluginMarketplaceCatalogSnapshot? LastValid,
    DateTimeOffset? LastAttemptAt,
    PluginMarketplaceRefreshFailureCode? RefreshFailure,
    string? SourceETag,
    DateTimeOffset? SourceModifiedAt
)
{
    public TimeSpan? Age(TimeProvider timeProvider) =>
        LastValid is null ? null : timeProvider.GetUtcNow() - LastValid.RefreshedAt;
}

public enum PluginMarketplaceRefreshFailureCode
{
    DownloadFailed,
    RepositoryInvalid,
    InvalidManifest,
    DuplicatePlugin,
}

public interface IPluginMarketplaceCatalogStore
{
    ValueTask<PluginMarketplaceCatalogState> LoadAsync(CancellationToken cancellationToken);

    ValueTask<PluginMarketplaceCatalogState> ReplaceAsync(
        PluginMarketplaceCatalogSnapshot snapshot,
        DateTimeOffset attemptedAt,
        string? sourceETag,
        DateTimeOffset? sourceModifiedAt,
        CancellationToken cancellationToken
    );

    ValueTask<PluginMarketplaceCatalogState> RecordNotModifiedAsync(
        DateTimeOffset attemptedAt,
        string? sourceETag,
        DateTimeOffset? sourceModifiedAt,
        CancellationToken cancellationToken
    );

    ValueTask<PluginMarketplaceCatalogState> RecordFailureAsync(
        DateTimeOffset attemptedAt,
        PluginMarketplaceRefreshFailureCode failure,
        CancellationToken cancellationToken
    );
}

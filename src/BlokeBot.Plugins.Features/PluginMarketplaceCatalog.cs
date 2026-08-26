using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed record PluginMarketplaceCompatibility(
    string BlokeBot,
    string PluginApi,
    string Lua,
    ImmutableArray<string> Targets
);

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
    PluginMarketplaceCompatibility Compatibility
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

public enum PluginMarketplaceCatalogFailureCode
{
    MalformedJson,
    UnsupportedSchema,
    InvalidEntry,
    DuplicateRelease,
}

public enum PluginMarketplaceRefreshFailureCode
{
    DownloadFailed,
    MalformedCatalog,
    UnsupportedSchema,
    InvalidEntry,
    DuplicateRelease,
}

public sealed record PluginMarketplaceCatalogFailure(
    PluginMarketplaceCatalogFailureCode Code,
    string Location
);

public abstract record PluginMarketplaceCatalogValidationOutcome
{
    private PluginMarketplaceCatalogValidationOutcome() { }

    public sealed record Accepted(ImmutableArray<PluginMarketplaceCatalogEntry> Entries)
        : PluginMarketplaceCatalogValidationOutcome;

    public sealed record Rejected(PluginMarketplaceCatalogFailure Failure)
        : PluginMarketplaceCatalogValidationOutcome;
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

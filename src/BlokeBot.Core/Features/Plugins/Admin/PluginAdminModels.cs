using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

public enum PluginAdminInstalledStatus
{
    Active,
    Degraded,
    Faulted,
    Operation,
}

public sealed record PluginAdminFeatureItem(
    PluginFeatureId Id,
    string Name,
    int EnabledChannelCount
);

public sealed record PluginAdminInstalledPlugin(
    PluginId PluginId,
    string Name,
    PluginLifecycleView Lifecycle,
    PluginAdminInstalledStatus Status,
    int EnabledChannelCount,
    ImmutableArray<PluginAdminFeatureItem> Features,
    PluginReleaseIdentity? UpdateRelease,
    PluginMarketplaceReceipt? LatestReceipt
)
{
    public bool OperationInProgress =>
        Lifecycle.Phase
            is PluginLifecyclePhase.Preparing
                or PluginLifecyclePhase.Migrating
                or PluginLifecyclePhase.Activating
                or PluginLifecyclePhase.Draining
                or PluginLifecyclePhase.Removing;
}

public sealed record PluginAdminCatalogEntry(
    PluginMarketplaceCatalogEntry Entry,
    bool Compatible,
    bool Installed,
    bool SelectedRelease
);

public abstract record PluginAdminCatalog
{
    private PluginAdminCatalog() { }

    public bool RefreshInProgress { get; init; }

    public sealed record Available(
        ImmutableArray<PluginAdminCatalogEntry> Entries,
        DateTimeOffset RefreshedAt,
        TimeSpan Age,
        PluginMarketplaceRefreshFailureCode? RefreshFailure
    ) : PluginAdminCatalog;

    public sealed record Unavailable(
        DateTimeOffset? LastAttemptAt,
        PluginMarketplaceRefreshFailureCode? RefreshFailure
    ) : PluginAdminCatalog;
}

public sealed record PluginAdminSnapshot(
    ImmutableArray<PluginAdminInstalledPlugin> Installed,
    PluginAdminCatalog Catalog
);

public abstract record PluginAdminLoadOutcome
{
    private PluginAdminLoadOutcome() { }

    public sealed record Loaded(PluginAdminSnapshot Snapshot) : PluginAdminLoadOutcome;

    public sealed record Unauthorized : PluginAdminLoadOutcome;
}

public interface IPluginAdminApplicationService
{
    ValueTask<PluginAdminLoadOutcome> LoadAsync(
        AuthenticatedSession session,
        string? catalogQuery,
        CancellationToken cancellationToken
    );

    ValueTask<PluginMarketplaceCommandOutcome> InstallAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    );

    ValueTask<PluginMarketplaceCommandOutcome> UpdateAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    );

    ValueTask<PluginMarketplaceCommandOutcome> RestartAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    );

    ValueTask<PluginMarketplaceCommandOutcome> RemoveAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    );
}

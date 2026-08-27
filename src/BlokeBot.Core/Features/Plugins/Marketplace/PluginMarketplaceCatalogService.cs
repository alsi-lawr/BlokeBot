using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public abstract record PluginMarketplaceSearchOutcome
{
    private PluginMarketplaceSearchOutcome() { }

    public bool RefreshInProgress { get; init; }

    public sealed record Available(
        ImmutableArray<PluginMarketplaceCatalogEntry> Entries,
        DateTimeOffset RefreshedAt,
        TimeSpan Age,
        PluginMarketplaceRefreshFailureCode? RefreshFailure
    ) : PluginMarketplaceSearchOutcome;

    public sealed record Unavailable(
        DateTimeOffset? LastAttemptAt,
        PluginMarketplaceRefreshFailureCode? RefreshFailure
    ) : PluginMarketplaceSearchOutcome;

    public sealed record Unauthorized : PluginMarketplaceSearchOutcome;
}

public sealed class PluginMarketplaceCatalogService
{
    private readonly PluginMarketplaceCatalogRegistry _registry;
    private readonly TimeProvider _timeProvider;

    internal PluginMarketplaceCatalogService(
        PluginMarketplaceCatalogRegistry registry,
        TimeProvider timeProvider
    )
    {
        _registry = registry;
        _timeProvider = timeProvider;
    }

    public PluginMarketplaceSearchOutcome Search(AuthenticatedSession session, string? query)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsBotAdmin)
        {
            return new PluginMarketplaceSearchOutcome.Unauthorized
            {
                RefreshInProgress = _registry.RefreshInProgress,
            };
        }

        var state = _registry.Current;
        if (state.LastValid is not { } snapshot)
        {
            return new PluginMarketplaceSearchOutcome.Unavailable(
                state.LastAttemptAt,
                state.RefreshFailure
            )
            {
                RefreshInProgress = _registry.RefreshInProgress,
            };
        }

        var normalized = query?.Trim();
        var entries = snapshot
            .Entries.Where(entry => Matches(entry, normalized))
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.PluginId.Value, StringComparer.Ordinal)
            .ThenByDescending(static entry => entry.Release.DeclaredVersion)
            .ToImmutableArray();
        return new PluginMarketplaceSearchOutcome.Available(
            entries,
            snapshot.RefreshedAt,
            state.Age(_timeProvider)!.Value,
            state.RefreshFailure
        )
        {
            RefreshInProgress = _registry.RefreshInProgress,
        };
    }

    internal PluginMarketplaceCatalogEntry? Find(
        PluginId pluginId,
        PluginReleaseIdentity release
    ) =>
        _registry.Current.LastValid?.Entries.SingleOrDefault(entry =>
            entry.PluginId == pluginId && entry.Release == release
        );

    private static bool Matches(PluginMarketplaceCatalogEntry entry, string? query) =>
        string.IsNullOrEmpty(query)
        || Contains(entry.PluginId.Value, query)
        || Contains(entry.Name, query)
        || Contains(entry.Summary, query)
        || Contains(entry.Author, query)
        || entry.Tags.Any(tag => Contains(tag, query));

    private static bool Contains(string candidate, string query) =>
        candidate.Contains(query, StringComparison.OrdinalIgnoreCase);
}

internal sealed class PluginMarketplaceCatalogRegistry(
    IPluginMarketplaceCatalogStore store,
    IPluginMarketplaceRepositoryTransport transport,
    TimeProvider timeProvider
) : IDisposable
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private PluginMarketplaceCatalogState _current = new(null, null, null, null, null);
    private int _refreshInProgress;

    internal PluginMarketplaceCatalogState Current => Volatile.Read(ref _current);

    internal bool RefreshInProgress => Volatile.Read(ref _refreshInProgress) != 0;

    internal async ValueTask InitializeAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref _current, await store.LoadAsync(cancellationToken));

    internal async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            _ = Interlocked.Exchange(ref _refreshInProgress, 1);
            var now = timeProvider.GetUtcNow();
            var download = await transport.DownloadAsync(
                Current.SourceETag,
                Current.SourceModifiedAt,
                cancellationToken
            );
            PluginMarketplaceCatalogState next;
            if (download is PluginMarketplaceRepositoryDownload.NotModified notModified)
            {
                next = await store.RecordNotModifiedAsync(
                    now,
                    notModified.SourceETag,
                    notModified.SourceModifiedAt,
                    cancellationToken
                );
            }
            else if (download is PluginMarketplaceRepositoryDownload.Delivered delivered)
            {
                var discovery = PluginMarketplaceRepositoryDiscovery.Validate(delivered.Repository);
                if (discovery is PluginMarketplaceRepositoryDiscoveryOutcome.Accepted accepted)
                {
                    next = await store.ReplaceAsync(
                        new(1, now, accepted.Entries),
                        now,
                        delivered.SourceETag,
                        delivered.SourceModifiedAt,
                        cancellationToken
                    );
                }
                else
                {
                    var rejected = (PluginMarketplaceRepositoryDiscoveryOutcome.Rejected)discovery;
                    next = await store.RecordFailureAsync(
                        now,
                        Map(rejected.Code),
                        cancellationToken
                    );
                }
            }
            else
            {
                next = await store.RecordFailureAsync(
                    now,
                    PluginMarketplaceRefreshFailureCode.DownloadFailed,
                    cancellationToken
                );
            }

            Volatile.Write(ref _current, next);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _refreshInProgress, 0);
            _ = _refresh.Release();
        }
    }

    public void Dispose() => _refresh.Dispose();

    private static PluginMarketplaceRefreshFailureCode Map(
        PluginMarketplaceRepositoryFailureCode code
    ) =>
        code switch
        {
            PluginMarketplaceRepositoryFailureCode.InvalidLayout =>
                PluginMarketplaceRefreshFailureCode.RepositoryInvalid,
            PluginMarketplaceRepositoryFailureCode.InvalidManifest =>
                PluginMarketplaceRefreshFailureCode.InvalidManifest,
            PluginMarketplaceRepositoryFailureCode.DuplicatePlugin =>
                PluginMarketplaceRefreshFailureCode.DuplicatePlugin,
        };
}

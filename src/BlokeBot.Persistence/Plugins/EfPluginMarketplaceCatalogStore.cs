using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginMarketplaceCatalogStore(
    IDbContextFactory<BlokeBotDbContext> contextFactory
) : IPluginMarketplaceCatalogStore
{
    private const int _stateId = 1;

    public async ValueTask<PluginMarketplaceCatalogState> LoadAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var state = await context
            .PluginMarketplaceCatalogState.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == _stateId, cancellationToken);
        return state is null
            ? new(null, null, null, null, null)
            : await MapAsync(context, state, cancellationToken);
    }

    public async ValueTask<PluginMarketplaceCatalogState> ReplaceAsync(
        PluginMarketplaceCatalogSnapshot snapshot,
        DateTimeOffset attemptedAt,
        string? sourceETag,
        DateTimeOffset? sourceModifiedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1)
        {
            throw new ArgumentException(
                "Unsupported marketplace snapshot schema version.",
                nameof(snapshot)
            );
        }

        if (
            snapshot.Entries.IsDefault
            || snapshot.Entries.Any(entry =>
                entry.RepositoryUrl != PluginMarketplaceRepositoryAuthority.RepositoryUrl
                || entry.PackagePath
                    != PluginMarketplaceRepositoryAuthority.PackagePath(entry.PluginId)
            )
        )
        {
            throw new ArgumentException(
                "A marketplace snapshot entry is outside the curated repository.",
                nameof(snapshot)
            );
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );
        _ = await context.PluginMarketplaceCatalogEntries.ExecuteDeleteAsync(cancellationToken);
        var state = await context.PluginMarketplaceCatalogState.SingleOrDefaultAsync(
            value => value.Id == _stateId,
            cancellationToken
        );
        if (state is null)
        {
            state = new PluginMarketplaceCatalogStateRecord { Id = _stateId };
            _ = context.PluginMarketplaceCatalogState.Add(state);
        }

        state.SchemaVersion = snapshot.SchemaVersion;
        state.FetchedAtUtc = snapshot.RefreshedAt.UtcDateTime;
        state.LastAttemptAtUtc = attemptedAt.UtcDateTime;
        state.SourceETag = sourceETag;
        state.SourceModifiedAtUtc = sourceModifiedAt?.UtcDateTime;
        state.FailureCode = null;
        foreach (var entry in snapshot.Entries)
        {
            Add(context, entry);
        }

        _ = await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(snapshot, attemptedAt, null, sourceETag, sourceModifiedAt);
    }

    public async ValueTask<PluginMarketplaceCatalogState> RecordNotModifiedAsync(
        DateTimeOffset attemptedAt,
        string? sourceETag,
        DateTimeOffset? sourceModifiedAt,
        CancellationToken cancellationToken
    )
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );
        var state = await context.PluginMarketplaceCatalogState.SingleOrDefaultAsync(
            value => value.Id == _stateId,
            cancellationToken
        );
        if (state?.SchemaVersion is null || state.FetchedAtUtc is null)
        {
            throw new InvalidOperationException(
                "A marketplace catalog cannot be unchanged before a successful refresh."
            );
        }

        state.LastAttemptAtUtc = attemptedAt.UtcDateTime;
        state.SourceETag = sourceETag ?? state.SourceETag;
        state.SourceModifiedAtUtc = sourceModifiedAt?.UtcDateTime ?? state.SourceModifiedAtUtc;
        state.FailureCode = null;
        _ = await context.SaveChangesAsync(cancellationToken);
        var mapped = await MapAsync(context, state, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return mapped;
    }

    public async ValueTask<PluginMarketplaceCatalogState> RecordFailureAsync(
        DateTimeOffset attemptedAt,
        PluginMarketplaceRefreshFailureCode failure,
        CancellationToken cancellationToken
    )
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );
        var state = await context.PluginMarketplaceCatalogState.SingleOrDefaultAsync(
            value => value.Id == _stateId,
            cancellationToken
        );
        if (state is null)
        {
            state = new PluginMarketplaceCatalogStateRecord { Id = _stateId };
            _ = context.PluginMarketplaceCatalogState.Add(state);
        }

        state.LastAttemptAtUtc = attemptedAt.UtcDateTime;
        state.FailureCode = failure;
        _ = await context.SaveChangesAsync(cancellationToken);
        var mapped = await MapAsync(context, state, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return mapped;
    }

    private static void Add(BlokeBotDbContext context, PluginMarketplaceCatalogEntry entry)
    {
        var version = entry.Release.DeclaredVersion.Value;
        var tag = entry.Release.Tag.Value;
        _ = context.PluginMarketplaceCatalogEntries.Add(
            new()
            {
                SnapshotId = _stateId,
                PluginId = entry.PluginId.Value,
                DeclaredVersion = version,
                MutableTag = tag,
                Name = entry.Name,
                Summary = entry.Summary,
                Author = entry.Author,
                IconUrl = entry.IconUrl?.AbsoluteUri,
                RepositoryUrl = entry.RepositoryUrl.AbsoluteUri.TrimEnd('/'),
                PackagePath = entry.PackagePath,
                CompatibilityBlokeBot = BlokeBotRange(entry.Compatibility),
                CompatibilityPluginApi = ApiRange(entry.Compatibility),
                CompatibilityLua = "5.4",
            }
        );
        context.AddRange(
            entry.Tags.Select(
                (value, position) =>
                    new PluginMarketplaceCatalogTagRecord
                    {
                        PluginId = entry.PluginId.Value,
                        DeclaredVersion = version,
                        MutableTag = tag,
                        Position = position,
                        Value = value,
                    }
            )
        );
        context.AddRange(
            entry.MediaUrls.Select(
                (value, position) =>
                    new PluginMarketplaceCatalogMediaRecord
                    {
                        PluginId = entry.PluginId.Value,
                        DeclaredVersion = version,
                        MutableTag = tag,
                        Position = position,
                        Url = value.AbsoluteUri,
                    }
            )
        );
        context.AddRange(
            entry.Compatibility.SupportedTargets.Select(
                (value, position) =>
                    new PluginMarketplaceCatalogTargetRecord
                    {
                        PluginId = entry.PluginId.Value,
                        DeclaredVersion = version,
                        MutableTag = tag,
                        Position = position,
                        Value = RuntimeIdentifier(value),
                    }
            )
        );
    }
}

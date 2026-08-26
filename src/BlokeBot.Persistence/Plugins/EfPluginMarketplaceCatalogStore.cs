using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed class EfPluginMarketplaceCatalogStore(
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
                "Unsupported marketplace schema version.",
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
                CompatibilityBlokeBot = entry.Compatibility.BlokeBot,
                CompatibilityPluginApi = entry.Compatibility.PluginApi,
                CompatibilityLua = entry.Compatibility.Lua,
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
            entry.Compatibility.Targets.Select(
                (value, position) =>
                    new PluginMarketplaceCatalogTargetRecord
                    {
                        PluginId = entry.PluginId.Value,
                        DeclaredVersion = version,
                        MutableTag = tag,
                        Position = position,
                        Value = value,
                    }
            )
        );
    }

    private static async ValueTask<PluginMarketplaceCatalogState> MapAsync(
        BlokeBotDbContext context,
        PluginMarketplaceCatalogStateRecord state,
        CancellationToken cancellationToken
    )
    {
        var attemptedAt = Utc(state.LastAttemptAtUtc);
        if (state.SchemaVersion is null || state.FetchedAtUtc is null)
        {
            var invalid =
                state.SchemaVersion is not null
                || state.FetchedAtUtc is not null
                || await context.PluginMarketplaceCatalogEntries.AnyAsync(cancellationToken);
            return invalid
                ? throw InvalidData()
                : new(
                    null,
                    attemptedAt,
                    state.FailureCode,
                    state.SourceETag,
                    Utc(state.SourceModifiedAtUtc)
                );
        }

        if (state.SchemaVersion != 1)
        {
            throw InvalidData();
        }

        var entryRecords = await context
            .PluginMarketplaceCatalogEntries.AsNoTracking()
            .OrderBy(value => value.PluginId)
            .ThenBy(value => value.DeclaredVersion)
            .ThenBy(value => value.MutableTag)
            .ToArrayAsync(cancellationToken);
        var tags = await context
            .Set<PluginMarketplaceCatalogTagRecord>()
            .AsNoTracking()
            .OrderBy(value => value.Position)
            .ToArrayAsync(cancellationToken);
        var media = await context
            .Set<PluginMarketplaceCatalogMediaRecord>()
            .AsNoTracking()
            .OrderBy(value => value.Position)
            .ToArrayAsync(cancellationToken);
        var targets = await context
            .Set<PluginMarketplaceCatalogTargetRecord>()
            .AsNoTracking()
            .OrderBy(value => value.Position)
            .ToArrayAsync(cancellationToken);
        var entries = entryRecords
            .Select(entry => Map(entry, tags, media, targets))
            .ToImmutableArray();
        return new(
            new(state.SchemaVersion.Value, Utc(state.FetchedAtUtc.Value), entries),
            attemptedAt,
            state.FailureCode,
            state.SourceETag,
            Utc(state.SourceModifiedAtUtc)
        );
    }

    private static PluginMarketplaceCatalogEntry Map(
        PluginMarketplaceCatalogEntryRecord record,
        IReadOnlyList<PluginMarketplaceCatalogTagRecord> tags,
        IReadOnlyList<PluginMarketplaceCatalogMediaRecord> media,
        IReadOnlyList<PluginMarketplaceCatalogTargetRecord> targets
    )
    {
        if (
            !PluginId.TryCreate(record.PluginId, out var pluginId)
            || !SemanticVersion.TryCreate(record.DeclaredVersion, out var version)
            || !PluginGitTag.TryCreate(record.MutableTag, out var tag)
            || !TryHttps(record.IconUrl, optional: true, out var iconUrl)
            || !TryHttps(record.RepositoryUrl, optional: false, out var repositoryUrl)
        )
        {
            throw InvalidData();
        }

        var key = (record.PluginId, record.DeclaredVersion, record.MutableTag);
        var entryTags = tags.Where(value =>
                Key(value.PluginId, value.DeclaredVersion, value.MutableTag) == key
            )
            .Select(value => value.Value)
            .ToImmutableArray();
        var entryMedia = media
            .Where(value => Key(value.PluginId, value.DeclaredVersion, value.MutableTag) == key)
            .Select(value =>
                TryHttps(value.Url, optional: false, out var uri) ? uri! : throw InvalidData()
            )
            .ToImmutableArray();
        var entryTargets = targets
            .Where(value => Key(value.PluginId, value.DeclaredVersion, value.MutableTag) == key)
            .Select(value => value.Value)
            .ToImmutableArray();
        return new(
            pluginId,
            record.Name,
            record.Summary,
            record.Author,
            entryTags,
            iconUrl,
            entryMedia,
            repositoryUrl!,
            record.PackagePath,
            new(version, tag),
            new(
                record.CompatibilityBlokeBot,
                record.CompatibilityPluginApi,
                record.CompatibilityLua,
                entryTargets
            )
        );
    }

    private static (string PluginId, string Version, string Tag) Key(
        string pluginId,
        string version,
        string tag
    ) => (pluginId, version, tag);

    private static bool TryHttps(string? value, bool optional, out Uri? uri)
    {
        uri = null;
        return value is null
            ? optional
            : value.StartsWith("https://", StringComparison.Ordinal)
                && Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    private static InvalidOperationException InvalidData() =>
        new("Persisted plugin marketplace catalog data is invalid.");
}

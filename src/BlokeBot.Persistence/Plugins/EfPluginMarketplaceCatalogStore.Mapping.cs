using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginMarketplaceCatalogStore
{
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
            || repositoryUrl != PluginMarketplaceRepositoryAuthority.RepositoryUrl
            || record.PackagePath != PluginMarketplaceRepositoryAuthority.PackagePath(pluginId)
            || !TryBlokeBotRange(
                record.CompatibilityBlokeBot,
                out var minimumBlokeBot,
                out var maximumBlokeBot
            )
            || !TryApiRange(record.CompatibilityPluginApi, out var minimumApi, out var maximumApi)
            || record.CompatibilityLua != "5.4"
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
        var targetValues = targets
            .Where(value => Key(value.PluginId, value.DeclaredVersion, value.MutableTag) == key)
            .Select(value => value.Value)
            .ToImmutableArray();
        var entryTargets = TryRuntimeIdentifiers(targetValues, out var parsedTargets)
            ? parsedTargets
            : throw InvalidData();
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
                minimumApi,
                maximumApi,
                minimumBlokeBot,
                maximumBlokeBot,
                PluginLuaVersion.Lua54,
                entryTargets
            )
        );
    }

    private static string BlokeBotRange(PluginCompatibilityDeclaration compatibility) =>
        $">={compatibility.MinimumBlokeBotVersion.Value} <{compatibility.MaximumBlokeBotVersionExclusive.Value}";

    private static string ApiRange(PluginCompatibilityDeclaration compatibility) =>
        compatibility.MinimumApiVersion == compatibility.MaximumApiVersion
            ? compatibility.MinimumApiVersion.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            )
            : $"{compatibility.MinimumApiVersion.Value}-{compatibility.MaximumApiVersion.Value}";

    private static bool TryBlokeBotRange(
        string value,
        out SemanticVersion minimum,
        out SemanticVersion maximumExclusive
    )
    {
        minimum = null!;
        maximumExclusive = null!;
        var components = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return components.Length == 2
            && components[0].StartsWith(">=", StringComparison.Ordinal)
            && components[1].StartsWith('<')
            && SemanticVersion.TryCreate(components[0][2..], out minimum)
            && SemanticVersion.TryCreate(components[1][1..], out maximumExclusive)
            && minimum.CompareTo(maximumExclusive) < 0;
    }

    private static bool TryApiRange(
        string value,
        out PluginApiVersion minimum,
        out PluginApiVersion maximum
    )
    {
        minimum = null!;
        maximum = null!;
        var components = value.Split('-');
        if (components.Length is < 1 or > 2 || !TryApiVersion(components[0], out minimum))
        {
            return false;
        }

        maximum = minimum;
        return (components.Length == 1 || TryApiVersion(components[1], out maximum))
            && minimum.CompareTo(maximum) <= 0;
    }

    private static bool TryApiVersion(string value, out PluginApiVersion version)
    {
        version = null!;
        return int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed
            ) && PluginApiVersion.TryCreate(parsed, out version);
    }

    private static bool TryRuntimeIdentifiers(
        ImmutableArray<string> values,
        out ImmutableArray<PluginRuntimeIdentifier> identifiers
    )
    {
        var result = ImmutableArray.CreateBuilder<PluginRuntimeIdentifier>(values.Length);
        foreach (var value in values)
        {
            if (!TryRuntimeIdentifier(value, out var identifier) || result.Contains(identifier))
            {
                identifiers = [];
                return false;
            }

            result.Add(identifier);
        }

        identifiers = result.MoveToImmutable();
        return !identifiers.IsEmpty;
    }

    private static bool TryRuntimeIdentifier(string value, out PluginRuntimeIdentifier identifier)
    {
        var candidate = value switch
        {
            "linux-x64" => PluginRuntimeIdentifier.LinuxX64,
            "linux-arm64" => PluginRuntimeIdentifier.LinuxArm64,
            "osx-arm64" => PluginRuntimeIdentifier.MacOsArm64,
            "win-x64" => PluginRuntimeIdentifier.WindowsX64,
            "win-arm64" => PluginRuntimeIdentifier.WindowsArm64,
            _ => (PluginRuntimeIdentifier?)null,
        };
        identifier = candidate.GetValueOrDefault();
        return candidate.HasValue;
    }

    private static string RuntimeIdentifier(PluginRuntimeIdentifier identifier) =>
        identifier switch
        {
            PluginRuntimeIdentifier.LinuxX64 => "linux-x64",
            PluginRuntimeIdentifier.LinuxArm64 => "linux-arm64",
            PluginRuntimeIdentifier.MacOsArm64 => "osx-arm64",
            PluginRuntimeIdentifier.WindowsX64 => "win-x64",
            PluginRuntimeIdentifier.WindowsArm64 => "win-arm64",
        };

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
        new("Persisted plugin marketplace snapshot data is invalid.");
}

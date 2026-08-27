using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal enum PluginMarketplaceRepositoryEntryKind
{
    File,
    Directory,
    Unsupported,
}

internal sealed record PluginMarketplaceRepositoryEntry(
    string Path,
    PluginMarketplaceRepositoryEntryKind Kind,
    ReadOnlyMemory<byte> Content
);

internal sealed record PluginMarketplaceRepositorySnapshot(
    ImmutableArray<PluginMarketplaceRepositoryEntry> Entries
);

internal enum PluginMarketplaceRepositoryFailureCode
{
    InvalidLayout,
    InvalidManifest,
    DuplicatePlugin,
}

internal abstract record PluginMarketplaceRepositoryDiscoveryOutcome
{
    private PluginMarketplaceRepositoryDiscoveryOutcome() { }

    internal sealed record Accepted(ImmutableArray<PluginMarketplaceCatalogEntry> Entries)
        : PluginMarketplaceRepositoryDiscoveryOutcome;

    internal sealed record Rejected(PluginMarketplaceRepositoryFailureCode Code)
        : PluginMarketplaceRepositoryDiscoveryOutcome;
}

internal static class PluginMarketplaceRepositoryDiscovery
{
    internal static PluginMarketplaceRepositoryDiscoveryOutcome Validate(
        PluginMarketplaceRepositorySnapshot snapshot
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Entries.IsDefault)
        {
            return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
        }

        var exactPaths = new HashSet<string>(StringComparer.Ordinal);
        var foldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packageDirectories = new HashSet<string>(StringComparer.Ordinal);
        var manifests = new List<(string Directory, PluginMarketplaceRepositoryEntry Entry)>();
        foreach (var entry in snapshot.Entries)
        {
            if (
                !MarketplacePackagePath.IsCanonical(entry.Path)
                || !exactPaths.Add(entry.Path)
                || !foldedPaths.Add(entry.Path)
            )
            {
                return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
            }

            var segments = entry.Path.Split('/');
            if (!string.Equals(segments[0], "plugins", StringComparison.Ordinal))
            {
                if (
                    string.Equals(
                        segments[^1],
                        PluginPackage.ManifestPath,
                        StringComparison.Ordinal
                    )
                )
                {
                    return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
                }

                continue;
            }

            if (segments.Length == 1)
            {
                if (entry.Kind != PluginMarketplaceRepositoryEntryKind.Directory)
                {
                    return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
                }

                continue;
            }

            if (!PluginId.TryCreate(segments[1], out _))
            {
                return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
            }

            if (segments.Length == 2)
            {
                if (entry.Kind != PluginMarketplaceRepositoryEntryKind.Directory)
                {
                    return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
                }

                _ = packageDirectories.Add(segments[1]);
                continue;
            }

            if (entry.Kind == PluginMarketplaceRepositoryEntryKind.Unsupported)
            {
                return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
            }

            if (!string.Equals(segments[^1], PluginPackage.ManifestPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (segments.Length != 3 || entry.Kind != PluginMarketplaceRepositoryEntryKind.File)
            {
                return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
            }

            manifests.Add((segments[1], entry));
        }

        if (
            !exactPaths.Contains("plugins")
            || packageDirectories.Any(directory =>
                !manifests.Any(manifest =>
                    string.Equals(manifest.Directory, directory, StringComparison.Ordinal)
                )
            )
            || manifests.Any(manifest => !packageDirectories.Contains(manifest.Directory))
        )
        {
            return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout);
        }

        var pluginIds = new HashSet<PluginId>();
        var parsed = new List<(string Directory, PluginManifest Manifest)>(manifests.Count);
        foreach (var (directory, source) in manifests.OrderBy(static value => value.Directory))
        {
            var validation = PluginManifestToml.ValidateForMarketplace(source.Content);
            if (validation is not PluginManifestDeclarationValidationOutcome.Accepted accepted)
            {
                return Rejected(PluginMarketplaceRepositoryFailureCode.InvalidManifest);
            }

            var manifest = accepted.Declaration.Manifest;
            if (!pluginIds.Add(manifest.Id))
            {
                return Rejected(PluginMarketplaceRepositoryFailureCode.DuplicatePlugin);
            }

            parsed.Add((directory, manifest));
        }

        return parsed.Any(value => value.Manifest.Id.Value != value.Directory)
            ? Rejected(PluginMarketplaceRepositoryFailureCode.InvalidLayout)
            : new PluginMarketplaceRepositoryDiscoveryOutcome.Accepted(
                parsed.Select(static value => Project(value.Manifest)).ToImmutableArray()
            );
    }

    private static PluginMarketplaceCatalogEntry Project(PluginManifest manifest) =>
        new(
            manifest.Id,
            manifest.Name,
            manifest.Description,
            manifest.Marketplace.Author,
            manifest.Marketplace.Tags,
            UriOrNull(manifest.Marketplace.IconUrl),
            manifest
                .Marketplace.MediaUrls.Select(static value => new Uri(value, UriKind.Absolute))
                .ToImmutableArray(),
            PluginMarketplaceRepositoryAuthority.RepositoryUrl,
            PluginMarketplaceRepositoryAuthority.PackagePath(manifest.Id),
            manifest.Release,
            manifest.Compatibility
        );

    private static Uri? UriOrNull(string? value) =>
        value is null ? null : new Uri(value, UriKind.Absolute);

    private static PluginMarketplaceRepositoryDiscoveryOutcome Rejected(
        PluginMarketplaceRepositoryFailureCode code
    ) => new PluginMarketplaceRepositoryDiscoveryOutcome.Rejected(code);
}

internal static class PluginMarketplaceCompatibilityPolicy
{
    internal static bool IsCompatible(
        PluginCompatibilityDeclaration compatibility,
        PluginHostCompatibilityTarget target
    ) =>
        target.ApiVersion.CompareTo(compatibility.MinimumApiVersion) >= 0
        && target.ApiVersion.CompareTo(compatibility.MaximumApiVersion) <= 0
        && target.BlokeBotVersion.CompareTo(compatibility.MinimumBlokeBotVersion) >= 0
        && target.BlokeBotVersion.CompareTo(compatibility.MaximumBlokeBotVersionExclusive) < 0
        && compatibility.LuaVersion == PluginLuaVersion.Lua54
        && compatibility.SupportedTargets.Contains(target.RuntimeIdentifier);
}

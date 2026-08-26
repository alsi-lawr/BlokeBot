using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal static class PluginMarketplaceCatalogParser
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        MaxDepth = 16,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static PluginMarketplaceCatalogValidationOutcome Validate(
        ReadOnlyMemory<byte> utf8Json
    )
    {
        CatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(utf8Json.Span, _json);
        }
        catch (JsonException)
        {
            return Rejected(PluginMarketplaceCatalogFailureCode.MalformedJson, "$catalog");
        }

        if (document is null)
        {
            return Rejected(PluginMarketplaceCatalogFailureCode.MalformedJson, "$catalog");
        }

        if (document.SchemaVersion != 1)
        {
            return Rejected(PluginMarketplaceCatalogFailureCode.UnsupportedSchema, "schemaVersion");
        }

        var entries = ImmutableArray.CreateBuilder<PluginMarketplaceCatalogEntry>(
            document.Plugins.Length
        );
        var releases = new HashSet<PluginInstallationIdentity>();
        for (var index = 0; index < document.Plugins.Length; index++)
        {
            var location = $"plugins[{index}]";
            if (!TryValidate(document.Plugins[index], out var entry))
            {
                return Rejected(PluginMarketplaceCatalogFailureCode.InvalidEntry, location);
            }

            if (!releases.Add(new(entry.PluginId, entry.Release)))
            {
                return Rejected(PluginMarketplaceCatalogFailureCode.DuplicateRelease, location);
            }

            entries.Add(entry);
        }

        return new PluginMarketplaceCatalogValidationOutcome.Accepted(entries.MoveToImmutable());
    }

    internal static byte[] Serialize(ImmutableArray<PluginMarketplaceCatalogEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("plugins");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.PluginId.Value);
                writer.WriteString("name", entry.Name);
                writer.WriteString("summary", entry.Summary);
                writer.WriteString("author", entry.Author);
                writer.WriteStartArray("tags");
                foreach (var tag in entry.Tags)
                {
                    writer.WriteStringValue(tag);
                }
                writer.WriteEndArray();
                if (entry.IconUrl is not null)
                {
                    writer.WriteString("iconUrl", entry.IconUrl.AbsoluteUri);
                }
                if (!entry.MediaUrls.IsEmpty)
                {
                    writer.WriteStartArray("mediaUrls");
                    foreach (var mediaUrl in entry.MediaUrls)
                    {
                        writer.WriteStringValue(mediaUrl.AbsoluteUri);
                    }
                    writer.WriteEndArray();
                }
                writer.WriteStartObject("source");
                writer.WriteString("repositoryUrl", entry.RepositoryUrl.AbsoluteUri.TrimEnd('/'));
                writer.WriteString("packagePath", entry.PackagePath);
                writer.WriteEndObject();
                writer.WriteString("version", entry.Release.DeclaredVersion.Value);
                writer.WriteString("tag", entry.Release.Tag.Value);
                writer.WriteStartObject("compatibility");
                writer.WriteString("blokeBot", entry.Compatibility.BlokeBot);
                writer.WriteString("pluginApi", entry.Compatibility.PluginApi);
                writer.WriteString("lua", entry.Compatibility.Lua);
                writer.WriteStartArray("targets");
                foreach (var target in entry.Compatibility.Targets)
                {
                    writer.WriteStringValue(target);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool TryValidate(
        CatalogEntryDocument candidate,
        out PluginMarketplaceCatalogEntry entry
    )
    {
        entry = null!;
        if (
            candidate.Id.Length > 100
            || !PluginId.TryCreate(candidate.Id, out var pluginId)
            || !Length(candidate.Name, 1, 100)
            || !Length(candidate.Summary, 1, 300)
            || !Length(candidate.Author, 1, 100)
            || candidate.Tags.Length > 20
            || candidate.Tags.Any(tag => !Length(tag, 1, 40))
            || candidate.Tags.Distinct(StringComparer.Ordinal).Count() != candidate.Tags.Length
            || !TryHttps(candidate.IconUrl, optional: true, out var iconUrl)
            || candidate.MediaUrls.Length > 20
            || candidate.MediaUrls.Distinct(StringComparer.Ordinal).Count()
                != candidate.MediaUrls.Length
            || !TryHttps(candidate.MediaUrls, out var mediaUrls)
            || !TryPublicGitHubRepository(candidate.Source.RepositoryUrl, out var repositoryUrl)
            || !MarketplacePackagePath.IsCanonical(candidate.Source.PackagePath)
            || candidate.Source.PackagePath.Length > 300
            || !SemanticVersion.TryCreate(candidate.Version, out var version)
            || !PluginGitTag.TryCreate(candidate.Tag, out var tag)
            || !PluginMarketplaceCompatibilityPolicy.IsValid(
                new(
                    candidate.Compatibility.BlokeBot,
                    candidate.Compatibility.PluginApi,
                    candidate.Compatibility.Lua,
                    candidate.Compatibility.Targets.ToImmutableArray()
                )
            )
        )
        {
            return false;
        }

        entry = new(
            pluginId,
            candidate.Name,
            candidate.Summary,
            candidate.Author,
            candidate.Tags.ToImmutableArray(),
            iconUrl,
            mediaUrls,
            repositoryUrl!,
            candidate.Source.PackagePath,
            new(version, tag),
            new(
                candidate.Compatibility.BlokeBot,
                candidate.Compatibility.PluginApi,
                candidate.Compatibility.Lua,
                candidate.Compatibility.Targets.ToImmutableArray()
            )
        );
        return true;
    }

    private static bool Length(string value, int minimum, int maximum) =>
        value.Length >= minimum && value.Length <= maximum;

    private static bool TryHttps(string? value, bool optional, out Uri? uri)
    {
        uri = null;
        return value is null
            ? optional
            : value.StartsWith("https://", StringComparison.Ordinal)
                && Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(uri.Host)
                && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool TryHttps(string[] values, out ImmutableArray<Uri> uris)
    {
        var result = ImmutableArray.CreateBuilder<Uri>(values.Length);
        foreach (var value in values)
        {
            if (!TryHttps(value, optional: false, out var uri))
            {
                uris = [];
                return false;
            }

            result.Add(uri!);
        }

        uris = result.MoveToImmutable();
        return true;
    }

    private static bool TryPublicGitHubRepository(string value, out Uri? repository)
    {
        repository = null;
        if (!TryHttps(value, optional: false, out var uri))
        {
            return false;
        }

        var components = uri!.AbsolutePath.Trim('/').Split('/');
        if (
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || uri.AbsolutePath.EndsWith('/')
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || components.Length != 2
            || components.Any(component => component.Length == 0)
            || components[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            || components.Any(component =>
                component.Any(character =>
                    character
                        is not (
                            (>= 'a' and <= 'z')
                            or (>= 'A' and <= 'Z')
                            or (>= '0' and <= '9')
                            or '.'
                            or '-'
                            or '_'
                        )
                )
            )
        )
        {
            return false;
        }

        repository = uri;
        return true;
    }

    private static PluginMarketplaceCatalogValidationOutcome Rejected(
        PluginMarketplaceCatalogFailureCode code,
        string location
    ) => new PluginMarketplaceCatalogValidationOutcome.Rejected(new(code, location));

    private sealed record CatalogDocument
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; init; }

        public required int SchemaVersion { get; init; }

        public required CatalogEntryDocument[] Plugins { get; init; }
    }

    private sealed record CatalogEntryDocument
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Summary { get; init; }
        public required string Author { get; init; }
        public required string[] Tags { get; init; }
        public string? IconUrl { get; init; }
        public string[] MediaUrls { get; init; } = [];
        public required CatalogSourceDocument Source { get; init; }
        public required string Version { get; init; }
        public required string Tag { get; init; }
        public required CatalogCompatibilityDocument Compatibility { get; init; }
    }

    private sealed record CatalogSourceDocument
    {
        public required string RepositoryUrl { get; init; }
        public required string PackagePath { get; init; }
    }

    private sealed record CatalogCompatibilityDocument
    {
        public required string BlokeBot { get; init; }
        public required string PluginApi { get; init; }
        public required string Lua { get; init; }
        public required string[] Targets { get; init; }
    }
}

internal static class PluginMarketplaceCompatibilityPolicy
{
    internal static bool IsValid(PluginMarketplaceCompatibility compatibility) =>
        TryBlokeBotRange(compatibility.BlokeBot, out _, out _)
        && int.TryParse(
            compatibility.PluginApi,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var apiVersion
        )
        && PluginApiVersion.TryCreate(apiVersion, out _)
        && compatibility.Lua == "5.4"
        && compatibility.Targets.Length > 0
        && compatibility.Targets.Distinct(StringComparer.Ordinal).Count()
            == compatibility.Targets.Length
        && compatibility.Targets.All(ValidTarget);

    internal static bool IsCompatible(
        PluginMarketplaceCompatibility compatibility,
        PluginHostCompatibilityTarget target
    ) =>
        TryBlokeBotRange(compatibility.BlokeBot, out var minimum, out var maximumExclusive)
        && int.TryParse(
            compatibility.PluginApi,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var apiVersion
        )
        && apiVersion == target.ApiVersion.Value
        && minimum.CompareTo(target.BlokeBotVersion) <= 0
        && maximumExclusive.CompareTo(target.BlokeBotVersion) > 0
        && compatibility.Lua == "5.4"
        && (
            compatibility.Targets.Contains("any", StringComparer.Ordinal)
            || compatibility.Targets.Contains(
                Target(target.RuntimeIdentifier),
                StringComparer.Ordinal
            )
        );

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

    private static bool ValidTarget(string value) =>
        value is "any" or "linux-x64" or "linux-arm64" or "osx-arm64" or "win-x64" or "win-arm64";

    private static string Target(PluginRuntimeIdentifier target) =>
        target switch
        {
            PluginRuntimeIdentifier.LinuxX64 => "linux-x64",
            PluginRuntimeIdentifier.LinuxArm64 => "linux-arm64",
            PluginRuntimeIdentifier.MacOsArm64 => "osx-arm64",
            PluginRuntimeIdentifier.WindowsX64 => "win-x64",
            PluginRuntimeIdentifier.WindowsArm64 => "win-arm64",
        };
}

internal static class MarketplacePackagePath
{
    internal static bool IsCanonical(string? path)
    {
        if (path is null or { Length: < 1 or > 240 } || path[0] == '/' || path[^1] == '/')
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (
                segment is "" or "." or ".."
                || segment.Length > 100
                || segment[^1] is '.' or ' '
                || segment.Any(character =>
                    character
                        is not (
                            (>= 'a' and <= 'z')
                            or (>= 'A' and <= 'Z')
                            or (>= '0' and <= '9')
                            or '.'
                            or '-'
                            or '_'
                        )
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}

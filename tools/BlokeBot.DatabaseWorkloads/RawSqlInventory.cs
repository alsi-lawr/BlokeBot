using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlokeBot.DatabaseWorkloads;

public sealed record RawSqlInventoryDocument(
    int SchemaVersion,
    string SourceCommit,
    IReadOnlyList<RawSqlInventoryEntry> Statements
);

public sealed record RawSqlInventoryEntry(
    string Id,
    string Path,
    int SourceLine,
    string Api,
    string SourceMarker,
    string SqlVerb,
    string Purpose,
    string DialectDependency
);

public sealed class InventoryDriftException(string message) : Exception(message);

public static partial class RawSqlInventory
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static RawSqlInventoryDocument Load(string path)
    {
        var document =
            JsonSerializer.Deserialize<RawSqlInventoryDocument>(
                File.ReadAllBytes(path),
                _jsonOptions
            ) ?? throw new InventoryDriftException("The raw SQL inventory is empty.");
        var valid =
            document.SchemaVersion == 1
            && document.SourceCommit == "2166d8209b316db64607bb8f92dc2e0d5772406c"
            && document.Statements.Count > 0
            && document.Statements.Select(static entry => entry.Id).Distinct().Count()
                == document.Statements.Count
            && document.Statements.All(static entry =>
                !string.IsNullOrWhiteSpace(entry.Id)
                && !string.IsNullOrWhiteSpace(entry.Path)
                && entry.SourceLine > 0
                && !string.IsNullOrWhiteSpace(entry.Api)
                && !string.IsNullOrWhiteSpace(entry.SourceMarker)
                && !string.IsNullOrWhiteSpace(entry.SqlVerb)
                && !string.IsNullOrWhiteSpace(entry.Purpose)
                && !string.IsNullOrWhiteSpace(entry.DialectDependency)
            );
        return valid
            ? document
            : throw new InventoryDriftException("The raw SQL inventory contract is invalid.");
    }

    public static void Verify(string repositoryRoot, RawSqlInventoryDocument document)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var sourceRoot = Path.Combine(root, "src");
        if (!Directory.Exists(sourceRoot))
        {
            throw new InventoryDriftException("The repository source root does not exist.");
        }

        var discovered = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path =>
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')
            )
            .Where(static path =>
                !path.StartsWith("src/BlokeBot.Plugins.Features/", StringComparison.Ordinal)
                && !path.Contains("/bin/", StringComparison.Ordinal)
                && !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.StartsWith(
                    "src/BlokeBot.Persistence/Migrations/",
                    StringComparison.Ordinal
                )
            )
            .SelectMany(path => Discover(root, path))
            .ToArray();

        var catalogReferences = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}BlokeBot.Plugins.Features{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}BlokeBot.Persistence{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                var relativePath = Path.GetRelativePath(root, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                return SqliteCatalog()
                    .Matches(source)
                    .Select(match => new CatalogReference(
                        relativePath,
                        SourceLine(source, match.Index),
                        match.Value
                    ));
            })
            .OrderBy(static reference => reference.Path, StringComparer.Ordinal)
            .ThenBy(static reference => reference.SourceLine)
            .ToArray();
        CatalogReference[] reviewedCatalogReferences =
        [
            new(
                "src/BlokeBot.DatabaseCutover/DatabaseCutoverRunner.Preflight.cs",
                115,
                "sqlite_schema"
            ),
            new("src/BlokeBot.Persistence/HetznerBaselineBridge.cs", 71, "sqlite_schema"),
            new("src/BlokeBot.Persistence/HetznerBaselineBridge.cs", 90, "sqlite_schema"),
            new("src/BlokeBot.Persistence/HetznerBaselineBridge.cs", 111, "sqlite_schema"),
        ];
        if (!catalogReferences.SequenceEqual(reviewedCatalogReferences))
        {
            throw new InventoryDriftException(
                "The SQLite schema-catalog inventory no longer matches the reviewed legacy-bridge and cutover sqlite_schema reads."
            );
        }

        var remaining = document.Statements.ToList();
        foreach (var statement in discovered)
        {
            var matches = remaining
                .Where(entry =>
                    entry.Path == statement.Path
                    && entry.SourceLine == statement.SourceLine
                    && entry.Api == statement.Api
                    && statement.NormalizedSource.Contains(
                        NormalizeWhitespace(entry.SourceMarker),
                        StringComparison.Ordinal
                    )
                )
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InventoryDriftException(
                    $"Raw SQL at {statement.Path}:{statement.SourceLine} ({statement.Api}) has {matches.Length} inventory entries."
                );
            }
            _ = remaining.Remove(matches[0]);
        }
        if (remaining.Count != 0)
        {
            throw new InventoryDriftException(
                $"The inventory contains {remaining.Count} statements not found in source: {string.Join(", ", remaining.Select(static entry => entry.Id))}."
            );
        }
    }

    private static IEnumerable<DiscoveredStatement> Discover(string root, string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(root, relativePath));
        var matches = RawSqlApi().Matches(source);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var end = index + 1 < matches.Count ? matches[index + 1].Index : source.Length;
            yield return new(
                relativePath,
                SourceLine(source, match.Index),
                match.Groups[1].Success ? match.Groups[1].Value : "DbCommand.CommandText",
                NormalizeWhitespace(source[match.Index..end])
            );
        }
    }

    private static int SourceLine(string source, int index) =>
        source.AsSpan(0, index).Count('\n') + 1;

    private static string NormalizeWhitespace(string value) =>
        Whitespace().Replace(value, " ").Trim();

    [GeneratedRegex(
        @"\.(ExecuteSql(?:Raw|Interpolated)(?:Async)?|FromSql(?:Raw|Interpolated)|SqlQuery(?:Raw|Interpolated)?|InsertIgnoreAsync|ExecuteDialectAsync)\s*\(|\b\w+\.CommandText\s*=",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex RawSqlApi();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\bsqlite_(?:master|schema)\b", RegexOptions.CultureInvariant)]
    private static partial Regex SqliteCatalog();

    private sealed record DiscoveredStatement(
        string Path,
        int SourceLine,
        string Api,
        string NormalizedSource
    );

    private sealed record CatalogReference(string Path, int SourceLine, string Value);
}

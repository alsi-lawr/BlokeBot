using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts.Testing;

public static class PublishedPluginExampleSourceLoader
{
    private const string _descriptorFile = "blokebot.example.json";
    private static readonly JsonSerializerOptions _options = CreateOptions();

    public static async ValueTask<IReadOnlyList<PublishedPluginExample>> LoadAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        var root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Example source root '{root}' was not found.");
        }

        RejectLink(root);
        var descriptorPaths = EnumerateTree(root, cancellationToken)
            .OfType<SourceTreeEntry.File>()
            .Where(entry =>
                string.Equals(
                    Path.GetFileName(entry.FullPath),
                    _descriptorFile,
                    StringComparison.Ordinal
                )
            )
            .Select(entry => entry.FullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var examples = new List<PublishedPluginExample>();
        foreach (var descriptorPath in descriptorPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(descriptorPath)!;
            await using var descriptorStream = File.OpenRead(descriptorPath);
            var descriptor =
                await JsonSerializer.DeserializeAsync<SourceDescriptor>(
                    descriptorStream,
                    _options,
                    cancellationToken
                )
                ?? throw new InvalidDataException(
                    $"Example descriptor '{descriptorPath}' is empty."
                );
            if (string.IsNullOrWhiteSpace(descriptor.Name) || descriptor.Scenarios.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    $"Example descriptor '{descriptorPath}' has no name or scenarios."
                );
            }

            var scenarios = descriptor.Scenarios.Select(MapScenario).ToImmutableArray();
            var package = await LoadPackageAsync(directory, cancellationToken);
            examples.Add(new(descriptor.Name, directory, package, scenarios));
        }

        return examples.Count == 0
            ? throw new InvalidDataException("No published plugin examples were found.")
            : examples.AsReadOnly();
    }

    private static async ValueTask<IReadOnlyList<PluginPackageEntry>> LoadPackageAsync(
        string directory,
        CancellationToken cancellationToken
    )
    {
        var entries = new List<PluginPackageEntry>();
        foreach (
            var entry in EnumerateTree(directory, cancellationToken)
                .OrderBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Ignored(entry.RelativePath))
            {
                continue;
            }

            switch (entry)
            {
                case SourceTreeEntry.File file:
                    entries.Add(
                        new PluginPackageEntry.File(
                            file.RelativePath,
                            await File.ReadAllBytesAsync(file.FullPath, cancellationToken)
                        )
                    );
                    break;
                case SourceTreeEntry.Link link:
                    entries.Add(
                        new PluginPackageEntry.SymbolicLink(link.RelativePath, link.Target)
                    );
                    break;
            }
        }

        return entries.AsReadOnly();
    }

    private static IReadOnlyList<SourceTreeEntry> EnumerateTree(
        string root,
        CancellationToken cancellationToken
    )
    {
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<DirectoryInfo>();
        var entries = new List<SourceTreeEntry>();
        pending.Push(new(fullRoot));
        while (pending.TryPop(out var directory))
        {
            foreach (
                var candidate in directory
                    .EnumerateFileSystemInfos()
                    .OrderBy(info => info.Name, StringComparer.Ordinal)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(fullRoot, candidate.FullName)
                    .Replace('\\', '/');
                if (HasLinkEvidence(candidate))
                {
                    entries.Add(
                        new SourceTreeEntry.Link(
                            relative,
                            candidate.LinkTarget ?? "<filesystem-reparse-point>"
                        )
                    );
                    continue;
                }

                if (candidate is DirectoryInfo child)
                {
                    pending.Push(child);
                }
                else
                {
                    entries.Add(new SourceTreeEntry.File(relative, candidate.FullName));
                }
            }
        }

        return entries.AsReadOnly();
    }

    private static void RejectLink(string root)
    {
        var directory = new DirectoryInfo(root);
        if (HasLinkEvidence(directory))
        {
            throw new InvalidDataException($"Example source root '{root}' is a filesystem link.");
        }
    }

    private static bool HasLinkEvidence(FileSystemInfo info) =>
        info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool Ignored(string relativePath) =>
        relativePath == _descriptorFile
        || relativePath.Equals("README.md", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(relativePath).Equals(".luarc.json", StringComparison.Ordinal);

    private static PublishedPluginExampleScenario MapScenario(SourceScenario scenario) =>
        !string.IsNullOrWhiteSpace(scenario.Name)
        && PluginLuaModuleId.TryCreate(scenario.Module, out var module)
        && PluginHostOperationId.TryCreate(scenario.Operation, out var operation)
            ? new(
                scenario.Name,
                scenario.WorkerMode,
                scenario.InvocationKind,
                module,
                operation,
                scenario.Expectation
            )
            : throw new InvalidDataException("An example scenario has invalid identifiers.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        );
        return options;
    }

    private abstract record SourceTreeEntry(string RelativePath)
    {
        internal sealed record File(string RelativePath, string FullPath)
            : SourceTreeEntry(RelativePath);

        internal sealed record Link(string RelativePath, string Target)
            : SourceTreeEntry(RelativePath);
    }

    private sealed record SourceDescriptor(string Name, ImmutableArray<SourceScenario> Scenarios);

    private sealed record SourceScenario(
        string Name,
        PluginWorkerMode WorkerMode,
        PublishedPluginExampleInvocationKind InvocationKind,
        string Module,
        string Operation,
        PublishedPluginExampleExpectation Expectation
    );
}

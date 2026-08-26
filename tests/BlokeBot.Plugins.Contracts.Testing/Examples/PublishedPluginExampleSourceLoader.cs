using System.Collections.Immutable;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace BlokeBot.Plugins.Contracts.Testing;

public static class PublishedPluginExampleSourceLoader
{
    private const string _descriptorFile = "tests.toml";

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
            var descriptor = await LoadDescriptorAsync(descriptorPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(descriptor.Name) || descriptor.Scenarios.Count == 0)
            {
                throw new InvalidDataException(
                    $"Example descriptor '{descriptorPath}' has no name or scenarios."
                );
            }

            var scenarios = descriptor.Scenarios.ToImmutableArray();
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

    private static async ValueTask<SourceDescriptor> LoadDescriptorAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        if (new FileInfo(path).Length > PluginContractLimits.MaximumManifestBytes)
        {
            throw new InvalidDataException($"Example descriptor '{path}' is too large.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length > PluginContractLimits.MaximumManifestBytes)
        {
            throw new InvalidDataException($"Example descriptor '{path}' is too large.");
        }

        string content;
        try
        {
            content = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"Example descriptor '{path}' is not valid UTF-8.",
                exception
            );
        }

        var options = new TomlSerializerOptions
        {
            DuplicateKeyHandling = TomlDuplicateKeyHandling.Error,
            MaxDepth = 8,
            PropertyNameCaseInsensitive = false,
        };
        TomlTable document;
        try
        {
            document =
                TomlSerializer.Deserialize<TomlTable>(content, options)
                ?? throw new InvalidDataException($"Example descriptor '{path}' is empty.");
        }
        catch (TomlException exception)
        {
            throw new InvalidDataException(
                $"Example descriptor '{path}' is malformed TOML.",
                exception
            );
        }

        return
            !document.Keys.Any(key => key is not "name" and not "scenarios")
            && document.TryGetValue("name", out var nameValue)
            && nameValue is string name
            && document.TryGetValue("scenarios", out var scenariosValue)
            && scenariosValue is IEnumerable<object> scenarios
            ? new(name, scenarios.Select(MapScenario).ToArray())
            : throw new InvalidDataException($"Example descriptor '{path}' has an invalid shape.");
    }

    private static PublishedPluginExampleScenario MapScenario(object value) =>
        value is TomlTable scenario
        && !scenario.Keys.Any(key =>
            key
                is not "name"
                    and not "workerMode"
                    and not "invocationKind"
                    and not "module"
                    and not "operation"
                    and not "expectation"
        )
        && RequiredString(scenario, "name", out var name)
        && RequiredString(scenario, "workerMode", out var workerModeName)
        && WorkerMode(workerModeName, out var workerMode)
        && RequiredString(scenario, "invocationKind", out var invocationKindName)
        && InvocationKind(invocationKindName, out var invocationKind)
        && RequiredString(scenario, "module", out var moduleName)
        && PluginLuaModuleId.TryCreate(moduleName, out var module)
        && RequiredString(scenario, "operation", out var operationName)
        && PluginHostOperationId.TryCreate(operationName, out var operation)
        && RequiredString(scenario, "expectation", out var expectationName)
        && Expectation(expectationName, out var expectation)
            ? new(name, workerMode, invocationKind, module, operation, expectation)
            : throw new InvalidDataException(
                "An example scenario has an invalid TOML declaration."
            );

    private static bool RequiredString(TomlTable table, string name, out string value)
    {
        var valid = table.TryGetValue(name, out var candidate) && candidate is string;
        value = valid ? (string)candidate! : string.Empty;
        return valid && !string.IsNullOrWhiteSpace(value);
    }

    private static bool WorkerMode(string name, out PluginWorkerMode mode)
    {
        var value = name switch
        {
            "admitted" => PluginWorkerMode.Admitted,
            "staging" => PluginWorkerMode.Staging,
            _ => (PluginWorkerMode?)null,
        };
        mode = value.GetValueOrDefault();
        return value.HasValue;
    }

    private static bool InvocationKind(string name, out PublishedPluginExampleInvocationKind kind)
    {
        var value = name switch
        {
            "lifecycle" => PublishedPluginExampleInvocationKind.Lifecycle,
            "migration" => PublishedPluginExampleInvocationKind.Migration,
            "command" => PublishedPluginExampleInvocationKind.Command,
            "event" => PublishedPluginExampleInvocationKind.Event,
            "schedule" => PublishedPluginExampleInvocationKind.Schedule,
            "hostAction" => PublishedPluginExampleInvocationKind.HostAction,
            "storage" => PublishedPluginExampleInvocationKind.Storage,
            "page" => PublishedPluginExampleInvocationKind.Page,
            "automation" => PublishedPluginExampleInvocationKind.Automation,
            _ => (PublishedPluginExampleInvocationKind?)null,
        };
        kind = value.GetValueOrDefault();
        return value.HasValue;
    }

    private static bool Expectation(string name, out PublishedPluginExampleExpectation expectation)
    {
        var value = name switch
        {
            "returned" => PublishedPluginExampleExpectation.Returned,
            "failed" => PublishedPluginExampleExpectation.Failed,
            "cancelled" => PublishedPluginExampleExpectation.Cancelled,
            "workerExited" => PublishedPluginExampleExpectation.WorkerExited,
            "migrationFailed" => PublishedPluginExampleExpectation.MigrationFailed,
            _ => (PublishedPluginExampleExpectation?)null,
        };
        expectation = value.GetValueOrDefault();
        return value.HasValue;
    }

    private abstract record SourceTreeEntry(string RelativePath)
    {
        internal sealed record File(string RelativePath, string FullPath)
            : SourceTreeEntry(RelativePath);

        internal sealed record Link(string RelativePath, string Target)
            : SourceTreeEntry(RelativePath);
    }

    private sealed record SourceDescriptor(
        string Name,
        IReadOnlyList<PublishedPluginExampleScenario> Scenarios
    );
}

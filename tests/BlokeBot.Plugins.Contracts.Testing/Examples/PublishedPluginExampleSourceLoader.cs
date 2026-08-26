using System.Collections.Immutable;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace BlokeBot.Plugins.Contracts.Testing;

public static class PublishedPluginExampleSourceLoader
{
    private const string _descriptorFile = "tests.toml";
    private const string _manifestFile = PluginPackage.ManifestPath;

    public static ValueTask<PublishedPluginExampleSourceLoadOutcome> LoadForValidationAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    ) => LoadAsync(sourceRoot, requireTestMetadata: false, cancellationToken);

    public static ValueTask<PublishedPluginExampleSourceLoadOutcome> LoadForTestsAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    ) => LoadAsync(sourceRoot, requireTestMetadata: true, cancellationToken);

    private static async ValueTask<PublishedPluginExampleSourceLoadOutcome> LoadAsync(
        string sourceRoot,
        bool requireTestMetadata,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return Rejected(
                PublishedPluginExampleFailureCode.SourceInvalid,
                "$source",
                "The plugin source path is required."
            );
        }

        try
        {
            var root = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(root))
            {
                return Rejected(
                    PublishedPluginExampleFailureCode.SourceInvalid,
                    Path.GetFileName(root),
                    $"Plugin source root '{root}' was not found."
                );
            }

            if (HasLinkEvidence(new DirectoryInfo(root)))
            {
                return Rejected(
                    PublishedPluginExampleFailureCode.SourceInvalid,
                    Path.GetFileName(root),
                    $"Plugin source root '{root}' is a filesystem link."
                );
            }

            var manifestPaths = EnumerateTree(root, cancellationToken)
                .OfType<SourceTreeEntry.File>()
                .Where(entry =>
                    string.Equals(
                        Path.GetFileName(entry.FullPath),
                        _manifestFile,
                        StringComparison.Ordinal
                    )
                )
                .Select(entry => entry.FullPath)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (manifestPaths.Length == 0)
            {
                return Rejected(
                    PublishedPluginExampleFailureCode.SourceInvalid,
                    Path.GetFileName(root),
                    $"No {_manifestFile} package was found."
                );
            }

            var examples = ImmutableArray.CreateBuilder<PublishedPluginExample>();
            var failures = ImmutableArray.CreateBuilder<PublishedPluginExampleFailure>();
            foreach (var manifestPath in manifestPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = Path.GetDirectoryName(manifestPath)!;
                var name = Path.GetFileName(directory);
                var scenarios = ImmutableArray<PublishedPluginExampleScenario>.Empty;
                if (requireTestMetadata)
                {
                    var descriptorPath = Path.Combine(directory, _descriptorFile);
                    if (!File.Exists(descriptorPath))
                    {
                        failures.Add(
                            new(
                                PublishedPluginExampleFailureCode.TestMetadataMissing,
                                name,
                                descriptorPath
                            )
                        );
                        continue;
                    }
                    if (HasLinkEvidence(new FileInfo(descriptorPath)))
                    {
                        failures.Add(
                            new(
                                PublishedPluginExampleFailureCode.SourceInvalid,
                                name,
                                descriptorPath
                            )
                        );
                        continue;
                    }

                    var descriptor = await LoadDescriptorAsync(descriptorPath, cancellationToken);
                    if (descriptor is SourceDescriptorLoadOutcome.Rejected rejected)
                    {
                        failures.Add(new(rejected.Code, name, descriptorPath));
                        continue;
                    }

                    var loaded = (SourceDescriptorLoadOutcome.Loaded)descriptor;
                    name = loaded.Descriptor.Name;
                    scenarios = [.. loaded.Descriptor.Scenarios];
                }

                var package = await LoadPackageAsync(directory, cancellationToken);
                examples.Add(new(name, directory, package, scenarios));
            }

            return failures.Count == 0
                ? new PublishedPluginExampleSourceLoadOutcome.Loaded(examples.ToImmutable())
                : new PublishedPluginExampleSourceLoadOutcome.Rejected(failures.ToImmutable());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return Rejected(
                PublishedPluginExampleFailureCode.SourceInvalid,
                "$source",
                exception.Message
            );
        }
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

    private static bool HasLinkEvidence(FileSystemInfo info) =>
        info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool Ignored(string relativePath) =>
        relativePath == _descriptorFile
        || relativePath.Equals("README.md", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(relativePath).Equals(".luarc.json", StringComparison.Ordinal);

    private static async ValueTask<SourceDescriptorLoadOutcome> LoadDescriptorAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        if (new FileInfo(path).Length > PluginContractLimits.MaximumManifestBytes)
        {
            return InvalidDescriptor();
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length > PluginContractLimits.MaximumManifestBytes)
        {
            return InvalidDescriptor();
        }

        string content;
        try
        {
            content = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return new SourceDescriptorLoadOutcome.Rejected(
                PublishedPluginExampleFailureCode.TestMetadataMalformed
            );
        }

        var options = new TomlSerializerOptions
        {
            DuplicateKeyHandling = TomlDuplicateKeyHandling.Error,
            MaxDepth = 8,
            PropertyNameCaseInsensitive = false,
        };
        TomlTable? document;
        try
        {
            document = TomlSerializer.Deserialize<TomlTable>(content, options);
        }
        catch (TomlException)
        {
            return new SourceDescriptorLoadOutcome.Rejected(
                PublishedPluginExampleFailureCode.TestMetadataMalformed
            );
        }

        if (
            document is null
            || document.Keys.Any(key => key is not "name" and not "scenarios")
            || !document.TryGetValue("name", out var nameValue)
            || nameValue is not string name
            || string.IsNullOrWhiteSpace(name)
            || !document.TryGetValue("scenarios", out var scenariosValue)
            || scenariosValue is not IEnumerable<object> scenarioValues
        )
        {
            return InvalidDescriptor();
        }

        var scenarios = new List<PublishedPluginExampleScenario>();
        foreach (var scenarioValue in scenarioValues)
        {
            if (!TryMapScenario(scenarioValue, out var scenario))
            {
                return InvalidDescriptor();
            }
            scenarios.Add(scenario);
        }

        return scenarios.Count > 0
            ? new SourceDescriptorLoadOutcome.Loaded(new(name, scenarios.AsReadOnly()))
            : InvalidDescriptor();
    }

    private static bool TryMapScenario(object value, out PublishedPluginExampleScenario scenario)
    {
        if (
            value is not TomlTable candidate
            || candidate.Keys.Any(key =>
                key
                    is not "name"
                        and not "workerMode"
                        and not "invocationKind"
                        and not "module"
                        and not "operation"
                        and not "expectation"
            )
            || !RequiredString(candidate, "name", out var name)
            || !RequiredString(candidate, "workerMode", out var workerModeName)
            || !WorkerMode(workerModeName, out var workerMode)
            || !RequiredString(candidate, "invocationKind", out var invocationKindName)
            || !InvocationKind(invocationKindName, out var invocationKind)
            || !RequiredString(candidate, "module", out var moduleName)
            || !PluginLuaModuleId.TryCreate(moduleName, out var module)
            || !RequiredString(candidate, "operation", out var operationName)
            || !PluginHostOperationId.TryCreate(operationName, out var operation)
            || !RequiredString(candidate, "expectation", out var expectationName)
            || !Expectation(expectationName, out var expectation)
        )
        {
            scenario = null!;
            return false;
        }

        scenario = new(name, workerMode, invocationKind, module, operation, expectation);
        return true;
    }

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

    private static SourceDescriptorLoadOutcome.Rejected InvalidDescriptor() =>
        new(PublishedPluginExampleFailureCode.TestMetadataInvalid);

    private static PublishedPluginExampleSourceLoadOutcome.Rejected Rejected(
        PublishedPluginExampleFailureCode code,
        string example,
        string subject
    ) => new([new(code, example, subject)]);

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

    private abstract record SourceDescriptorLoadOutcome
    {
        private SourceDescriptorLoadOutcome() { }

        internal sealed record Loaded(SourceDescriptor Descriptor) : SourceDescriptorLoadOutcome;

        internal sealed record Rejected(PublishedPluginExampleFailureCode Code)
            : SourceDescriptorLoadOutcome;
    }
}

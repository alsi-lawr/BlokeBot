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

        var examples = new List<PublishedPluginExample>();
        foreach (
            var descriptorPath in Directory
                .EnumerateFiles(root, _descriptorFile, SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
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
        var files = new List<PluginPackageEntry>();
        foreach (
            var path in Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (
                relative == _descriptorFile
                || relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(relative).Equals(".luarc.json", StringComparison.Ordinal)
            )
            {
                continue;
            }

            files.Add(
                new PluginPackageEntry.File(
                    relative,
                    await File.ReadAllBytesAsync(path, cancellationToken)
                )
            );
        }

        return files.AsReadOnly();
    }

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

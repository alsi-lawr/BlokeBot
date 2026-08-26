using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginAuthoringArtifact(string RelativePath, string Content);

public enum PluginAuthoringArtifactDriftCode
{
    Missing,
    Different,
}

public sealed record PluginAuthoringArtifactDrift(
    PluginAuthoringArtifactDriftCode Code,
    string RelativePath
);

public static class PluginAuthoringArtifacts
{
    public static ImmutableArray<PluginAuthoringArtifact> Generate(
        PluginAuthoringContract? contract = null
    )
    {
        var source = contract ?? PluginAuthoringContract.Current;
        return
        [
            new(
                $"sdk/lua/5.4/v{source.Runtime.HostApiVersion.Value}/blokebot.lua",
                PluginLuaLanguageServerStubEmitter.Emit(source)
            ),
            new(
                $"docs/plugin-authoring/v{source.Runtime.HostApiVersion.Value}.md",
                PluginAuthorReferenceEmitter.Emit(source)
            ),
        ];
    }

    public static async ValueTask<IReadOnlyList<PluginAuthoringArtifactDrift>> FindDriftAsync(
        string repositoryRoot,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var failures = new List<PluginAuthoringArtifactDrift>();
        foreach (var artifact in Generate())
        {
            var path = Path.Combine(repositoryRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                failures.Add(new(PluginAuthoringArtifactDriftCode.Missing, artifact.RelativePath));
                continue;
            }

            var actual = await File.ReadAllTextAsync(path, cancellationToken);
            if (!string.Equals(actual, artifact.Content, StringComparison.Ordinal))
            {
                failures.Add(
                    new(PluginAuthoringArtifactDriftCode.Different, artifact.RelativePath)
                );
            }
        }

        return failures.AsReadOnly();
    }

    public static async ValueTask WriteAsync(
        string repositoryRoot,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        foreach (var artifact in Generate())
        {
            var path = Path.Combine(repositoryRoot, artifact.RelativePath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, artifact.Content, cancellationToken);
        }
    }
}

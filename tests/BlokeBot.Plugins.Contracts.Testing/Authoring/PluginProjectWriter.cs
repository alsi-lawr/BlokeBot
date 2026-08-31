using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts.Testing;

public abstract record PluginProjectWriteOutcome
{
    private PluginProjectWriteOutcome() { }

    public sealed record Written(ImmutableArray<string> Paths) : PluginProjectWriteOutcome;

    public sealed record Rejected(string Code, string Subject) : PluginProjectWriteOutcome;
}

public static class PluginProjectWriter
{
    public static async ValueTask<PluginProjectWriteOutcome> InitializeAsync(
        PluginId pluginId,
        string destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        var fullDestination = Path.GetFullPath(destination);
        if (UnsafeExistingPath(fullDestination))
        {
            return Rejected("UnsafeDestination", fullDestination);
        }
        if (File.Exists(fullDestination))
        {
            return Rejected("DestinationNotEmpty", fullDestination);
        }
        if (
            Directory.Exists(fullDestination)
            && Directory.EnumerateFileSystemEntries(fullDestination).Any()
        )
        {
            return Rejected("DestinationNotEmpty", fullDestination);
        }

        var artifacts = PluginProjectArtifacts.Scaffold(pluginId);
        var parent = Path.GetDirectoryName(fullDestination)!;
        _ = Directory.CreateDirectory(parent);
        var stage = Path.Combine(
            parent,
            $".{Path.GetFileName(fullDestination)}.blokebot-init-{Guid.NewGuid():N}"
        );
        try
        {
            await WriteAllAsync(stage, artifacts, cancellationToken);
            if (Directory.Exists(fullDestination))
            {
                Directory.Delete(fullDestination);
            }
            Directory.Move(stage, fullDestination);
            return new PluginProjectWriteOutcome.Written([
                .. artifacts.Select(artifact =>
                    Path.Combine(fullDestination, artifact.RelativePath)
                ),
            ]);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    public static async ValueTask<PluginProjectWriteOutcome> GenerateAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    )
    {
        var loaded = await PluginProjectArtifacts.LoadAsync(sourceRoot, cancellationToken);
        if (loaded is PluginProjectLoadOutcome.Rejected rejected)
        {
            return Rejected(rejected.Code, rejected.Subject);
        }

        var root = Path.GetFullPath(sourceRoot);
        var target = Path.Combine(
            root,
            PluginProjectArtifacts.GeneratedRoot.Replace('/', Path.DirectorySeparatorChar)
        );
        if (UnsafeExistingPath(target))
        {
            return Rejected("UnsafeGeneratedDestination", target);
        }
        var marker = Path.Combine(target, PluginProjectArtifacts.GeneratedMarker);
        if (
            (File.Exists(target) || Directory.Exists(target))
            && (!Directory.Exists(target) || !File.Exists(marker))
        )
        {
            return Rejected("GeneratedDestinationNotOwned", target);
        }

        var artifacts = PluginProjectArtifacts.Generate(
            ((PluginProjectLoadOutcome.Loaded)loaded).Manifest
        );
        var parent = Path.GetDirectoryName(target)!;
        _ = Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".generate-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".previous-{Guid.NewGuid():N}");
        try
        {
            await WriteGeneratedAsync(stage, artifacts, cancellationToken);
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
            }
            try
            {
                Directory.Move(stage, target);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(target))
                {
                    Directory.Move(backup, target);
                }
                throw;
            }
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
            return new PluginProjectWriteOutcome.Written([
                .. artifacts.Select(artifact => Path.Combine(root, artifact.RelativePath)),
            ]);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
            if (Directory.Exists(backup) && Directory.Exists(target))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
    }

    private static async ValueTask WriteAllAsync(
        string root,
        IEnumerable<PluginProjectArtifact> artifacts,
        CancellationToken cancellationToken
    )
    {
        foreach (var artifact in artifacts)
        {
            var path = Path.Combine(
                root,
                artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, artifact.Content, cancellationToken);
        }
    }

    private static ValueTask WriteGeneratedAsync(
        string root,
        IEnumerable<PluginProjectArtifact> artifacts,
        CancellationToken cancellationToken
    ) =>
        WriteAllAsync(
            root,
            artifacts.Select(artifact =>
                artifact with
                {
                    RelativePath = artifact.RelativePath[
                        (PluginProjectArtifacts.GeneratedRoot.Length + 1)..
                    ],
                }
            ),
            cancellationToken
        );

    private static bool UnsafeExistingPath(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (!current.Exists)
            {
                continue;
            }
            if (
                current.LinkTarget is not null
                || (current.Attributes & FileAttributes.ReparsePoint) != 0
            )
            {
                return true;
            }
        }
        return false;
    }

    private static PluginProjectWriteOutcome.Rejected Rejected(string code, string subject) =>
        new(code, subject);
}

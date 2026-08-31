using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginProjectArtifact(string RelativePath, string Content);

public abstract record PluginProjectLoadOutcome
{
    private PluginProjectLoadOutcome() { }

    public sealed record Loaded(PluginManifest Manifest) : PluginProjectLoadOutcome;

    public sealed record Rejected(string Code, string Subject) : PluginProjectLoadOutcome;
}

public static class PluginProjectArtifacts
{
    public const string GeneratedRoot = ".blokebot/lua/5.4/v1";
    public const string GeneratedMarker = ".generated-by-blokebot-plugin";
    public const string GeneratedHandlerSkeleton = "handler-skeletons.lua";

    public static ImmutableArray<PluginProjectArtifact> Generate(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var contract = PluginAuthoringContract.Current;
        return
        [
            new($"{GeneratedRoot}/blokebot.lua", PluginLuaLanguageServerStubEmitter.Emit(contract)),
            new($"{GeneratedRoot}/plugin.lua", PluginProjectTypeEmitter.Emit(manifest)),
            new(
                $"{GeneratedRoot}/{GeneratedHandlerSkeleton}",
                PluginProjectHandlerSkeletonEmitter.Emit(manifest)
            ),
            new(
                $"{GeneratedRoot}/{GeneratedMarker}",
                $"BlokeBot plugin generator v{contract.Runtime.HostApiVersion.Value}\n"
            ),
        ];
    }

    public static ImmutableArray<PluginProjectArtifact> Scaffold(PluginId pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        var manifest = PluginProjectScaffoldEmitter.Manifest(pluginId);
        var validated = PluginManifestToml.Validate(
            System.Text.Encoding.UTF8.GetBytes(manifest),
            PluginAuthoringContract.Current.Target(PluginRuntimeIdentifier.LinuxX64)
        );
        var pluginManifest = validated is PluginManifestValidationOutcome.Accepted accepted
            ? accepted.Manifest.Manifest
            : throw new InvalidOperationException("The generated starter manifest is invalid.");
        return
        [
            new(PluginPackage.ManifestPath, manifest),
            new("lua/main.lua", PluginProjectScaffoldEmitter.Lua(pluginManifest)),
            new("tests.toml", PluginProjectScaffoldEmitter.Tests(pluginId)),
            new(".luarc.json", PluginProjectScaffoldEmitter.LuaLanguageServerConfiguration()),
            .. Generate(pluginManifest),
        ];
    }

    public static async ValueTask<PluginProjectLoadOutcome> LoadAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    )
    {
        var loaded = await PublishedPluginExampleSourceLoader.LoadForValidationAsync(
            sourceRoot,
            cancellationToken
        );
        if (loaded is PublishedPluginExampleSourceLoadOutcome.Rejected rejected)
        {
            var failure = rejected.Failures.First();
            return new PluginProjectLoadOutcome.Rejected(failure.Code.ToString(), failure.Subject);
        }

        var root = Path.GetFullPath(sourceRoot);
        var examples = ((PublishedPluginExampleSourceLoadOutcome.Loaded)loaded).Examples;
        if (
            examples.Length != 1
            || !string.Equals(examples[0].SourceDirectory, root, StringComparison.Ordinal)
        )
        {
            return new PluginProjectLoadOutcome.Rejected(
                "ProjectRootInvalid",
                "The source must contain exactly one plugin.toml at its root."
            );
        }

        PluginManifest? manifest = null;
        foreach (var runtimeIdentifier in PluginAuthoringContract.Current.RuntimeIdentifiers)
        {
            var validation = PluginPackageValidator.Validate(
                examples[0].Package,
                PluginAuthoringContract.Current.Target(runtimeIdentifier)
            );
            if (validation is PluginPackageValidationOutcome.Rejected packageRejected)
            {
                return new PluginProjectLoadOutcome.Rejected(
                    "PackageRejected",
                    $"{runtimeIdentifier}: {string.Join(", ", packageRejected.Errors)}"
                );
            }
            manifest = ((PluginPackageValidationOutcome.Accepted)validation).Manifest.Manifest;
        }

        return new PluginProjectLoadOutcome.Loaded(manifest!);
    }
}

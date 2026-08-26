using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginAuthoringContract(
    PluginRuntimeContract Runtime,
    ImmutableArray<PluginRuntimeIdentifier> RuntimeIdentifiers,
    ImmutableArray<PluginHostModuleDescriptor> HostModules
)
{
    public static PluginAuthoringContract Current { get; } =
        new(
            PluginRuntimeContract.Current,
            [.. Enum.GetValues<PluginRuntimeIdentifier>()],
            PluginStandardHostModules.All
        );

    public PluginHostCompatibilityTarget Target(
        PluginRuntimeIdentifier runtimeIdentifier,
        SemanticVersion? blokeBotVersion = null
    ) =>
        new(
            blokeBotVersion ?? SemanticVersion("0.13.0"),
            Runtime.HostApiVersion,
            runtimeIdentifier,
            HostModules
        );

    private static SemanticVersion SemanticVersion(string value) =>
        BlokeBot.Plugins.Contracts.SemanticVersion.TryCreate(value, out var version)
            ? version
            : throw new InvalidOperationException($"Invalid authoring contract version '{value}'.");
}

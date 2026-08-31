using System.Collections.Immutable;
using System.Reflection;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginAuthoringContractSurface(Assembly Assembly, string Namespace)
{
    public ImmutableArray<Type> ExportedTypes =>
        [
            .. Assembly
                .GetExportedTypes()
                .Where(type => string.Equals(type.Namespace, Namespace, StringComparison.Ordinal))
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];
}

public sealed record PluginAuthoringContract(
    PluginRuntimeContract Runtime,
    PluginIdentifierSyntaxContract IdentifierSyntax,
    PluginGitTagSyntaxContract GitTagSyntax,
    ImmutableArray<PluginRuntimeIdentifier> RuntimeIdentifiers,
    ImmutableArray<PluginHostModuleDescriptor> HostModules,
    ImmutableArray<PluginLuaSchemaDescriptor> InvocationInputSchemas,
    ImmutableArray<PluginLuaSchemaDescriptor> StructuredValueSchemas,
    ImmutableArray<PluginLuaUnionDescriptor> StructuredValueUnions,
    ImmutableArray<PluginAuthoringContractSurface> PublicContractSurfaces
)
{
    public static PluginAuthoringContract Current { get; } =
        new(
            PluginRuntimeContract.Current,
            PluginIdentifierSyntaxContract.Current,
            PluginGitTagSyntaxContract.Current,
            [.. Enum.GetValues<PluginRuntimeIdentifier>()],
            PluginStandardHostModules.All,
            PluginInvocationInputSchemas.All,
            PluginStructuredValueSchemas.All,
            PluginStructuredValueSchemas.Unions,
            [Surface(typeof(PluginRuntimeContract)), Surface(typeof(PluginLifecycleOutcome))]
        );

    public ImmutableArray<Type> PublicContractTypes =>
        [
            .. PublicContractSurfaces
                .SelectMany(surface => surface.ExportedTypes)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

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

    private static PluginAuthoringContractSurface Surface(Type authority) =>
        new(authority.Assembly, authority.Namespace!);

    private static SemanticVersion SemanticVersion(string value) =>
        BlokeBot.Plugins.Contracts.SemanticVersion.TryCreate(value, out var version)
            ? version
            : throw new InvalidOperationException($"Invalid authoring contract version '{value}'.");
}

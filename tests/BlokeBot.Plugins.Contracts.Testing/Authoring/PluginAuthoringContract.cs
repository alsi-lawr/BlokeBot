using System.Collections.Immutable;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginAuthoringSemanticSurface(string Title, Type ContractType);

public sealed record PluginAuthoringContract(
    PluginRuntimeContract Runtime,
    PluginIdentifierSyntaxContract IdentifierSyntax,
    PluginGitTagSyntaxContract GitTagSyntax,
    ImmutableArray<PluginRuntimeIdentifier> RuntimeIdentifiers,
    ImmutableArray<PluginHostModuleDescriptor> HostModules,
    ImmutableArray<PluginAuthoringSemanticSurface> SemanticSurfaces
)
{
    public static PluginAuthoringContract Current { get; } =
        new(
            PluginRuntimeContract.Current,
            PluginIdentifierSyntaxContract.Current,
            PluginGitTagSyntaxContract.Current,
            [.. Enum.GetValues<PluginRuntimeIdentifier>()],
            PluginStandardHostModules.All,
            [
                new("Trust", typeof(PluginTrustContract)),
                new("Identifier syntax", typeof(PluginIdentifierSyntaxContract)),
                new("Mutable Git tag syntax", typeof(PluginGitTagSyntaxContract)),
                new("Release identity", typeof(PluginReleaseIdentity)),
                new("Manifest", typeof(PluginManifest)),
                new("Manifest validation", typeof(PluginManifestValidationOutcome)),
                new("Manifest failures", typeof(PluginManifestErrorCode)),
                new("Package entries", typeof(PluginPackageEntry)),
                new("Package validation", typeof(PluginPackageValidationOutcome)),
                new("Package failures", typeof(PluginPackageEntryErrorCode)),
                new("Compatibility target", typeof(PluginHostCompatibilityTarget)),
                new("Compatibility", typeof(PluginCompatibilityOutcome)),
                new("Compatibility failures", typeof(PluginCompatibilityFailureCode)),
                new("Host call", typeof(PluginHostCall)),
                new("Host calls", typeof(PluginHostCallOutcome)),
                new("Host-call failures", typeof(PluginHostFailureCode)),
                new("Cancellation reasons", typeof(PluginCancellationReason)),
                new("Worker invocation identity", typeof(PluginWorkerInvocationIdentity)),
                new("Worker invocation result", typeof(PluginWorkerInvocationResult)),
                new("Worker invocations", typeof(PluginWorkerInvocationOutcome)),
                new("Worker failures", typeof(PluginWorkerFailureCode)),
                new("Lifecycle view", typeof(PluginLifecycleView)),
                new("Lifecycle outcome", typeof(PluginLifecycleOutcome)),
                new("Lifecycle commands", typeof(PluginLifecycleCommandOutcome)),
                new("Lifecycle command rejections", typeof(PluginLifecycleCommandRejectionCode)),
                new("Lifecycle phases", typeof(PluginLifecyclePhase)),
                new("Lifecycle operations", typeof(PluginLifecycleOperationKind)),
                new("Lifecycle outcomes", typeof(PluginLifecycleOutcomeCode)),
                new("Lifecycle failures", typeof(PluginLifecycleFailureCode)),
            ]
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

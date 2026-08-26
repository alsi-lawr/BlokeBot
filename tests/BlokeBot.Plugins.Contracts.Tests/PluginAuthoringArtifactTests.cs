using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginAuthoringArtifactTests
{
    [Test]
    public async Task GeneratedAuthorArtifacts_MatchCanonicalContract()
    {
        var drift = await PluginAuthoringArtifacts.FindDriftAsync(
            Path.Combine(AppContext.BaseDirectory, "AuthoringArtifacts"),
            CancellationToken.None
        );

        drift.ShouldBeEmpty();
    }

    [Test]
    public void GeneratedReference_CoversEveryCanonicalOutcomeFailureMemberAndField()
    {
        var contract = PluginAuthoringContract.Current;
        Type[] requiredSurfaces =
        [
            typeof(PluginTrustContract),
            typeof(PluginIdentifierSyntaxContract),
            typeof(PluginGitTagSyntaxContract),
            typeof(PluginReleaseIdentity),
            typeof(PluginManifest),
            typeof(PluginManifestValidationOutcome),
            typeof(PluginManifestErrorCode),
            typeof(PluginPackageEntry),
            typeof(PluginPackageValidationOutcome),
            typeof(PluginPackageEntryErrorCode),
            typeof(PluginHostCompatibilityTarget),
            typeof(PluginCompatibilityOutcome),
            typeof(PluginCompatibilityFailureCode),
            typeof(PluginHostCall),
            typeof(PluginHostCallOutcome),
            typeof(PluginHostFailureCode),
            typeof(PluginCancellationReason),
            typeof(PluginWorkerInvocationIdentity),
            typeof(PluginWorkerInvocationResult),
            typeof(PluginWorkerInvocationOutcome),
            typeof(PluginWorkerFailureCode),
            typeof(PluginLifecycleView),
            typeof(PluginLifecycleOutcome),
            typeof(PluginLifecycleCommandOutcome),
            typeof(PluginLifecycleCommandRejectionCode),
            typeof(PluginLifecyclePhase),
            typeof(PluginLifecycleOperationKind),
            typeof(PluginLifecycleOutcomeCode),
            typeof(PluginLifecycleFailureCode),
        ];
        contract
            .SemanticSurfaces.Select(surface => surface.ContractType)
            .ShouldBe(requiredSurfaces);
        var reference = PluginAuthoringArtifacts
            .Generate(contract)
            .Single(artifact => artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .Content;

        PluginAuthoringSemanticCoverage.FindOmissions(reference, contract).ShouldBeEmpty();
    }
}

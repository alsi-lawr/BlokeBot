using BlokeBot.Plugins.Contracts.Testing;
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
    public void GeneratedReference_CoversEveryExportedCanonicalPublicTypeAndMember()
    {
        var contract = PluginAuthoringContract.Current;
        var reference = PluginAuthoringArtifacts
            .Generate(contract)
            .Single(artifact => artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .Content;

        PluginAuthoringSemanticCoverage.FindOmissions(reference, contract).ShouldBeEmpty();
    }

    [Test]
    public void GeneratedReference_DetectsAnOmittedCanonicalPublicMember()
    {
        var contract = PluginAuthoringContract.Current;
        var reference = PluginAuthoringArtifacts
            .Generate(contract)
            .Single(artifact => artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .Content;
        var member = contract
            .PublicContractTypes.SelectMany(type => PluginAuthoringSemanticCoverage.Members(type))
            .Last();
        var omitted = reference.Replace(
            $"`{member.CanonicalName}`",
            "`intentionally-omitted`",
            StringComparison.Ordinal
        );

        PluginAuthoringSemanticCoverage
            .FindOmissions(omitted, contract)
            .ShouldContain(new PluginAuthoringSemanticOmission(member.CanonicalName));
    }
}

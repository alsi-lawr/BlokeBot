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
}

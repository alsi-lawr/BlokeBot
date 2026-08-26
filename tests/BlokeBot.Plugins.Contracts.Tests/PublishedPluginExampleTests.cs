using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PublishedPluginExampleTests
{
    [Test]
    public async Task PublishedExamples_ValidateForEveryTargetAndExecuteThroughWorkerProtocol()
    {
        var outcome = await PublishedPluginExampleHarness.RunAsync(
            new(
                Path.Combine(AppContext.BaseDirectory, "PublishedPluginExamples"),
                new(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "plugin-worker",
                        "BlokeBot.PluginWorker.dll"
                    )
                )
            ),
            CancellationToken.None
        );

        var passed = outcome.ShouldBeOfType<PublishedPluginExampleHarnessOutcome.Passed>();
        passed.Observations.ShouldAllBe(observation =>
            observation.ValidatedRuntimeIdentifiers.Length
                == PluginAuthoringContract.Current.RuntimeIdentifiers.Length
            && !observation.ExecutedScenarios.IsDefaultOrEmpty
        );
        var showcase = passed.Observations.Single(observation =>
            observation.Example == "author-showcase"
        );
        showcase.ExternalEffectRemainedCompleted.ShouldBeTrue();
        showcase.LateHostResultDiscarded.ShouldBeTrue();
        var update = passed.Observations.Single(observation =>
            observation.Example == "update-failure"
        );
        update.UpdateMigrationFaulted.ShouldBeTrue();
        update.OldRuntimeRemainedStopped.ShouldBeTrue();
        update.UpdateRecoveryRemainedFaulted.ShouldBeTrue();
    }
}

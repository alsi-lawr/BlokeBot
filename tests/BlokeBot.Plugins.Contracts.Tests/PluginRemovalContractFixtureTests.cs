using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginRemovalContractFixtureTests
{
    [Test]
    public async Task Remove_DeletesEveryPluginOwnedResourceAndKeepsGlobalCatalogueMetadata()
    {
        var outcome = await PluginRemovalContractFixtures.RunAsync(
            new DestructiveRemovalAdapter(),
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<PluginRemovalFixtureOutcome.Passed>();
    }

    [Test]
    public async Task RemoveFixture_ReportsPluginOwnedResourceLeftByAdapter()
    {
        var outcome = await PluginRemovalContractFixtures.RunAsync(
            new DestructiveRemovalAdapter(PluginOwnedRemovalResource.Schedules),
            CancellationToken.None
        );

        var failure = outcome
            .ShouldBeOfType<PluginRemovalFixtureOutcome.Failed>()
            .Failures.ShouldHaveSingleItem();
        failure.Code.ShouldBe(PluginRemovalFixtureFailureCode.PluginOwnedResourcePresent);
        failure.Resource.ShouldBe(PluginOwnedRemovalResource.Schedules);
    }

    private sealed class DestructiveRemovalAdapter(PluginOwnedRemovalResource? leave = null)
        : IPluginRemovalContractFixtureAdapter
    {
        private PluginRemovalFixtureSnapshot _snapshot = new([], false);

        public ValueTask SeedAsync(
            PluginRemovalFixtureSnapshot snapshot,
            CancellationToken cancellationToken
        )
        {
            _snapshot = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(CancellationToken cancellationToken)
        {
            _snapshot = _snapshot with
            {
                PluginOwnedResources = leave is { } resource ? [resource] : [],
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask<PluginRemovalFixtureSnapshot> ObserveAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(_snapshot);
    }
}

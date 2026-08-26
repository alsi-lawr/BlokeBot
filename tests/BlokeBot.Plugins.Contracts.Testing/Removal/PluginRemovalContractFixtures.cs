using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts.Testing;

public enum PluginOwnedRemovalResource
{
    InstalledPackage,
    InstallationSettings,
    ChannelSettings,
    FeatureState,
    Configuration,
    Secrets,
    Schedules,
    PrivateData,
    AutomationDefinitions,
    AutomationLedgers,
    DependentFlows,
    DependentNodes,
    RunHistory,
    MarketplaceReceipts,
    InvocationContext,
}

public sealed record PluginRemovalFixtureSnapshot(
    ImmutableHashSet<PluginOwnedRemovalResource> PluginOwnedResources,
    bool GlobalCatalogueMetadataPresent
)
{
    public static PluginRemovalFixtureSnapshot Seeded { get; } =
        new([.. Enum.GetValues<PluginOwnedRemovalResource>()], true);
}

public interface IPluginRemovalContractFixtureAdapter
{
    ValueTask SeedAsync(PluginRemovalFixtureSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask RemoveAsync(CancellationToken cancellationToken);

    ValueTask<PluginRemovalFixtureSnapshot> ObserveAsync(CancellationToken cancellationToken);
}

public enum PluginRemovalFixtureFailureCode
{
    PluginOwnedResourcePresent,
    GlobalCatalogueMetadataMissing,
}

public sealed record PluginRemovalFixtureFailure(
    PluginRemovalFixtureFailureCode Code,
    PluginOwnedRemovalResource? Resource
);

public abstract record PluginRemovalFixtureOutcome
{
    private PluginRemovalFixtureOutcome() { }

    public sealed record Passed : PluginRemovalFixtureOutcome;

    public sealed record Failed(ImmutableArray<PluginRemovalFixtureFailure> Failures)
        : PluginRemovalFixtureOutcome;
}

public static class PluginRemovalContractFixtures
{
    public static async ValueTask<PluginRemovalFixtureOutcome> RunAsync(
        IPluginRemovalContractFixtureAdapter adapter,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(adapter);
        await adapter.SeedAsync(PluginRemovalFixtureSnapshot.Seeded, cancellationToken);
        await adapter.RemoveAsync(cancellationToken);
        var observed = await adapter.ObserveAsync(cancellationToken);
        var failures = observed
            .PluginOwnedResources.Order()
            .Select(resource => new PluginRemovalFixtureFailure(
                PluginRemovalFixtureFailureCode.PluginOwnedResourcePresent,
                resource
            ))
            .ToList();
        if (!observed.GlobalCatalogueMetadataPresent)
        {
            failures.Add(
                new(PluginRemovalFixtureFailureCode.GlobalCatalogueMetadataMissing, Resource: null)
            );
        }

        return failures.Count == 0
            ? new PluginRemovalFixtureOutcome.Passed()
            : new PluginRemovalFixtureOutcome.Failed([.. failures]);
    }
}

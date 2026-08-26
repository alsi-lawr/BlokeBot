using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginAdminApplicationService(
    PluginMarketplaceCatalogService catalog,
    PluginMarketplaceRuntimeContext runtime,
    IPluginLifecycleStore lifecycles,
    IPluginMarketplaceReceiptStore receipts,
    IPluginFeatureDeclarationProvider declarations,
    IPluginFeatureSnapshotProvider features,
    PluginMarketplaceApplicationService marketplace
) : IPluginAdminApplicationService
{
    public async ValueTask<PluginAdminLoadOutcome> LoadAsync(
        AuthenticatedSession session,
        string? catalogQuery,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsBotAdmin)
        {
            return new PluginAdminLoadOutcome.Unauthorized();
        }

        var catalogSearch = catalog.Search(session, catalogQuery);
        var catalogInventory = string.IsNullOrWhiteSpace(catalogQuery)
            ? catalogSearch
            : catalog.Search(session, null);
        var lifecycleStates = (await lifecycles.LoadAllAsync(cancellationToken))
            .Where(static state => state.Phase != PluginLifecyclePhase.Removed)
            .OrderBy(static state => state.PluginId.Value, StringComparer.Ordinal)
            .ToArray();
        var receiptTasks = lifecycleStates
            .Select(state => receipts.LoadAsync(state.PluginId, cancellationToken).AsTask())
            .ToArray();
        var installedReceipts = await Task.WhenAll(receiptTasks);

        var declarationSnapshot = declarations.Current;
        var featureSnapshot = features.Current;
        var catalogEntries = catalogInventory is PluginMarketplaceSearchOutcome.Available available
            ? available.Entries
            : [];
        var installed = lifecycleStates
            .Select(
                (state, index) =>
                    ProjectInstalled(
                        state,
                        installedReceipts[index],
                        declarationSnapshot,
                        featureSnapshot,
                        catalogEntries,
                        runtime.Target
                    )
            )
            .ToImmutableArray();
        var installedById = lifecycleStates.ToDictionary(static state => state.PluginId);
        return new PluginAdminLoadOutcome.Loaded(
            new(installed, ProjectCatalog(catalogSearch, installedById, runtime.Target))
        );
    }

    public ValueTask<PluginMarketplaceCommandOutcome> InstallAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    ) => marketplace.InstallAsync(session, pluginId, release, cancellationToken);

    public ValueTask<PluginMarketplaceCommandOutcome> UpdateAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    ) => marketplace.UpdateAsync(session, pluginId, release, cancellationToken);

    public ValueTask<PluginMarketplaceCommandOutcome> RestartAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) => marketplace.RestartAsync(session, pluginId, cancellationToken);

    public ValueTask<PluginMarketplaceCommandOutcome> RemoveAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) => marketplace.RemoveAsync(session, pluginId, cancellationToken);

    private static PluginAdminInstalledPlugin ProjectInstalled(
        PluginLifecycleState state,
        PluginMarketplaceReceipt? receipt,
        PluginFeatureDeclarationSnapshot declarations,
        PluginFeatureSnapshot features,
        ImmutableArray<PluginMarketplaceCatalogEntry> catalogEntries,
        PluginHostCompatibilityTarget target
    )
    {
        _ = declarations.Declarations.TryGetValue(state.PluginId, out var declaration);
        var featureStates = features
            .States.Values.Where(feature => feature.Key.PluginId == state.PluginId)
            .ToArray();
        var enabledChannelCount = featureStates
            .Where(static feature => feature.Enabled)
            .Select(static feature => feature.Key.HostId)
            .Distinct()
            .Count();
        var featureItems =
            declaration
                ?.Manifest.Features.OrderBy(
                    static feature => feature.Name,
                    StringComparer.OrdinalIgnoreCase
                )
                .ThenBy(static feature => feature.Id.Value, StringComparer.Ordinal)
                .Select(feature => new PluginAdminFeatureItem(
                    feature.Id,
                    feature.Name,
                    featureStates
                        .Where(state => state.Key.FeatureId == feature.Id && state.Enabled)
                        .Select(static state => state.Key.HostId)
                        .Distinct()
                        .Count()
                ))
                .ToImmutableArray()
            ?? [];
        var catalogReleases = catalogEntries
            .Where(entry =>
                entry.PluginId == state.PluginId
                && PluginMarketplaceCompatibilityPolicy.IsCompatible(entry.Compatibility, target)
            )
            .OrderByDescending(static entry => entry.Release.DeclaredVersion)
            .ThenBy(static entry => entry.Release.Tag.Value, StringComparer.Ordinal)
            .ToArray();
        var updateRelease = catalogReleases
            .Select(static entry => entry.Release)
            .FirstOrDefault(release =>
                release.DeclaredVersion.CompareTo(
                    state.SelectedInstallation.Release.DeclaredVersion
                ) > 0
                || (
                    release.DeclaredVersion == state.SelectedInstallation.Release.DeclaredVersion
                    && release.Tag != state.SelectedInstallation.Release.Tag
                )
            );
        var name =
            declaration?.Manifest.Name
            ?? catalogReleases.FirstOrDefault()?.Name
            ?? state.PluginId.Value;
        return new(
            state.PluginId,
            name,
            PluginLifecycleView.From(state),
            Status(state, featureStates),
            enabledChannelCount,
            featureItems,
            updateRelease,
            receipt
        );
    }

    private static PluginAdminInstalledStatus Status(
        PluginLifecycleState state,
        IReadOnlyList<PluginFeatureState> featureStates
    ) =>
        state.Phase switch
        {
            PluginLifecyclePhase.Faulted => PluginAdminInstalledStatus.Faulted,
            PluginLifecyclePhase.Preparing
            or PluginLifecyclePhase.Migrating
            or PluginLifecyclePhase.Activating
            or PluginLifecyclePhase.Draining
            or PluginLifecyclePhase.Removing => PluginAdminInstalledStatus.Operation,
            _ when featureStates.Any(static feature =>
                    feature.Readiness is PluginFeatureReadiness.EnabledDegraded
                ) => PluginAdminInstalledStatus.Degraded,
            _ => PluginAdminInstalledStatus.Active,
        };

    private static PluginAdminCatalog ProjectCatalog(
        PluginMarketplaceSearchOutcome search,
        IReadOnlyDictionary<PluginId, PluginLifecycleState> installed,
        PluginHostCompatibilityTarget target
    ) =>
        search switch
        {
            PluginMarketplaceSearchOutcome.Available available => new PluginAdminCatalog.Available(
                available
                    .Entries.Select(entry => new PluginAdminCatalogEntry(
                        entry,
                        PluginMarketplaceCompatibilityPolicy.IsCompatible(
                            entry.Compatibility,
                            target
                        ),
                        installed.ContainsKey(entry.PluginId),
                        installed.TryGetValue(entry.PluginId, out var current)
                            && current.SelectedInstallation.Release == entry.Release
                    ))
                    .ToImmutableArray(),
                available.RefreshedAt,
                available.Age,
                available.RefreshFailure
            )
            {
                RefreshInProgress = available.RefreshInProgress,
            },
            PluginMarketplaceSearchOutcome.Unavailable unavailable =>
                new PluginAdminCatalog.Unavailable(
                    unavailable.LastAttemptAt,
                    unavailable.RefreshFailure
                )
                {
                    RefreshInProgress = unavailable.RefreshInProgress,
                },
            PluginMarketplaceSearchOutcome.Unauthorized => throw new InvalidOperationException(
                "An authorized plugin Admin query returned an unauthorized catalogue result."
            ),
            _ => throw new InvalidOperationException("Unknown plugin catalogue result."),
        };
}

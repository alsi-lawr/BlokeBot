using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Simulation;

internal sealed class SimulationPluginAdminScenario : IPluginAdminApplicationService
{
    private static readonly DateTimeOffset _now = SimulationMode.Now;
    private string _state = "installed";

    internal bool SetState(string state)
    {
        if (state is not ("installed" or "fault" or "no-snapshot" or "removal-confirmation"))
        {
            return false;
        }

        Volatile.Write(ref _state, state);
        return true;
    }

    public ValueTask<PluginAdminLoadOutcome> LoadAsync(
        AuthenticatedSession session,
        string? catalogQuery,
        CancellationToken cancellationToken
    )
    {
        if (!session.IsBotAdmin)
        {
            return ValueTask.FromResult<PluginAdminLoadOutcome>(
                new PluginAdminLoadOutcome.Unauthorized()
            );
        }

        var snapshot = Volatile.Read(ref _state) switch
        {
            "fault" => FaultSnapshot(),
            "no-snapshot" => NoSnapshot(),
            _ => InstalledSnapshot(),
        };
        return ValueTask.FromResult<PluginAdminLoadOutcome>(
            new PluginAdminLoadOutcome.Loaded(Filter(snapshot, catalogQuery))
        );
    }

    public ValueTask<PluginMarketplaceCommandOutcome> InstallAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    ) => Rejected();

    public ValueTask<PluginMarketplaceCommandOutcome> UpdateAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    ) => Rejected();

    public ValueTask<PluginMarketplaceCommandOutcome> RestartAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) => Rejected();

    public ValueTask<PluginMarketplaceCommandOutcome> RemoveAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) => Rejected();

    private static ValueTask<PluginMarketplaceCommandOutcome> Rejected() =>
        ValueTask.FromResult<PluginMarketplaceCommandOutcome>(
            new PluginMarketplaceCommandOutcome.Rejected(
                PluginMarketplaceCommandRejectionCode.LifecycleRejected,
                null
            )
            {
                LifecycleRejection = PluginLifecycleCommandRejectionCode.Busy,
            }
        );

    private static PluginAdminSnapshot InstalledSnapshot()
    {
        var linkQueue = Installed(
            "community.link-queue",
            "Link queue",
            PluginAdminInstalledStatus.Active,
            PluginLifecyclePhase.Active,
            Release("1.1.0", "link-queue-v1"),
            Release("1.2.0", "link-queue-current"),
            3,
            [Feature("collection", "Link collection", 3), Feature("review", "Review queue", 2)],
            new(
                Id("community.link-queue"),
                PluginMarketplaceOperationKind.Update,
                Release("1.1.0", "link-queue-v1"),
                "Activated",
                null,
                _now.AddMinutes(-18)
            )
        );
        var alerts = Installed(
            "community.alerts",
            "Community alerts",
            PluginAdminInstalledStatus.Degraded,
            PluginLifecyclePhase.Active,
            Release("2.0.0", "community-alerts"),
            null,
            1,
            [Feature("announcements", "Announcements", 1)],
            new(
                Id("community.alerts"),
                PluginMarketplaceOperationKind.Restart,
                Release("2.0.0", "community-alerts"),
                "Restarted",
                null,
                _now.AddHours(-2)
            )
        );
        return new(
            [linkQueue, alerts],
            new PluginAdminCatalog.Available(
                [
                    Catalog(
                        "community.link-queue",
                        "Link queue",
                        "Queue and review links from chat.",
                        "Community Tools",
                        Release("1.2.0", "link-queue-current"),
                        compatible: true,
                        installed: true
                    ),
                    Catalog(
                        "community.sound-scenes",
                        "Sound scenes",
                        "Build reusable audio scenes for channel events.",
                        "Sound Workshop",
                        Release("0.8.0", "sound-scenes-beta"),
                        compatible: false,
                        installed: false
                    ),
                ],
                _now.AddHours(-3),
                TimeSpan.FromHours(3),
                PluginMarketplaceRefreshFailureCode.DownloadFailed
            )
            {
                RefreshInProgress = true,
            }
        );
    }

    private static PluginAdminSnapshot FaultSnapshot()
    {
        _ = PluginLifecycleSafeDetail.TryCreate(
            "The worker stopped after the restart limit.",
            out var detail
        );
        var fault = Installed(
            "community.link-queue",
            "Link queue",
            PluginAdminInstalledStatus.Faulted,
            PluginLifecyclePhase.Faulted,
            Release("1.2.0", "link-queue-current"),
            Release("1.2.0", "link-queue-current"),
            0,
            [],
            new(
                Id("community.link-queue"),
                PluginMarketplaceOperationKind.Restart,
                Release("1.2.0", "link-queue-current"),
                "WorkerExited",
                detail.Value,
                _now.AddMinutes(-4)
            ),
            PluginLifecycleOutcome.Failure(
                PluginLifecycleFailureCode.WorkerExited,
                detail,
                _now.AddMinutes(-4)
            )
        );
        return new(
            [fault],
            new PluginAdminCatalog.Available(
                [
                    Catalog(
                        "community.link-queue",
                        "Link queue",
                        "Queue and review links from chat.",
                        "Community Tools",
                        Release("1.2.0", "link-queue-current"),
                        compatible: true,
                        installed: true
                    ),
                ],
                _now.AddMinutes(-45),
                TimeSpan.FromMinutes(45),
                null
            )
        );
    }

    private static PluginAdminSnapshot NoSnapshot() =>
        new(
            [],
            new PluginAdminCatalog.Unavailable(
                _now.AddMinutes(-6),
                PluginMarketplaceRefreshFailureCode.DownloadFailed
            )
        );

    private static PluginAdminSnapshot Filter(PluginAdminSnapshot snapshot, string? query)
    {
        var normalized = query?.Trim();
        return (normalized, snapshot.Catalog) switch
        {
            ({ Length: > 0 } search, PluginAdminCatalog.Available available) => snapshot with
            {
                Catalog = available with
                {
                    Entries = available
                        .Entries.Where(entry =>
                            entry.Entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || entry.Entry.Summary.Contains(
                                search,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || entry.Entry.Author.Contains(
                                search,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || entry.Entry.Tags.Any(tag =>
                                tag.Contains(search, StringComparison.OrdinalIgnoreCase)
                            )
                        )
                        .ToImmutableArray(),
                },
            },
            _ => snapshot,
        };
    }

    private static PluginAdminInstalledPlugin Installed(
        string id,
        string name,
        PluginAdminInstalledStatus status,
        PluginLifecyclePhase phase,
        PluginReleaseIdentity release,
        PluginReleaseIdentity? update,
        int enabledChannels,
        ImmutableArray<PluginAdminFeatureItem> features,
        PluginMarketplaceReceipt receipt,
        PluginLifecycleOutcome? outcome = null
    )
    {
        _ = PluginWorkerGeneration.TryCreate(3, out var generation);
        return new(
            Id(id),
            name,
            new(
                new(Id(id), release),
                phase,
                PluginLifecycleOperationId.New(),
                generation,
                outcome
                    ?? PluginLifecycleOutcome.Progress(
                        PluginLifecycleOutcomeCode.Activated,
                        _now.AddHours(-1)
                    ),
                status == PluginAdminInstalledStatus.Faulted
            ),
            status,
            enabledChannels,
            features,
            update,
            receipt
        );
    }

    private static PluginAdminCatalogEntry Catalog(
        string id,
        string name,
        string summary,
        string author,
        PluginReleaseIdentity release,
        bool compatible,
        bool installed
    )
    {
        var pluginId = Id(id);
        return new(
            new(
                pluginId,
                name,
                summary,
                author,
                ["chat", "workflow"],
                null,
                [],
                PluginMarketplaceRepositoryAuthority.RepositoryUrl,
                PluginMarketplaceRepositoryAuthority.PackagePath(pluginId),
                release,
                SimulationPluginFeatureManifest.Load().Manifest.Compatibility
            ),
            compatible,
            installed,
            installed
        );
    }

    private static PluginAdminFeatureItem Feature(string id, string name, int enabledChannels)
    {
        _ = PluginFeatureId.TryCreate(id, out var featureId);
        return new(featureId, name, enabledChannels);
    }

    private static PluginId Id(string value)
    {
        _ = PluginId.TryCreate(value, out var id);
        return id;
    }

    private static PluginReleaseIdentity Release(string versionValue, string tagValue)
    {
        _ = SemanticVersion.TryCreate(versionValue, out var version);
        _ = PluginGitTag.TryCreate(tagValue, out var tag);
        return new(version, tag);
    }
}

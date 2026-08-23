using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator : IPluginLifecycleCoordinator
{
    private readonly IPluginLifecycleStore _store;
    private readonly IPluginLifecyclePackageResolver _packages;
    private readonly IReadOnlyList<IPluginMigrationDataOwner> _migrationOwners;
    private readonly IReadOnlyList<IPluginPurgeDataOwner> _purgeOwners;
    private readonly IPluginPendingWorkCanceller _pendingWork;
    private readonly IPluginLifecycleWorkerManager _workers;
    private readonly PluginRuntimeSnapshotRegistry _snapshots;
    private readonly PluginLifecycleSerialization _serialization;
    private readonly PluginLifecycleOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PluginLifecycleCoordinator> _logger;

    internal PluginLifecycleCoordinator(
        IPluginLifecycleStore store,
        IPluginLifecyclePackageResolver packages,
        IEnumerable<IPluginMigrationDataOwner> migrationOwners,
        IEnumerable<IPluginPurgeDataOwner> purgeOwners,
        IPluginPendingWorkCanceller pendingWork,
        IPluginLifecycleWorkerManager workers,
        PluginRuntimeSnapshotRegistry snapshots,
        PluginLifecycleSerialization serialization,
        PluginLifecycleOptions options,
        TimeProvider timeProvider,
        ILogger<PluginLifecycleCoordinator> logger
    )
    {
        _store = store;
        _packages = packages;
        _migrationOwners = migrationOwners.ToArray();
        _purgeOwners = purgeOwners.ToArray();
        _pendingWork = pendingWork;
        _workers = workers;
        _snapshots = snapshots;
        _serialization = serialization;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<PluginLifecycleCommandOutcome> ActivateAsync(
        PluginLifecycleOperationId operationId,
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        if (!package.MatchesIdentity)
        {
            return new PluginLifecycleCommandOutcome.Rejected(
                PluginLifecycleCommandRejectionCode.InvalidPackageIdentity,
                null
            );
        }

        await using var lease = await _serialization.AcquireAsync(
            package.Installation.PluginId,
            cancellationToken
        );
        var begun = await _store.BeginActivationAsync(
            new(package.Installation, operationId, Now()),
            cancellationToken
        );
        return begun is PluginLifecycleStoreBeginOutcome.Rejected rejected
            ? Rejected(rejected.Code, rejected.Current)
            : await PrepareAndActivateAsync(
                ((PluginLifecycleStoreBeginOutcome.Begun)begun).State,
                package,
                cancellationToken
            );
    }

    public ValueTask<PluginLifecycleCommandOutcome> RemoveAsync(
        PluginId pluginId,
        PluginLifecycleOperationId operationId,
        CancellationToken cancellationToken
    ) => RemoveOrPurgeAsync(pluginId, operationId, purge: false, cancellationToken);

    public ValueTask<PluginLifecycleCommandOutcome> PurgeAsync(
        PluginId pluginId,
        PluginLifecycleOperationId operationId,
        CancellationToken cancellationToken
    ) => RemoveOrPurgeAsync(pluginId, operationId, purge: true, cancellationToken);

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private static PluginLifecycleCommandOutcome Rejected(
        PluginLifecycleTransitionFailureCode code,
        PluginLifecycleState? current
    ) =>
        new PluginLifecycleCommandOutcome.Rejected(
            code switch
            {
                PluginLifecycleTransitionFailureCode.Busy =>
                    PluginLifecycleCommandRejectionCode.Busy,
                PluginLifecycleTransitionFailureCode.AlreadyActive =>
                    PluginLifecycleCommandRejectionCode.AlreadyActive,
                PluginLifecycleTransitionFailureCode.FaultedInstallation =>
                    PluginLifecycleCommandRejectionCode.FaultedInstallation,
                PluginLifecycleTransitionFailureCode.NotFound =>
                    PluginLifecycleCommandRejectionCode.NotFound,
                PluginLifecycleTransitionFailureCode.NotFaulted =>
                    PluginLifecycleCommandRejectionCode.NotFaulted,
                PluginLifecycleTransitionFailureCode.InvalidTransition =>
                    PluginLifecycleCommandRejectionCode.Conflict,
                PluginLifecycleTransitionFailureCode.GenerationExhausted =>
                    PluginLifecycleCommandRejectionCode.GenerationExhausted,
            },
            current is null ? null : PluginLifecycleView.From(current)
        );

    private static PluginLifecycleCommandOutcome Conflict(PluginLifecycleState? current) =>
        new PluginLifecycleCommandOutcome.Rejected(
            PluginLifecycleCommandRejectionCode.Conflict,
            current is null ? null : PluginLifecycleView.From(current)
        );

    private static PluginLifecycleCommandOutcome Succeeded(PluginLifecycleState state) =>
        new PluginLifecycleCommandOutcome.Succeeded(PluginLifecycleView.From(state));

    private static PluginLifecycleCommandOutcome Failed(PluginLifecycleState state) =>
        new PluginLifecycleCommandOutcome.Failed(PluginLifecycleView.From(state));

    private static PluginLifecycleCommandOutcome Purged(PluginLifecycleTombstone tombstone) =>
        new PluginLifecycleCommandOutcome.Purged(tombstone);
}

using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginLifecycleBeginRequest(
    PluginInstallationIdentity Installation,
    PluginLifecycleOperationId OperationId,
    DateTimeOffset OccurredAtUtc
);

public abstract record PluginLifecycleStoreBeginOutcome
{
    private PluginLifecycleStoreBeginOutcome() { }

    public sealed record Begun(PluginLifecycleState State) : PluginLifecycleStoreBeginOutcome;

    public sealed record Rejected(
        PluginLifecycleTransitionFailureCode Code,
        PluginLifecycleState? Current
    ) : PluginLifecycleStoreBeginOutcome;
}

public abstract record PluginLifecycleStoreWriteOutcome
{
    private PluginLifecycleStoreWriteOutcome() { }

    public sealed record Written(PluginLifecycleState State) : PluginLifecycleStoreWriteOutcome;

    public sealed record Conflict(PluginLifecycleState? Current) : PluginLifecycleStoreWriteOutcome;
}

public abstract record PluginLifecycleStorePurgeOutcome
{
    private PluginLifecycleStorePurgeOutcome() { }

    public sealed record Completed(PluginLifecycleTombstone Tombstone)
        : PluginLifecycleStorePurgeOutcome;

    public sealed record Conflict(PluginLifecycleState? Current) : PluginLifecycleStorePurgeOutcome;
}

public interface IPluginLifecycleStore
{
    ValueTask<PluginLifecycleState?> LoadAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    );

    ValueTask<IReadOnlyList<PluginLifecycleState>> LoadAllAsync(
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleTombstone?> LoadTombstoneAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleStoreBeginOutcome> BeginActivationAsync(
        PluginLifecycleBeginRequest request,
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleStoreWriteOutcome> WriteAsync(
        PluginLifecycleState expected,
        PluginLifecycleState next,
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleStorePurgeOutcome> CompletePurgeAsync(
        PluginLifecycleState expected,
        PluginLifecycleOutcome outcome,
        CancellationToken cancellationToken
    );
}

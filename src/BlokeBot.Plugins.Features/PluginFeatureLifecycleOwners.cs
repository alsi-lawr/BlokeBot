using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed class PluginFeatureRemovalOwner(
    IPluginFeatureStore store,
    PluginFeatureSnapshotRegistry snapshots,
    IPluginFeatureDeclarationPublisher declarations
) : IPluginRemovalDataOwner
{
    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        await store.RemovePluginDataAsync(context.PluginId, cancellationToken);
        snapshots.Remove(context.PluginId);
        declarations.Remove(context.PluginId, context.Fence);
        return new PluginLifecycleOwnerOutcome.Succeeded();
    }
}

public sealed class PluginFeaturePendingWorkCanceller(
    IPluginFeatureStore store,
    IPluginFeatureReconciler reconciler,
    IPluginFeatureWorkCoordinator? work = null
) : IPluginPendingWorkCanceller
{
    public async ValueTask<PluginLifecycleOwnerOutcome> CancelAsync(
        PluginId pluginId,
        PluginLifecycleFence fence,
        CancellationToken cancellationToken
    )
    {
        if (work is not null)
        {
            await work.CancelAndDrainPluginAsync(pluginId, cancellationToken);
        }
        var states = await store.LoadFeatureStatesAsync(pluginId, cancellationToken);
        foreach (var state in states.Where(state => state.Enabled && state.Fence == fence))
        {
            await reconciler.CancelAsync(
                state.Key,
                state.Fence,
                state.Generation,
                cancellationToken
            );
        }
        return new PluginLifecycleOwnerOutcome.Succeeded();
    }
}

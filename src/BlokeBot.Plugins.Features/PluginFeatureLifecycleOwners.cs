using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed class PluginFeatureRemovalOwner(
    IPluginFeatureStore store,
    PluginFeatureSnapshotRegistry snapshots
) : IPluginRemovalDataOwner
{
    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        await store.RemovePluginDataAsync(context.PluginId, cancellationToken);
        snapshots.Remove(context.PluginId);
        return new PluginLifecycleOwnerOutcome.Succeeded();
    }
}

public sealed class PluginFeatureActivationPublisher(
    IPluginFeatureDeclarationPublisher declarations
) : IPluginLifecycleActivationPublisher
{
    public ValueTask<PluginLifecycleOwnerOutcome> PublishAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = context.Package.PreparedPackage.Manifest;
        if (
            manifest is null
            || context.Package.Installation != context.Installation
            || manifest.Manifest.Id != context.Installation.PluginId
            || manifest.Manifest.Release != context.Installation.Release
        )
        {
            return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Rejected,
                    InvalidDeclarationDetail()
                )
            );
        }

        declarations.Publish(manifest, context.Fence);
        return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
            new PluginLifecycleOwnerOutcome.Succeeded()
        );
    }

    public ValueTask WithdrawAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        declarations.Remove(context.Installation.PluginId, context.Fence);
        return ValueTask.CompletedTask;
    }

    private static PluginLifecycleSafeDetail InvalidDeclarationDetail() =>
        PluginLifecycleSafeDetail.TryCreate(
            "The validated plugin declaration does not match the selected installation.",
            out var detail
        )
            ? detail
            : throw new InvalidOperationException("Invalid declaration failure detail.");
}

public sealed class PluginFeaturePendingWorkCanceller(
    IPluginFeatureStore store,
    IPluginFeatureReconciler reconciler,
    IPluginFeatureDeclarationPublisher declarations,
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
        declarations.Remove(pluginId, fence);
        return new PluginLifecycleOwnerOutcome.Succeeded();
    }
}

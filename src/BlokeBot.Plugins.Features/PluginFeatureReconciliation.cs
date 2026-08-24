using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginFeatureReconciliationRequest(
    PluginFeatureKey Key,
    PluginLifecycleFence Fence,
    PluginFeatureGeneration Generation,
    PluginTwitchRequirements Requirements
);

public abstract record PluginFeatureReconciliationResult
{
    private PluginFeatureReconciliationResult() { }

    public sealed record Ready : PluginFeatureReconciliationResult;

    public sealed record MissingScopes(ImmutableArray<string> Scopes)
        : PluginFeatureReconciliationResult;

    public sealed record Pending : PluginFeatureReconciliationResult;

    public sealed record Failed(PluginReadinessReason Reason) : PluginFeatureReconciliationResult;
}

public interface IPluginFeatureReconciler
{
    ValueTask<PluginFeatureReconciliationResult> ReconcileAsync(
        PluginFeatureReconciliationRequest request,
        CancellationToken cancellationToken
    );

    ValueTask CancelAsync(
        PluginFeatureKey key,
        PluginLifecycleFence fence,
        PluginFeatureGeneration generation,
        CancellationToken cancellationToken
    );
}

public sealed class EmptyPluginFeatureReconciler : IPluginFeatureReconciler
{
    public ValueTask<PluginFeatureReconciliationResult> ReconcileAsync(
        PluginFeatureReconciliationRequest request,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginFeatureReconciliationResult>(
            request.Requirements.Scopes.IsEmpty && request.Requirements.EventSubTypes.IsEmpty
                ? new PluginFeatureReconciliationResult.Ready()
                : new PluginFeatureReconciliationResult.Pending()
        );

    public ValueTask CancelAsync(
        PluginFeatureKey key,
        PluginLifecycleFence fence,
        PluginFeatureGeneration generation,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;
}

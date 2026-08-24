namespace BlokeBot.Plugins.Features;

public sealed partial class PluginFeatureManager
{
    public async ValueTask<PluginFeatureReconciliationApplyOutcome> ApplyReconciliationAsync(
        PluginFeatureReconciliationRequest request,
        PluginFeatureReconciliationResult result,
        CancellationToken cancellationToken
    )
    {
        await using var lease = await lifecycleSerialization.AcquireAsync(
            request.Key.PluginId,
            cancellationToken
        );
        return await ApplyReconciliationCoreAsync(request, result, cancellationToken);
    }

    private async ValueTask<PluginFeatureReconciliationApplyOutcome> ApplyReconciliationCoreAsync(
        PluginFeatureReconciliationRequest request,
        PluginFeatureReconciliationResult result,
        CancellationToken cancellationToken
    )
    {
        var current = await store.LoadFeatureStateAsync(request.Key, cancellationToken);
        var declaration = FindDeclaration(request.Key.PluginId);
        if (
            current is null
            || !current.Enabled
            || current.Fence != request.Fence
            || current.Generation != request.Generation
            || declaration is null
            || declaration.Fence != request.Fence
            || !lifecycleHealth.IsHealthy(declaration)
        )
        {
            return new PluginFeatureReconciliationApplyOutcome.Ignored(current);
        }

        var readiness = Readiness(result);
        if (current.Readiness == readiness)
        {
            return new PluginFeatureReconciliationApplyOutcome.Applied(current);
        }
        var next = current with
        {
            Readiness = readiness,
            Revision = NextRevision(current.Revision),
        };
        var written = await store.WriteFeatureStateAsync(current, next, cancellationToken);
        if (written is PluginFeatureStateStoreWriteOutcome.Conflict conflict)
        {
            return new PluginFeatureReconciliationApplyOutcome.Conflict(conflict.Current);
        }

        next = ((PluginFeatureStateStoreWriteOutcome.Written)written).State;
        snapshots.Publish(next);
        return new PluginFeatureReconciliationApplyOutcome.Applied(next);
    }

    private static PluginFeatureReadiness Readiness(PluginFeatureReconciliationResult result) =>
        result switch
        {
            PluginFeatureReconciliationResult.Ready => new PluginFeatureReadiness.Ready(),
            PluginFeatureReconciliationResult.MissingScopes missing =>
                new PluginFeatureReadiness.EnabledDegraded(MissingScopesReason(missing)),
            PluginFeatureReconciliationResult.Pending => new PluginFeatureReadiness.EnabledDegraded(
                PendingReason()
            ),
            PluginFeatureReconciliationResult.Failed failed =>
                new PluginFeatureReadiness.EnabledDegraded(failed.Reason),
            _ => throw new InvalidOperationException("Unknown reconciliation result."),
        };

    private static PluginReadinessReason MissingScopesReason(
        PluginFeatureReconciliationResult.MissingScopes missing
    )
    {
        var detail =
            missing.Scopes.Length == 1
                ? $"Reconnect Twitch to add {missing.Scopes[0]}."
                : "Reconnect Twitch to add the required permissions.";
        return PluginReadinessReason.TryCreate(
            PluginReadinessReasonCode.MissingScopes,
            PluginRecoveryAction.ReconnectTwitch,
            detail,
            out var reason
        )
            ? reason
            : throw new InvalidOperationException("Invalid missing-scope readiness reason.");
    }
}

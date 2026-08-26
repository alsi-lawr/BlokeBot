using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginFeatureManager
{
    public async ValueTask<IReadOnlyList<PluginFeatureEnableOutcome>> SynchronizeDeclarationAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var outcomes = new List<PluginFeatureEnableOutcome>();
        var states = await store.LoadFeatureStatesAsync(pluginId, cancellationToken);
        foreach (var state in states.Where(static state => state.Enabled))
        {
            var declaration = FindDeclaration(pluginId);
            if (declaration is not null && state.Fence == declaration.Fence)
            {
                outcomes.Add(
                    await RetryAsync(state.Key, cancellationToken) switch
                    {
                        PluginFeatureReconciliationApplyOutcome.Applied applied =>
                            new PluginFeatureEnableOutcome.Enabled(applied.State),
                        PluginFeatureReconciliationApplyOutcome.Ignored ignored =>
                            new PluginFeatureEnableOutcome.Superseded(ignored.Current),
                        PluginFeatureReconciliationApplyOutcome.Conflict conflict =>
                            new PluginFeatureEnableOutcome.Superseded(conflict.Current),
                        _ => throw new InvalidOperationException("Unknown reconciliation outcome."),
                    }
                );
            }
            else
            {
                outcomes.Add(await EnableAsync(state.Key, cancellationToken));
            }
        }
        return outcomes.AsReadOnly();
    }

    public async ValueTask<PluginFeatureEnableOutcome> EnableAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    )
    {
        PluginFeatureEnableCommitOutcome commit;
        await using (await lifecycleSerialization.AcquireAsync(key.PluginId, cancellationToken))
        {
            commit = await CommitEnableAsync(key, cancellationToken);
        }
        if (commit is PluginFeatureEnableCommitOutcome.Completed completed)
        {
            return completed.Outcome;
        }

        var enabled = (PluginFeatureEnableCommitOutcome.Committed)commit;
        var request = new PluginFeatureReconciliationRequest(
            key,
            enabled.State.Fence,
            enabled.State.Generation,
            enabled.Requirements
        );
        var reconciliation = await reconciler.ReconcileAsync(request, cancellationToken);
        return await ApplyReconciliationAsync(request, reconciliation, cancellationToken) switch
        {
            PluginFeatureReconciliationApplyOutcome.Applied applied =>
                new PluginFeatureEnableOutcome.Enabled(applied.State),
            PluginFeatureReconciliationApplyOutcome.Ignored ignored =>
                new PluginFeatureEnableOutcome.Superseded(ignored.Current),
            PluginFeatureReconciliationApplyOutcome.Conflict reconciliationConflict =>
                new PluginFeatureEnableOutcome.Superseded(reconciliationConflict.Current),
            _ => throw new InvalidOperationException("Unknown reconciliation outcome."),
        };
    }

    private async ValueTask<PluginFeatureEnableCommitOutcome> CommitEnableAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    )
    {
        var declaration = FindDeclaration(key.PluginId);
        var feature = declaration?.FindFeature(key.FeatureId);
        if (declaration is null || feature is null)
        {
            return Completed(Rejected(PluginFeatureEnableRejectionCode.NotDeclared));
        }

        var installationOwner = new PluginConfigurationOwner.Installation(key.PluginId);
        var featureOwner = new PluginConfigurationOwner.Feature(key);
        var installation = await store.LoadConfigurationAsync(installationOwner, cancellationToken);
        var featureConfiguration = await store.LoadConfigurationAsync(
            featureOwner,
            cancellationToken
        );
        var relevant = RelevantDeclaration(declaration, feature);
        var issues = EnableSettingIssues(relevant, installation, featureConfiguration);
        if (issues.Count > 0)
        {
            return Completed(
                new PluginFeatureEnableOutcome.Rejected(
                    PluginFeatureEnableRejectionCode.InvalidSettings,
                    issues
                )
            );
        }

        var current = await store.LoadFeatureStateAsync(key, cancellationToken);
        if (current is { Enabled: true } && current.Fence == declaration.Fence)
        {
            return Completed(new PluginFeatureEnableOutcome.AlreadyEnabled(current));
        }
        var currentDeclaration = FindDeclaration(key.PluginId);
        if (
            currentDeclaration != declaration
            || currentDeclaration?.FindFeature(key.FeatureId) != feature
        )
        {
            return Completed(new PluginFeatureEnableOutcome.Superseded(current));
        }
        if (!lifecycleHealth.IsHealthy(declaration))
        {
            return Completed(Rejected(PluginFeatureEnableRejectionCode.LifecycleNotHealthy));
        }
        if (
            dependencies.Check(declaration.Manifest.HostModules)
            is PluginCoreDependencyStatus.Missing
        )
        {
            return Completed(Rejected(PluginFeatureEnableRejectionCode.MissingCoreDependency));
        }
        var reservationOutcome = commandActivation?.Reserve(key, feature);
        if (reservationOutcome is PluginCommandActivationReservationOutcome.Rejected)
        {
            return Completed(Rejected(PluginFeatureEnableRejectionCode.CommandRouteCollision));
        }
        await using var commandReservation = reservationOutcome
            is PluginCommandActivationReservationOutcome.Reserved reserved
            ? reserved.Reservation
            : null;
        if (!PluginFeatureGeneration.TryNext(current?.Generation, out var generation))
        {
            return Completed(Rejected(PluginFeatureEnableRejectionCode.GenerationExhausted));
        }
        var next = new PluginFeatureState(
            key,
            declaration.Fence,
            generation,
            new PluginFeatureReadiness.EnabledDegraded(PendingReason()),
            NextRevision(current?.Revision)
        );
        var automationPlan = automations?.Prepare(declaration, feature, next, Guid.NewGuid());
        if (automationPlan is PluginAutomationPlanOutcome.Rejected)
        {
            return Completed(Rejected(PluginFeatureEnableRejectionCode.AutomationInvalid));
        }
        var committed = await store.EnableAsync(
            new(
                current,
                next,
                installation.Revision,
                featureConfiguration.Revision,
                (automationPlan as PluginAutomationPlanOutcome.Prepared)?.Plan
            ),
            cancellationToken
        );
        if (committed is PluginFeatureEnableStoreOutcome.Conflict conflict)
        {
            return
                conflict.Code
                    is PluginFeatureEnableConflictCode.AutomationName
                        or PluginFeatureEnableConflictCode.AutomationProvenance
                ? Completed(Rejected(PluginFeatureEnableRejectionCode.AutomationConflict))
                : Completed(new PluginFeatureEnableOutcome.Superseded(conflict.Current));
        }

        next = ((PluginFeatureEnableStoreOutcome.Enabled)committed).State;
        snapshots.Publish(next);
        return new PluginFeatureEnableCommitOutcome.Committed(next, feature.Twitch);
    }

    public async ValueTask<PluginFeatureDisableOutcome> DisableAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    )
    {
        PluginFeatureState? cancelled = null;
        PluginFeatureDisableOutcome outcome;
        await using (await lifecycleSerialization.AcquireAsync(key.PluginId, cancellationToken))
        {
            (outcome, cancelled) = await CommitDisableAsync(key, cancellationToken);
        }
        if (cancelled is not null)
        {
            await reconciler.CancelAsync(
                key,
                cancelled.Fence,
                cancelled.Generation,
                CancellationToken.None
            );
        }
        return outcome;
    }

    private async ValueTask<(
        PluginFeatureDisableOutcome Outcome,
        PluginFeatureState? Cancelled
    )> CommitDisableAsync(PluginFeatureKey key, CancellationToken cancellationToken)
    {
        var current = await store.LoadFeatureStateAsync(key, cancellationToken);
        if (current is null || !current.Enabled)
        {
            return (new PluginFeatureDisableOutcome.AlreadyDisabled(current), null);
        }
        if (work is not null)
        {
            await work.CancelAndDrainAsync(current, cancellationToken);
        }
        if (!PluginFeatureGeneration.TryNext(current.Generation, out var generation))
        {
            work?.Resume(current);
            return (new PluginFeatureDisableOutcome.GenerationExhausted(), null);
        }

        var next = current with
        {
            Generation = generation,
            Readiness = new PluginFeatureReadiness.Disabled(),
            Revision = NextRevision(current.Revision),
        };
        var written = await store.WriteFeatureStateAsync(current, next, cancellationToken);
        if (written is PluginFeatureStateStoreWriteOutcome.Conflict conflict)
        {
            work?.Resume(current);
            return (new PluginFeatureDisableOutcome.Conflict(conflict.Current), null);
        }

        next = ((PluginFeatureStateStoreWriteOutcome.Written)written).State;
        snapshots.Publish(next);
        return (new PluginFeatureDisableOutcome.Disabled(next), current);
    }

    public async ValueTask<PluginFeatureReconciliationApplyOutcome> RetryAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    )
    {
        PluginFeatureReconciliationRequest? request;
        PluginFeatureState? current;
        await using (await lifecycleSerialization.AcquireAsync(key.PluginId, cancellationToken))
        {
            current = await store.LoadFeatureStateAsync(key, cancellationToken);
            var declaration = FindDeclaration(key.PluginId);
            var feature = declaration?.FindFeature(key.FeatureId);
            request =
                current is null
                || !current.Enabled
                || declaration is null
                || feature is null
                || current.Fence != declaration.Fence
                || !lifecycleHealth.IsHealthy(declaration)
                    ? null
                    : new(key, current.Fence, current.Generation, feature.Twitch);
        }
        if (request is null)
        {
            return new PluginFeatureReconciliationApplyOutcome.Ignored(current);
        }
        var result = await reconciler.ReconcileAsync(request, cancellationToken);
        return await ApplyReconciliationAsync(request, result, cancellationToken);
    }

    private static PluginFeatureEnableOutcome.Rejected Rejected(
        PluginFeatureEnableRejectionCode code
    ) => new(code, []);

    private static PluginFeatureEnableCommitOutcome.Completed Completed(
        PluginFeatureEnableOutcome outcome
    ) => new(outcome);

    private abstract record PluginFeatureEnableCommitOutcome
    {
        private PluginFeatureEnableCommitOutcome() { }

        internal sealed record Completed(PluginFeatureEnableOutcome Outcome)
            : PluginFeatureEnableCommitOutcome;

        internal sealed record Committed(
            PluginFeatureState State,
            PluginTwitchRequirements Requirements
        ) : PluginFeatureEnableCommitOutcome;
    }
}

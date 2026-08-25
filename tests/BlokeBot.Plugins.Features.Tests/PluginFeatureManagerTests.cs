using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginFeatureManagerTests
{
    [Test]
    public async Task Save_WhenLifecycleFenceIsNoLongerCurrent_DoesNotRecreateConfiguration()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var manager = context.Manager(
            new RemovedLifecycle(),
            new AvailableDependencies(),
            new QueueReconciler()
        );
        var owner = new PluginConfigurationOwner.Installation(
            PluginFeatureTestContext.Key("collection").PluginId
        );
        var loaded = (
            await manager.LoadConfigurationAsync(owner, CancellationToken.None)
        ).ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();

        _ = (
            await manager.SaveConfigurationAsync(
                new(
                    owner,
                    loaded.Configuration.Revision,
                    PluginFeatureTestContext.InstallationValues(),
                    []
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.NotDeclared>();
        (
            await context.Store.LoadConfigurationAsync(owner, CancellationToken.None)
        ).Revision.ShouldBe(PluginConfigurationRevision.Initial);
    }

    [Test]
    public async Task Save_InvalidConfigurationWithSecretReplacement_DoesNotCommitEither()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var manager = context.Manager(
            new HealthyLifecycle(),
            new AvailableDependencies(),
            new QueueReconciler()
        );
        var installationOwner = new PluginConfigurationOwner.Installation(
            PluginFeatureTestContext.Key("collection").PluginId
        );
        var installation = (
            await manager.LoadConfigurationAsync(installationOwner, CancellationToken.None)
        ).ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();
        PluginSettingChoiceId.TryCreate("unknown", out var invalidChoice).ShouldBeTrue();
        var invalid = await manager.SaveConfigurationAsync(
            new(
                installationOwner,
                installation.Configuration.Revision,
                Values(
                    PluginFeatureTestContext.Entry(
                        "moderation-mode",
                        new PluginSettingValue.Choice(invalidChoice)
                    )
                ),
                [PluginFeatureTestContext.SecretReplacement("raw-secret")]
            ),
            CancellationToken.None
        );

        var issues = invalid.ShouldBeOfType<PluginConfigurationSaveOutcome.Invalid>().Issues;
        issues.ShouldContain(issue => issue.Code == PluginSettingValidationCode.InvalidChoice);
        var unchangedInstallation = await context.Store.LoadConfigurationAsync(
            installationOwner,
            CancellationToken.None
        );
        unchangedInstallation.Revision.ShouldBe(PluginConfigurationRevision.Initial);
        unchangedInstallation.Secrets.ShouldBeEmpty();
    }

    [Test]
    public async Task Enable_RejectsInvalidSettingsDependenciesAndLifecycleBeforeStateCommit()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var reconciler = new QueueReconciler(new PluginFeatureReconciliationResult.Ready());
        var manager = context.Manager(
            new HealthyLifecycle(),
            new AvailableDependencies(),
            reconciler
        );
        await SaveInstallationAsync(manager);
        var invalidSave = await SaveFeatureAsync(
            manager,
            PluginFeatureTestContext.Key("collection"),
            PluginFeatureTestContext.CollectionValues(maximumLinks: 0)
        );

        _ = invalidSave.ShouldBeOfType<PluginConfigurationSaveOutcome.Invalid>();
        (
            await context.Store.LoadFeatureStateAsync(
                PluginFeatureTestContext.Key("collection"),
                CancellationToken.None
            )
        ).ShouldBeNull();

        _ = (
            await SaveFeatureAsync(
                manager,
                PluginFeatureTestContext.Key("collection"),
                PluginFeatureTestContext.CollectionValues()
            )
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        var missingDependency = await context
            .Manager(new HealthyLifecycle(), new MissingDependencies(), reconciler)
            .EnableAsync(PluginFeatureTestContext.Key("collection"), CancellationToken.None);
        var faulted = await context
            .Manager(new FaultedLifecycle(), new AvailableDependencies(), reconciler)
            .EnableAsync(PluginFeatureTestContext.Key("collection"), CancellationToken.None);

        missingDependency
            .ShouldBeOfType<PluginFeatureEnableOutcome.Rejected>()
            .Code.ShouldBe(PluginFeatureEnableRejectionCode.MissingCoreDependency);
        faulted
            .ShouldBeOfType<PluginFeatureEnableOutcome.Rejected>()
            .Code.ShouldBe(PluginFeatureEnableRejectionCode.LifecycleNotHealthy);
        (
            await context.Store.LoadFeatureStateAsync(
                PluginFeatureTestContext.Key("collection"),
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task MissingScopes_StaysEnabledDegradedAndRecoversWithoutRetoggle()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var reconciler = new QueueReconciler(
            new PluginFeatureReconciliationResult.MissingScopes(["moderator:read:chatters"]),
            new PluginFeatureReconciliationResult.Ready(),
            new PluginFeatureReconciliationResult.Ready()
        );
        var manager = context.Manager(
            new HealthyLifecycle(),
            new AvailableDependencies(),
            reconciler
        );
        await SaveInstallationAsync(manager);
        _ = (
            await SaveFeatureAsync(
                manager,
                PluginFeatureTestContext.Key("collection"),
                PluginFeatureTestContext.CollectionValues()
            )
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        _ = (
            await SaveFeatureAsync(
                manager,
                PluginFeatureTestContext.Key("publishing"),
                PluginFeatureTestContext.PublishingValues()
            )
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();

        var collection = (
            await manager.EnableAsync(
                PluginFeatureTestContext.Key("collection"),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>();
        var recovered = (
            await manager.SynchronizeDeclarationAsync(
                PluginFeatureTestContext.Key("collection").PluginId,
                CancellationToken.None
            )
        )
            .Single()
            .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>();
        var publishing = (
            await manager.EnableAsync(
                PluginFeatureTestContext.Key("publishing"),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>();

        _ = collection.State.Readiness.ShouldBeOfType<PluginFeatureReadiness.EnabledDegraded>();
        collection.State.Enabled.ShouldBeTrue();
        _ = recovered.State.Readiness.ShouldBeOfType<PluginFeatureReadiness.Ready>();
        recovered.State.Generation.ShouldBe(collection.State.Generation);
        _ = publishing.State.Readiness.ShouldBeOfType<PluginFeatureReadiness.Ready>();
        publishing.State.Key.FeatureId.ShouldNotBe(collection.State.Key.FeatureId);
    }

    [Test]
    public async Task Enable_WaitsForLifecycleOwnerAndRevalidatesHealthBeforeCommit()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var lifecycle = new MutableLifecycleHealth();
        var manager = context.Manager(
            lifecycle,
            new AvailableDependencies(),
            new QueueReconciler(new PluginFeatureReconciliationResult.Ready())
        );
        await SaveInstallationAsync(manager);
        var key = PluginFeatureTestContext.Key("collection");
        _ = (
            await SaveFeatureAsync(manager, key, PluginFeatureTestContext.CollectionValues())
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        var lifecycleLease = await context.Serialization.AcquireAsync(
            key.PluginId,
            CancellationToken.None
        );

        var enable = manager.EnableAsync(key, CancellationToken.None).AsTask();
        enable.IsCompleted.ShouldBeFalse();
        lifecycle.Healthy = false;
        await lifecycleLease.DisposeAsync();

        (await enable)
            .ShouldBeOfType<PluginFeatureEnableOutcome.Rejected>()
            .Code.ShouldBe(PluginFeatureEnableRejectionCode.LifecycleNotHealthy);
        (await context.Store.LoadFeatureStateAsync(key, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Enable_CancelledAfterCommit_RetainsOnePendingStateAndRecoversCurrentGenerationOnce()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var reconciler = new CancellationThenReadyReconciler();
        var manager = context.Manager(
            new HealthyLifecycle(),
            new AvailableDependencies(),
            reconciler
        );
        await SaveInstallationAsync(manager);
        var key = PluginFeatureTestContext.Key("collection");
        _ = (
            await SaveFeatureAsync(manager, key, PluginFeatureTestContext.CollectionValues())
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        using var cancellation = new CancellationTokenSource();

        var enable = manager.EnableAsync(key, cancellation.Token).AsTask();
        PluginFeatureState pending;
        PluginConfigurationState retained;
        try
        {
            await reconciler.FirstAttemptStarted.WaitAsync(TimeSpan.FromSeconds(5));
            pending = (
                await context.Store.LoadFeatureStatesAsync(key.PluginId, CancellationToken.None)
            ).ShouldHaveSingleItem();
            retained = await context.Store.LoadConfigurationAsync(
                new PluginConfigurationOwner.Feature(key),
                CancellationToken.None
            );
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                _ = await enable;
            }
            catch (OperationCanceledException) { }
        }

        _ = await Should.ThrowAsync<OperationCanceledException>(() => enable);
        var degraded = pending.Readiness.ShouldBeOfType<PluginFeatureReadiness.EnabledDegraded>();
        degraded.Reason.Code.ShouldBe(PluginReadinessReasonCode.ReconciliationPending);
        pending.Enabled.ShouldBeTrue();
        retained.Values.ShouldBe(PluginFeatureTestContext.CollectionValues());
        var recovered = (
            await manager.SynchronizeDeclarationAsync(key.PluginId, CancellationToken.None)
        )
            .Single()
            .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
            .State;

        _ = recovered.Readiness.ShouldBeOfType<PluginFeatureReadiness.Ready>();
        recovered.Generation.ShouldBe(pending.Generation);
        var recovery = reconciler.Requests.Skip(1).ShouldHaveSingleItem();
        recovery.Fence.ShouldBe(pending.Fence);
        recovery.Generation.ShouldBe(pending.Generation);
        (await context.Store.LoadFeatureStatesAsync(key.PluginId, CancellationToken.None))
            .ShouldHaveSingleItem()
            .ShouldBe(recovered);
        (
            await context.Store.LoadConfigurationAsync(
                new PluginConfigurationOwner.Feature(key),
                CancellationToken.None
            )
        ).Values.ShouldBe(PluginFeatureTestContext.CollectionValues());
    }

    [Test]
    public async Task Disable_CancelsCommittedGenerationAndIgnoresStaleReconciliation()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        _ = context.Publish();
        var reconciler = new QueueReconciler(new PluginFeatureReconciliationResult.Ready());
        var manager = context.Manager(
            new HealthyLifecycle(),
            new AvailableDependencies(),
            reconciler
        );
        await SaveInstallationAsync(manager);
        _ = (
            await SaveFeatureAsync(
                manager,
                PluginFeatureTestContext.Key("collection"),
                PluginFeatureTestContext.CollectionValues()
            )
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        var enabled = (
            await manager.EnableAsync(
                PluginFeatureTestContext.Key("collection"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
            .State;
        var staleRequest = new PluginFeatureReconciliationRequest(
            enabled.Key,
            enabled.Fence,
            enabled.Generation,
            context
                .Declarations.Current.Declarations[enabled.Key.PluginId]
                .FindFeature(enabled.Key.FeatureId)!
                .Twitch
        );

        var disabled = (await manager.DisableAsync(enabled.Key, CancellationToken.None))
            .ShouldBeOfType<PluginFeatureDisableOutcome.Disabled>()
            .State;
        var stale = await manager.ApplyReconciliationAsync(
            staleRequest,
            new PluginFeatureReconciliationResult.Ready(),
            CancellationToken.None
        );

        _ = disabled.Readiness.ShouldBeOfType<PluginFeatureReadiness.Disabled>();
        disabled.Generation.Value.ShouldBe(enabled.Generation.Value + 1);
        _ = stale.ShouldBeOfType<PluginFeatureReconciliationApplyOutcome.Ignored>();
        reconciler.CancellationTokens.Single().CanBeCanceled.ShouldBeFalse();
        (
            await context.Store.LoadConfigurationAsync(
                new PluginConfigurationOwner.Feature(enabled.Key),
                CancellationToken.None
            )
        ).Values.Entries.ShouldNotBeEmpty();
    }

    [Test]
    public async Task CompatibleReinstall_RebindsEnabledRetainedStateToNewLifecycleFence()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        var oldDeclaration = context.Publish();
        var reconciler = new QueueReconciler(
            new PluginFeatureReconciliationResult.Ready(),
            new PluginFeatureReconciliationResult.Ready()
        );
        var manager = context.Manager(
            new HealthyLifecycle(),
            new AvailableDependencies(),
            reconciler
        );
        await SaveInstallationAsync(manager);
        var key = PluginFeatureTestContext.Key("collection");
        _ = (
            await SaveFeatureAsync(manager, key, PluginFeatureTestContext.CollectionValues())
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        var before = (await manager.EnableAsync(key, CancellationToken.None))
            .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
            .State;
        var newFence = PluginFeatureTestContext.Fence(2);
        var nextDeclaration = context.Publish(newFence, "1.3.0");

        var outcomes = await manager.SynchronizeDeclarationAsync(
            key.PluginId,
            CancellationToken.None
        );
        var after = outcomes.Single().ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>().State;

        oldDeclaration.Fence.ShouldBe(before.Fence);
        nextDeclaration.Fence.ShouldBe(newFence);
        after.Fence.ShouldBe(newFence);
        after.Generation.Value.ShouldBe(before.Generation.Value + 1);
        _ = after.Readiness.ShouldBeOfType<PluginFeatureReadiness.Ready>();
        (
            await context.Store.LoadConfigurationAsync(
                new PluginConfigurationOwner.Feature(key),
                CancellationToken.None
            )
        ).Values.ShouldBe(PluginFeatureTestContext.CollectionValues());
    }

    private static async Task SaveInstallationAsync(PluginFeatureManager manager)
    {
        var owner = new PluginConfigurationOwner.Installation(
            PluginFeatureTestContext.Key("collection").PluginId
        );
        var current = (
            await manager.LoadConfigurationAsync(owner, CancellationToken.None)
        ).ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();
        _ = (
            await manager.SaveConfigurationAsync(
                new(
                    owner,
                    current.Configuration.Revision,
                    PluginFeatureTestContext.InstallationValues(),
                    [PluginFeatureTestContext.SecretReplacement("raw-secret")]
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
    }

    private static async Task<PluginConfigurationSaveOutcome> SaveFeatureAsync(
        PluginFeatureManager manager,
        PluginFeatureKey key,
        PluginSettingValues values
    )
    {
        var owner = new PluginConfigurationOwner.Feature(key);
        var current = (
            await manager.LoadConfigurationAsync(owner, CancellationToken.None)
        ).ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();
        return await manager.SaveConfigurationAsync(
            new(owner, current.Configuration.Revision, values, []),
            CancellationToken.None
        );
    }

    private static PluginSettingValues Values(params PluginSettingValueEntry[] entries) =>
        ((PluginSettingValuesOutcome.Created)PluginSettingValues.Create(entries)).Values;

    private sealed class CancellationThenReadyReconciler : IPluginFeatureReconciler
    {
        private readonly TaskCompletionSource _firstAttemptStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task FirstAttemptStarted => _firstAttemptStarted.Task;

        internal List<PluginFeatureReconciliationRequest> Requests { get; } = [];

        public async ValueTask<PluginFeatureReconciliationResult> ReconcileAsync(
            PluginFeatureReconciliationRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (Requests.Count == 1)
            {
                _ = _firstAttemptStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancelled attempt continued.");
            }
            return Requests.Count == 2
                ? new PluginFeatureReconciliationResult.Ready()
                : throw new InvalidOperationException("Unexpected reconciliation attempt.");
        }

        public ValueTask CancelAsync(
            PluginFeatureKey key,
            PluginLifecycleFence fence,
            PluginFeatureGeneration generation,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }
}

using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginFeatureAdmissionTests
{
    [Test]
    public async Task Admission_FencesReadinessTransitionsAndAllowsIndependentDegradedWork()
    {
        var runtime = new PluginRuntimeSnapshotRegistry();
        var featureSnapshots = new PluginFeatureSnapshotRegistry();
        var key = PluginFeatureTestContext.Key("collection");
        var fence = PluginFeatureTestContext.Fence();
        var installation = Installation(key.PluginId);
        var lifecycle = Lifecycle(installation, fence);
        var worker = new AdmittedWorker();
        _ = runtime.Publish(lifecycle, worker);
        PluginFeatureGeneration.TryCreate(1, out var enabledGeneration).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(1, out var enabledRevision).ShouldBeTrue();
        var enabled = new PluginFeatureState(
            key,
            fence,
            enabledGeneration,
            new PluginFeatureReadiness.Ready(),
            enabledRevision
        );
        featureSnapshots.Hydrate([enabled]);
        var admissions = new PluginFeatureAdmissionService(featureSnapshots, runtime);

        var admitted = admissions
            .Admit(key, new(fence, enabledGeneration), PluginFeatureReadinessDependency.Required)
            .ShouldBeOfType<PluginFeatureAdmissionOutcome.Admitted>()
            .Admission;
        admitted.ValidateCallbackCompletion().ShouldBeTrue();
        PluginFeatureRevision.TryCreate(2, out var degradedRevision).ShouldBeTrue();
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.ReconciliationPending,
                PluginRecoveryAction.Retry,
                "Twitch setup is still in progress.",
                out var reason
            )
            .ShouldBeTrue();
        var degraded = enabled with
        {
            Readiness = new PluginFeatureReadiness.EnabledDegraded(reason),
            Revision = degradedRevision,
        };
        featureSnapshots.Publish(degraded);

        admitted.ValidateWorkerResult().ShouldBeFalse();
        var independent = admissions
            .Admit(key, new(fence, enabledGeneration), PluginFeatureReadinessDependency.Independent)
            .ShouldBeOfType<PluginFeatureAdmissionOutcome.Admitted>()
            .Admission;
        independent.ValidateCallbackCompletion().ShouldBeTrue();
        PluginFeatureRevision.TryCreate(3, out var recoveredRevision).ShouldBeTrue();
        var recovered = enabled with { Revision = recoveredRevision };
        featureSnapshots.Publish(recovered);
        featureSnapshots.Publish(degraded);

        admitted.ValidateCancellation().ShouldBeFalse();
        independent.ValidateWorkerResult().ShouldBeTrue();
        featureSnapshots.Current.States[key].ShouldBe(recovered);
        var recoveredAdmission = admissions
            .Admit(key, new(fence, enabledGeneration), PluginFeatureReadinessDependency.Required)
            .ShouldBeOfType<PluginFeatureAdmissionOutcome.Admitted>()
            .Admission;
        PluginFeatureGeneration.TryCreate(2, out var disabledGeneration).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(4, out var disabledRevision).ShouldBeTrue();
        featureSnapshots.Publish(
            enabled with
            {
                Generation = disabledGeneration,
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = disabledRevision,
            }
        );

        independent.ValidateCancellation().ShouldBeFalse();
        recoveredAdmission.ValidateWorkerResult().ShouldBeFalse();
        admissions
            .Admit(key, new(fence, enabledGeneration), PluginFeatureReadinessDependency.Required)
            .ShouldBeOfType<PluginFeatureAdmissionOutcome.Rejected>()
            .Rejection.Code.ShouldBe(PluginFeatureAdmissionRejectionCode.StaleFeatureGeneration);
        await admitted.DisposeAsync();
        await independent.DisposeAsync();
        await recoveredAdmission.DisposeAsync();
    }

    private static PluginInstallationIdentity Installation(PluginId pluginId)
    {
        SemanticVersion.TryCreate("1.2.0", out var version).ShouldBeTrue();
        PluginGitTag.TryCreate("community-link-queue", out var tag).ShouldBeTrue();
        return new(pluginId, new(version, tag));
    }

    private static PluginLifecycleState Lifecycle(
        PluginInstallationIdentity installation,
        PluginLifecycleFence fence
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            installation.PluginId,
            installation,
            fence.OperationId,
            fence.Generation,
            new(installation, fence),
            PluginLifecyclePhase.Active,
            PluginLifecycleOperationKind.Activate,
            null,
            false,
            null,
            PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Activated, now),
            1,
            now
        );
    }

    private sealed class AdmittedWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ).Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

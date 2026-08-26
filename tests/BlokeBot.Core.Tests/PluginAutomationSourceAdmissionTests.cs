using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginAutomationSourceAdmissionTests
{
    [Test]
    public async Task DisableDrainStartsFirst_SourceEmissionIsDropped()
    {
        var context = Context(new RecordingDispatcher());

        await context.Work.CancelAndDrainAsync(context.State, CancellationToken.None);
        await context.Source.AdmitAsync(
            context.Endpoint,
            context.Invocation,
            [Emission(new PluginValue.String("https://example.invalid"))],
            CancellationToken.None
        );

        context.Dispatcher.Calls.ShouldBe(0);
        _ = context
            .Work.Admit(context.State, CancellationToken.None)
            .ShouldBeOfType<PluginDispatchWorkAdmission.Stopping>();
    }

    [Test]
    public async Task SourceLeaseStartsFirst_DisableCancelsAndDrainsTheAdmission()
    {
        var dispatcher = new PausedDispatcher();
        var context = Context(dispatcher);
        var emission = context
            .Source.AdmitAsync(
                context.Endpoint,
                context.Invocation,
                [Emission(new PluginValue.String("https://example.invalid"))],
                CancellationToken.None
            )
            .AsTask();
        await dispatcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var drain = context
            .Work.CancelAndDrainAsync(context.State, CancellationToken.None)
            .AsTask();
        await dispatcher.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(emission, drain).WaitAsync(TimeSpan.FromSeconds(5));

        dispatcher.Calls.ShouldBe(1);
        _ = context
            .Work.Admit(context.State, CancellationToken.None)
            .ShouldBeOfType<PluginDispatchWorkAdmission.Stopping>();
    }

    [Test]
    public async Task RequiredNilSourceOutput_IsIgnoredWithoutDispatchOrException()
    {
        var context = Context(new RecordingDispatcher());

        await context.Source.AdmitAsync(
            context.Endpoint,
            context.Invocation,
            [Emission(new PluginValue.Nil())],
            CancellationToken.None
        );

        context.Dispatcher.Calls.ShouldBe(0);
    }

    private static SourceTestContext Context(RecordingDispatcher dispatcher)
    {
        var manifest = ManifestWithSourceCommand();
        var fence = Fence();
        var automations = new PluginAutomationCatalogRegistry();
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch, automations);
        var features = new PluginFeatureSnapshotRegistry(dispatch, automations);
        var runtime = new PluginRuntimeSnapshotRegistry();
        var work = new PluginDispatchWorkRegistry();
        declarations.Publish(manifest, fence);
        var state = State(manifest, fence);
        features.Publish(state);
        _ = runtime.Publish(Lifecycle(state, manifest), new PassiveWorker());
        var endpoint = dispatch.Current.Commands.Values.ShouldHaveSingleItem();
        var catalog = new AutomationCatalogService(
            new AutomationDefinitionCatalog([], automations),
            null!
        );
        var source = new PluginAutomationSourceAdmission(
            catalog,
            dispatcher,
            new HostResolver(state.Key.HostId),
            TimeProvider.System,
            new(features, runtime),
            work
        );
        return new(
            source,
            dispatcher,
            work,
            state,
            endpoint,
            new(new(manifest.Manifest.Id, manifest.Manifest.Release), state.Key.HostId)
        );
    }

    private static PluginAutomationSourceEmission Emission(PluginValue value) =>
        new(Definition("queued-link"), new([new PluginValueProperty("link", value)]));

    private static ValidatedPluginManifest ManifestWithSourceCommand()
    {
        var accepted = PluginManifestToml.Validate(
            PluginContractFixtures.CompleteManifestToml(),
            PluginContractFixtures.CompatibleHost()
        );
        var manifest = ((PluginManifestValidationOutcome.Accepted)accepted).Manifest;
        var feature = manifest.Manifest.Features.Single(candidate =>
            candidate.Id == Feature("publishing")
        );
        PluginHostOperationId.TryCreate("handle", out var operation).ShouldBeTrue();
        var modified = manifest.Manifest with
        {
            Features = manifest.Manifest.Features.Replace(
                feature,
                feature with
                {
                    Dispatch = new(
                        [
                            new(
                                "plugin-source",
                                manifest.Manifest.LuaModules[0].Id,
                                operation,
                                PluginCallbackRequirements.Independent
                            ),
                        ],
                        [],
                        []
                    ),
                }
            ),
        };
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(modified, PluginContractFixtures.CompatibleHost())
        ).Manifest;
    }

    private static PluginFeatureState State(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence
    )
    {
        PluginHostId.TryCreate(1, out var host).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(1, out var revision).ShouldBeTrue();
        return new(
            new(manifest.Manifest.Id, Feature("publishing"), host),
            fence,
            generation,
            new PluginFeatureReadiness.Ready(),
            revision
        );
    }

    private static PluginLifecycleState Lifecycle(
        PluginFeatureState state,
        ValidatedPluginManifest manifest
    )
    {
        var installation = new PluginInstallationIdentity(
            state.Key.PluginId,
            manifest.Manifest.Release
        );
        var now = DateTimeOffset.UtcNow;
        return new(
            state.Key.PluginId,
            installation,
            state.Fence.OperationId,
            state.Fence.Generation,
            new(installation, state.Fence),
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

    private static PluginLifecycleFence Fence()
    {
        PluginWorkerGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), generation);
    }

    private static PluginFeatureId Feature(string value) =>
        PluginFeatureId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid feature test ID.");

    private static PluginAutomationDefinitionId Definition(string value) =>
        PluginAutomationDefinitionId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid definition test ID.");

    private sealed record SourceTestContext(
        PluginAutomationSourceAdmission Source,
        RecordingDispatcher Dispatcher,
        PluginDispatchWorkRegistry Work,
        PluginFeatureState State,
        PluginDispatchEndpoint Endpoint,
        PluginInvocationContext.Channel Invocation
    );

    private class RecordingDispatcher : IPluginAutomationRunDispatcher
    {
        internal int Calls { get; private protected set; }

        public virtual Task<AutomationDispatchOutcome> DispatchAsync(
            AutomationTrigger trigger,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new AutomationDispatchOutcome(AutomationDispatchStatus.NoMatchingFlow, [])
            );
        }
    }

    private sealed class PausedDispatcher : RecordingDispatcher
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<AutomationDispatchOutcome> DispatchAsync(
            AutomationTrigger trigger,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            _ = Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _ = Canceled.TrySetResult();
                throw;
            }
            throw new InvalidOperationException("The paused dispatcher resumed unexpectedly.");
        }
    }

    private sealed class HostResolver(PluginHostId hostId) : IPluginHostContextResolver
    {
        public ValueTask<PluginHostContext?> FindAsync(
            PluginHostId selectedHost,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostContext?>(
                selectedHost == hostId ? new(hostId, "streamer") : null
            );

        public ValueTask<PluginHostContext?> FindAsync(
            string channelLogin,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostContext?>(
                string.Equals(channelLogin, "streamer", StringComparison.OrdinalIgnoreCase)
                    ? new(hostId, "streamer")
                    : null
            );
    }

    private sealed class PassiveWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ).Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

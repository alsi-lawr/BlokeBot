using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task PluginWidget_ManifestConfigurationInvocationAndLateGenerationFailureRemainIsolated()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var html = environment.Registry.Create(new("html"), Guid.NewGuid())!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Plugin", Document() with { Widgets = [widget, html] }),
                _ct
            )
        );
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(created.Overlay.Id, created.Overlay.Revision, "Plugin", created.Overlay.Draft),
                _ct
            )
        ).Overlay;
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            published.Id,
            published.PublishedVersion,
            published.Revision,
            published.Draft,
            FullOverlayDataMode.Live,
            []
        );
        var first = await environment.Registry.ProjectAsync(context, _ct);
        first[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Plugin>()
            .State.Properties.ShouldContain(
                new PluginValueProperty("text", new PluginValue.String("Public title"))
            );
        var invocation = plugin.Worker.Identity!;
        invocation
            .Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .Mode.ShouldBe(PluginWidgetProjectionMode.Live);
        invocation.Host.Value.ShouldBe(fixture.HostId);
        invocation
            .Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .OverlayId.ShouldBe(published.Id);
        invocation
            .Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .InstanceId.ShouldBe(widget.Id.Value);
        invocation
            .Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .DocumentId.ShouldBe(published.Draft.Id);
        var hostContext = await new PluginContextHostModule().InvokeAsync(
            invocation,
            new(
                PluginContractFixtures.HostCallId(),
                invocation.CoroutineId,
                PluginStandardHostModules.Context.Id,
                PluginStandardHostModules.Context.Operations[0].Id,
                invocation.Context,
                []
            ),
            _ct
        );
        PluginStructuredValueSchemas
            .WidgetInvocationContext.Accepts(
                hostContext
                    .ShouldBeOfType<PluginHostCallOutcome.Returned>()
                    .Value.ShouldBeOfType<PluginValue.Map>()
            )
            .ShouldBeTrue();
        _ = (
            await environment.Registry.ProjectAsync(
                context with
                {
                    Version = null,
                    SelectionRevision = null,
                    DataMode = FullOverlayDataMode.Sample,
                },
                _ct
            )
        )[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Plugin>();
        plugin
            .Worker.Identity!.Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .Mode.ShouldBe(PluginWidgetProjectionMode.PreviewSample);
        plugin.Worker.Pause = true;
        var pending = environment.Registry.ProjectAsync(context, _ct);
        await plugin.Worker.Started.Task;
        plugin.Disable();
        _ = plugin.Worker.Release.TrySetResult();
        var stale = await pending;
        _ = stale[0].Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        _ = stale[1].Output.ShouldBeOfType<FullOverlayWidgetOutput.Html>();
        var saved = Value(await fixture.Service.GetAsync(fixture.Owner, published.Id, _ct));
        saved
            .Draft.Widgets[0]
            .Configuration.GetProperty("text")
            .GetString()
            .ShouldBe("Public title");
        plugin.Enable();
        plugin.Worker.Pause = false;
        _ = (await environment.Registry.ProjectAsync(context, _ct))[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Plugin>();
        _ = (
            await environment.Registry.ProjectAsync(
                context with
                {
                    HostId = fixture.OtherHostId,
                },
                _ct
            )
        )[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
    }

    [Test]
    public async Task PluginAssets_AdmitOnlyDeclaredCurrentOverlayWidgetInstallationAndRejectLateRemoval()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Assets", Document() with { Widgets = [widget] }),
                _ct
            )
        );
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(created.Overlay.Id, created.Overlay.Revision, "Assets", created.Overlay.Draft),
                _ct
            )
        ).Overlay;
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            published.Id,
            published.PublishedVersion,
            published.Revision,
            published.Draft,
            FullOverlayDataMode.Live,
            []
        );
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context,
                widget.Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.Available>();
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context,
                widget.Id,
                "private.txt",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context,
                widget.Id,
                "media/icon.webp",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context with
                {
                    HostId = fixture.OtherHostId,
                },
                widget.Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        plugin.Package.Pause = true;
        var pending = environment
            .Registry.ResolvePluginAssetAsync(context, widget.Id, "web/index.html", _ct)
            .AsTask();
        await plugin.Package.Started.Task;
        plugin.Declarations.Remove(plugin.Manifest.Manifest.Id, plugin.State.Fence);
        _ = plugin.Package.Release.TrySetResult();
        _ = (await pending).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
    }

    private sealed class WidgetPluginRig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-workspace/overlay-composition-20261001/286/plugin",
            Guid.NewGuid().ToString("N")
        );
        internal ValidatedPluginManifest Manifest { get; }
        internal PluginFeatureDeclarationRegistry Declarations { get; } = new();
        internal PluginFeatureSnapshotRegistry Features { get; } = new();
        internal PluginRuntimeSnapshotRegistry Runtime { get; } = new();
        internal PluginFeatureState State { get; private set; }
        internal WidgetWorker Worker { get; } = new();
        internal WidgetPackage Package { get; }

        internal WidgetPluginRig(int hostId)
        {
            var previous = PluginManifestToml
                .Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                .Manifest.Manifest;
            _ = PluginWidgetId.TryCreate("display", out var widgetId);
            _ = PluginLuaModuleId.TryCreate("main", out var module);
            var manifest = previous with
            {
                Widgets =
                [
                    new(
                        widgetId,
                        previous.Features[0].Id,
                        "Public display",
                        module,
                        "widget-render",
                        previous.Assets.Single(asset => asset.Path == "web/index.html").Id,
                        [],
                        [new("text", "Text", PluginValueKind.String, true)],
                        new PluginValue.Map([new("text", new PluginValue.String("Public title"))])
                    ),
                ],
            };
            var validated = PluginManifestValidator
                .Validate(manifest, PluginContractFixtures.CompatibleHost())
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                .Manifest;
            Manifest = PluginManifestToml
                .Validate(
                    PluginManifestToml.Serialize(validated),
                    PluginContractFixtures.CompatibleHost()
                )
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                .Manifest;
            _ = PluginHostId.TryCreate(hostId, out var host);
            _ = PluginLifecycleOperationId.TryCreate(Guid.NewGuid(), out var operation);
            _ = PluginWorkerGeneration.TryCreate(1, out var generation);
            _ = PluginFeatureGeneration.TryCreate(1, out var featureGeneration);
            _ = PluginFeatureRevision.TryCreate(1, out var revision);
            State = new(
                new(manifest.Id, manifest.Features[0].Id, host),
                new(operation, generation),
                featureGeneration,
                new PluginFeatureReadiness.Ready(),
                revision
            );
            var installation = new PluginInstallationIdentity(manifest.Id, manifest.Release);
            var now = DateTimeOffset.UtcNow;
            var lifecycle = new PluginLifecycleState(
                manifest.Id,
                installation,
                operation,
                generation,
                new(installation, State.Fence),
                PluginLifecyclePhase.Active,
                PluginLifecycleOperationKind.Activate,
                null,
                false,
                null,
                PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Activated, now),
                1,
                now
            );
            _ = Runtime.Publish(lifecycle, Worker);
            Features.Publish(State);
            Declarations.Publish(Manifest, State.Fence);
            _ = Directory.CreateDirectory(Path.Combine(_root, "web"));
            File.WriteAllText(
                Path.Combine(_root, "web/index.html"),
                "<!doctype html><main>Public widget</main>"
            );
            File.WriteAllText(Path.Combine(_root, "private.txt"), "server-only");
            Package = new(Manifest, _root);
        }

        internal void Disable() => Transition(new PluginFeatureReadiness.Disabled());

        internal void Enable() => Transition(new PluginFeatureReadiness.Ready());

        private void Transition(PluginFeatureReadiness readiness)
        {
            _ = PluginFeatureGeneration.TryCreate(State.Generation.Value + 1, out var generation);
            _ = PluginFeatureRevision.TryCreate(State.Revision.Value + 1, out var revision);
            State = State with
            {
                Generation = generation,
                Revision = revision,
                Readiness = readiness,
            };
            Features.Publish(State);
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WidgetWorker : IPluginLifecycleWorkerSession
    {
        internal bool Pause { get; set; }
        internal Exception? Failure { get; set; }
        internal PluginWorkerInvocationIdentity? Identity { get; private set; }
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;
        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>().Task;

        public async ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken ct
        )
        {
            Identity = identity;
            if (Pause)
            {
                _ = Started.TrySetResult();
                await Release.Task;
            }
            if (Failure is { } failure)
            {
                throw failure;
            }
            var configuration = invocation
                .Input.ShouldBeOfType<PluginValue.Map>()
                .Properties.Single(property => property.Name == "configuration")
                .Value;
            return new(
                new PluginWorkerInvocationOutcome.Returned(configuration),
                PluginWorkerInvocationMetrics.Empty,
                []
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class WidgetPackage(ValidatedPluginManifest manifest, string root)
        : IPluginPackageAssetResolver
    {
        internal bool Pause { get; set; }
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PluginPackageAssetResolution> ResolveAsync(
            PluginInstallationIdentity installation,
            PluginPackageOperationId operation,
            CancellationToken ct
        )
        {
            if (Pause)
            {
                _ = Started.TrySetResult();
                await Release.Task;
            }
            return new PluginPackageAssetResolution.Available(manifest, root);
        }
    }
}

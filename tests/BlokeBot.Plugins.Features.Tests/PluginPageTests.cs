using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginPageTests
{
    [Test]
    public void Catalogue_ProjectsReadyAndActionableLifecycleStatesWithoutOwningTransitions()
    {
        var setup = Setup();

        _ = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-management")
            .ShouldBeOfType<PluginPageResolution.Available>();

        setup.Features.Publish(
            setup.State with
            {
                Generation = Generation(2),
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = Revision(2),
            }
        );
        _ = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-management")
            .ShouldBeOfType<PluginPageResolution.Disabled>();

        setup.Features.Publish(
            setup.State with
            {
                Generation = Generation(3),
                Readiness = new PluginFeatureReadiness.EnabledDegraded(DegradedReason()),
                Revision = Revision(3),
            }
        );
        _ = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-management")
            .ShouldBeOfType<PluginPageResolution.NeedsAttention>();

        _ = setup.Runtime.Publish(
            setup.Lifecycle with
            {
                ActiveRuntime = null,
                Phase = PluginLifecyclePhase.Faulted,
                FaultedFrom = PluginLifecyclePhase.Active,
            },
            worker: null
        );
        _ = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-management")
            .ShouldBeOfType<PluginPageResolution.Faulted>();

        _ = setup.Runtime.Publish(
            setup.Lifecycle with
            {
                ActiveRuntime = null,
                Phase = PluginLifecyclePhase.Removed,
            },
            worker: null
        );
        _ = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-management")
            .ShouldBeOfType<PluginPageResolution.Removed>();
        _ = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "not-declared")
            .ShouldBeOfType<PluginPageResolution.Missing>();
    }

    [Test]
    public void Sessions_ExpireAndRejectWrongOriginReplayAndGeneration()
    {
        var setup = Setup();
        var endpoint = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-preview")
            .ShouldBeOfType<PluginPageResolution.Available>()
            .Endpoint;
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var sessions = new PluginPageSessionRegistry(time);
        var session = sessions
            .Create(endpoint, ["https://blokebot.example", "https://plugin.example"])
            .ShouldBeOfType<PluginPageSessionCreation.Created>()
            .Session;
        var message = MessageId();

        sessions
            .AdmitMessage(session.Id, message, session.Binding, "https://other.example")
            .ShouldBeOfType<PluginPageMessageAdmission.Rejected>()
            .Code.ShouldBe(PluginPageMessageRejectionCode.InvalidOrigin);
        _ = sessions
            .AdmitMessage(session.Id, message, session.Binding, "https://blokebot.example")
            .ShouldBeOfType<PluginPageMessageAdmission.Admitted>();
        sessions
            .AdmitMessage(session.Id, message, session.Binding, "https://blokebot.example")
            .ShouldBeOfType<PluginPageMessageAdmission.Rejected>()
            .Code.ShouldBe(PluginPageMessageRejectionCode.Duplicate);
        sessions
            .AdmitMessage(
                session.Id,
                MessageId(),
                session.Binding with
                {
                    Generation = Generation(2),
                },
                "https://blokebot.example"
            )
            .ShouldBeOfType<PluginPageMessageAdmission.Rejected>()
            .Code.ShouldBe(PluginPageMessageRejectionCode.Stale);

        time.Advance(TimeSpan.FromMinutes(16));
        sessions
            .AdmitMessage(session.Id, MessageId(), session.Binding, "https://blokebot.example")
            .ShouldBeOfType<PluginPageMessageAdmission.Rejected>()
            .Code.ShouldBe(PluginPageMessageRejectionCode.Expired);
    }

    [Test]
    public void GeneratedDocument_RequiresVersionKnownActionAndSurfaceBounds()
    {
        var setup = Setup();
        var feature = setup.Manifest.Manifest.Features.Single(candidate =>
            candidate.Id == setup.Feature
        );
        var action = feature.DispatchDeclarations.Actions.ShouldHaveSingleItem().Id;
        var valid = Document([
            Map(
                ("kind", Text("form")),
                ("title", Text("Queue controls")),
                ("action", Text(action.Value)),
                (
                    "fields",
                    Array(
                        Map(("id", Text("query")), ("label", Text("Query")), ("kind", Text("text")))
                    )
                )
            ),
            Map(("kind", Text("status")), ("title", Text("Queue")), ("tone", Text("success"))),
        ]);

        var parsed = PluginPageDocumentParser
            .Parse(valid, feature)
            .ShouldBeOfType<PluginPageDocumentParseOutcome.Parsed>();
        parsed.Document.Sections.Length.ShouldBe(2);

        PluginPageDocumentParser
            .Parse(Document([], version: 2), feature)
            .ShouldBeOfType<PluginPageDocumentParseOutcome.Rejected>()
            .Errors.ShouldContain(error =>
                error.Code == PluginPageDocumentErrorCode.UnsupportedVersion
            );
        var unknownAction = Document([
            Map(
                ("kind", Text("form")),
                ("title", Text("Unknown")),
                ("action", Text("not-declared")),
                ("fields", Array())
            ),
        ]);
        PluginPageDocumentParser
            .Parse(unknownAction, feature)
            .ShouldBeOfType<PluginPageDocumentParseOutcome.Rejected>()
            .Errors.ShouldContain(error => error.Code == PluginPageDocumentErrorCode.UnknownAction);
        PluginPageDocumentParser
            .Parse(
                Document(
                    Enumerable.Repeat<PluginValue>(
                        Map(("kind", Text("text")), ("title", Text("One")), ("body", Text("Body"))),
                        PluginContractLimits.MaximumPageSections + 1
                    )
                ),
                feature
            )
            .ShouldBeOfType<PluginPageDocumentParseOutcome.Rejected>()
            .Errors.ShouldContain(error => error.Code == PluginPageDocumentErrorCode.LimitExceeded);
    }

    [Test]
    public async Task GeneratedPage_UsesPageContextAndExistingAdmissionFence()
    {
        var worker = new RecordingWorker();
        var setup = Setup(worker);
        var endpoint = setup
            .Catalogue.Resolve(setup.Plugin, setup.Feature, setup.Host, "queue-management")
            .ShouldBeOfType<PluginPageResolution.Available>()
            .Endpoint;
        var session = PluginContractFixtures.PageSessionId();
        var context = new PluginInvocationContext.Page(
            endpoint.Definition.Declaration.Installation,
            setup.Host,
            endpoint.Definition.Id,
            session
        );
        var invoker = new PluginDispatchInvoker(
            new(setup.Features, setup.Runtime),
            setup.Runtime,
            new(),
            TimeProvider.System
        );

        _ = (
            await invoker.InvokePageAsync(
                endpoint,
                context,
                new PluginValue.Map([]),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginDispatchInvocationOutcome.Returned>();
        _ = worker.Invocations.ShouldHaveSingleItem().ShouldBeOfType<PluginLiveInvocation.Page>();
        worker.Identities.ShouldHaveSingleItem().Context.ShouldBe(context);
        worker.Identities[0].Activation!.FeatureGeneration.Value.ShouldBe(1UL);

        setup.Features.Publish(
            endpoint.State with
            {
                Generation = Generation(2),
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = Revision(2),
            }
        );
        _ = (
            await invoker.InvokePageAsync(
                endpoint,
                context,
                new PluginValue.Map([]),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginDispatchInvocationOutcome.Rejected>();
        worker.Invocations.Count.ShouldBe(1);
    }

    private static PageSetup Setup(RecordingWorker? worker = null)
    {
        var manifest = Manifest();
        var plugin = manifest.Manifest.Id;
        var feature = manifest.Manifest.Features.Single(candidate =>
            candidate.Id.Value == "collection"
        );
        var host = Host(1);
        var fence = PluginFeatureTestContext.Fence();
        var state = new PluginFeatureState(
            new(plugin, feature.Id, host),
            fence,
            Generation(1),
            new PluginFeatureReadiness.Ready(),
            Revision(1)
        );
        var runtime = new PluginRuntimeSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry();
        var features = new PluginFeatureSnapshotRegistry();
        declarations.Publish(manifest, fence);
        features.Publish(state);
        var lifecycle = Lifecycle(manifest, state);
        _ = runtime.Publish(lifecycle, worker ?? new RecordingWorker());
        return new(
            manifest,
            plugin,
            feature.Id,
            host,
            state,
            lifecycle,
            runtime,
            features,
            new(declarations, features, runtime)
        );
    }

    private static ValidatedPluginManifest Manifest()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestToml.Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(candidate =>
            candidate.Id.Value == "collection"
        );
        var module = accepted.Manifest.LuaModules[0].Id;
        _ = PluginActionId.TryCreate("refresh", out var action);
        _ = PluginHostOperationId.TryCreate("handle", out var operation);
        var modified = accepted.Manifest with
        {
            Features = accepted.Manifest.Features.Replace(
                feature,
                feature with
                {
                    Dispatch = new(
                        [],
                        [],
                        [],
                        [],
                        [new(action, module, operation, PluginCallbackRequirements.Independent)]
                    ),
                }
            ),
        };
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(modified, PluginContractFixtures.CompatibleHost())
        ).Manifest;
    }

    private static PluginLifecycleState Lifecycle(
        ValidatedPluginManifest manifest,
        PluginFeatureState state
    )
    {
        var installation = new PluginInstallationIdentity(
            manifest.Manifest.Id,
            manifest.Manifest.Release
        );
        var now = DateTimeOffset.UtcNow;
        return new(
            manifest.Manifest.Id,
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

    private static PluginValue.Map Document(IEnumerable<PluginValue> sections, int version = 1) =>
        Map(("version", new PluginValue.Number(version)), ("sections", Array(sections.ToArray())));

    private static PluginValue.Map Map(params (string Name, PluginValue Value)[] values) =>
        new(
            values
                .Select(value => new PluginValueProperty(value.Name, value.Value))
                .ToImmutableArray()
        );

    private static PluginValue.Array Array(params PluginValue[] values) => new([.. values]);

    private static PluginValue.String Text(string value) => new(value);

    private static PluginPageMessageId MessageId()
    {
        PluginPageMessageId.TryCreate(Guid.NewGuid(), out var id).ShouldBeTrue();
        return id;
    }

    private static PluginHostId Host(int value)
    {
        PluginHostId.TryCreate(value, out var host).ShouldBeTrue();
        return host;
    }

    private static PluginFeatureGeneration Generation(ulong value)
    {
        PluginFeatureGeneration.TryCreate(value, out var generation).ShouldBeTrue();
        return generation;
    }

    private static PluginFeatureRevision Revision(long value)
    {
        PluginFeatureRevision.TryCreate(value, out var revision).ShouldBeTrue();
        return revision;
    }

    private static PluginReadinessReason DegradedReason()
    {
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.ReconciliationFailed,
                PluginRecoveryAction.Retry,
                "Reconnect Twitch and try again.",
                out var reason
            )
            .ShouldBeTrue();
        return reason;
    }

    private sealed record PageSetup(
        ValidatedPluginManifest Manifest,
        PluginId Plugin,
        PluginFeatureId Feature,
        PluginHostId Host,
        PluginFeatureState State,
        PluginLifecycleState Lifecycle,
        PluginRuntimeSnapshotRegistry Runtime,
        PluginFeatureSnapshotRegistry Features,
        PluginPageCatalog Catalogue
    );

    private sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;

        internal void Advance(TimeSpan duration) => current = current.Add(duration);
    }

    private sealed class RecordingWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>().Task;

        internal List<PluginWorkerInvocationIdentity> Identities { get; } = [];

        internal List<PluginLiveInvocation> Invocations { get; } = [];

        public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            Identities.Add(identity);
            Invocations.Add(invocation);
            return ValueTask.FromResult(
                new PluginWorkerInvocationResult(
                    new PluginWorkerInvocationOutcome.Returned(new PluginValue.Map([])),
                    PluginWorkerInvocationMetrics.Empty,
                    []
                )
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

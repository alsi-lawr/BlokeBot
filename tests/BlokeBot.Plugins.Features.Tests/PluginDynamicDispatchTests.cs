using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginDynamicDispatchTests
{
    [Test]
    public async Task LifecycleActivationPublisher_HotSwapsExactDeclarationFenceAndRoutes()
    {
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var publisher = new PluginFeatureActivationPublisher(declarations);
        var firstManifest = Manifest();
        var firstFence = PluginFeatureTestContext.Fence();
        var firstState = State(firstManifest, firstFence, 1, 1, new PluginFeatureReadiness.Ready());

        _ = (
            await publisher.PublishAsync(
                Activation(firstManifest, firstFence),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        features.Publish(firstState);
        dispatch.Current.Commands.ShouldContainKey(new(firstState.Key.HostId, "plugin-route"));

        var firstFeature = firstManifest.Manifest.Features.Single(feature =>
            feature.Id == firstState.Key.FeatureId
        );
        var movedCommand = firstFeature.DispatchDeclarations.Commands[0] with
        {
            Route = "moved-route",
        };
        var secondManifest = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(
                    firstManifest.Manifest with
                    {
                        Features = firstManifest.Manifest.Features.Replace(
                            firstFeature,
                            firstFeature with
                            {
                                Dispatch = firstFeature.DispatchDeclarations with
                                {
                                    Commands = [movedCommand],
                                },
                            }
                        ),
                    },
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var secondFence = PluginFeatureTestContext.Fence(generation: 2);

        _ = (
            await publisher.PublishAsync(
                Activation(secondManifest, secondFence),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        dispatch.Current.Commands.ShouldBeEmpty();
        features.Publish(
            firstState with
            {
                Fence = secondFence,
                Generation = Generation(2),
                Revision = Revision(2),
            }
        );

        dispatch.Current.Commands.ShouldContainKey(new(firstState.Key.HostId, "moved-route"));
        dispatch.Current.Commands.ShouldNotContainKey(new(firstState.Key.HostId, "plugin-route"));
        await publisher.WithdrawAsync(
            Activation(secondManifest, secondFence),
            CancellationToken.None
        );
        declarations.Current.Declarations.ShouldNotContainKey(firstManifest.Manifest.Id);
        dispatch.Current.Commands.ShouldBeEmpty();
    }

    [Test]
    public void PublishedFeatureState_ChangesAllDynamicEndpointsInOneSnapshot()
    {
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var manifest = Manifest();
        var fence = PluginFeatureTestContext.Fence();
        var first = State(manifest, fence, 1, 1, new PluginFeatureReadiness.Ready());
        var second = State(manifest, fence, 2, 1, new PluginFeatureReadiness.Ready());

        declarations.Publish(manifest, fence);
        features.Hydrate([first, second]);

        dispatch.Current.Commands.Keys.ShouldBe(
            [new(first.Key.HostId, "plugin-route"), new(second.Key.HostId, "plugin-route")],
            ignoreOrder: true
        );
        dispatch
            .Current.Events.Select(endpoint => endpoint.State.Key)
            .ShouldBe([first.Key, second.Key], ignoreOrder: true);
        dispatch
            .Current.Schedules.Select(endpoint => endpoint.State.Key)
            .ShouldBe([first.Key, second.Key], ignoreOrder: true);
        dispatch.Current.Webhooks.Count.ShouldBe(2);
        dispatch.Current.HttpActions.Count.ShouldBe(2);
        dispatch.Current.PageActions.Count.ShouldBe(2);

        features.Publish(
            first with
            {
                Generation = Generation(2),
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = Revision(2),
            }
        );

        dispatch.Current.Commands.Keys.ShouldBe([new(second.Key.HostId, "plugin-route")]);
        dispatch.Current.Events.ShouldHaveSingleItem().State.Key.ShouldBe(second.Key);
        dispatch.Current.Schedules.ShouldHaveSingleItem().State.Key.ShouldBe(second.Key);
        dispatch.Current.Webhooks.Values.ShouldHaveSingleItem().State.Key.ShouldBe(second.Key);
        dispatch.Current.HttpActions.Values.ShouldHaveSingleItem().State.Key.ShouldBe(second.Key);
        dispatch.Current.PageActions.Values.ShouldHaveSingleItem().State.Key.ShouldBe(second.Key);

        declarations.Remove(manifest.Manifest.Id, fence);

        dispatch.Current.Commands.ShouldBeEmpty();
        dispatch.Current.Events.ShouldBeEmpty();
        dispatch.Current.Schedules.ShouldBeEmpty();
        dispatch.Current.Webhooks.ShouldBeEmpty();
        dispatch.Current.HttpActions.ShouldBeEmpty();
        dispatch.Current.PageActions.ShouldBeEmpty();
    }

    [Test]
    public async Task ActivePluginCommandRoute_RejectsOnlyASecondPluginClaimForTheSameHost()
    {
        var dispatch = new PluginDispatchSnapshotRegistry();
        var manifest = Manifest();
        var fence = PluginFeatureTestContext.Fence();
        var active = State(manifest, fence, 1, 1, new PluginFeatureReadiness.Ready());
        dispatch.PublishDeclaration(Declaration(manifest, fence));
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        features.Publish(active);
        var feature = manifest.Manifest.Features.Single(item => item.Id == active.Key.FeatureId);
        var secondPlugin = PluginContractFixtures.PluginId("community.second-plugin");
        var conflicting = active.Key with { PluginId = secondPlugin };

        var rejection = dispatch
            .Reserve(conflicting, feature)
            .ShouldBeOfType<PluginCommandActivationReservationOutcome.Rejected>();

        rejection.Code.ShouldBe(PluginCommandActivationRejectionCode.ActivePluginRouteCollision);
        rejection.Route.ShouldBe("plugin-route");
        var otherHost = active.Key with { PluginId = secondPlugin, HostId = Host(2) };
        var reservation = dispatch
            .Reserve(otherHost, feature)
            .ShouldBeOfType<PluginCommandActivationReservationOutcome.Reserved>();
        await reservation.Reservation.DisposeAsync();
        dispatch.Current.Commands.ShouldContainKey(new(active.Key.HostId, "plugin-route"));
    }

    [Test]
    public async Task FaultedOrRemovedRuntime_RemovesEveryEndpointAndReleasesItsCommandRoute()
    {
        var runtime = new PluginRuntimeSnapshotRegistry();
        var dispatch = new PluginDispatchSnapshotRegistry(runtime);
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var manifest = Manifest();
        var fence = PluginFeatureTestContext.Fence();
        var state = State(manifest, fence, 1, 1, new PluginFeatureReadiness.Ready());
        var lifecycle = Lifecycle(state, manifest);
        declarations.Publish(manifest, fence);
        features.Publish(state);
        _ = runtime.Publish(lifecycle, new RecordingWorker());

        dispatch.Current.Commands.ShouldNotBeEmpty();
        dispatch.Current.Events.ShouldNotBeEmpty();
        dispatch.Current.Schedules.ShouldNotBeEmpty();

        _ = runtime.Publish(
            lifecycle with
            {
                ActiveRuntime = null,
                Phase = PluginLifecyclePhase.Faulted,
                FaultedFrom = PluginLifecyclePhase.Active,
            },
            worker: null
        );

        dispatch.Current.Commands.ShouldBeEmpty();
        dispatch.Current.Events.ShouldBeEmpty();
        dispatch.Current.Schedules.ShouldBeEmpty();
        var secondPlugin = PluginContractFixtures.PluginId("community.second-plugin");
        var feature = manifest.Manifest.Features.Single(item => item.Id == state.Key.FeatureId);
        var reservation = dispatch
            .Reserve(state.Key with { PluginId = secondPlugin }, feature)
            .ShouldBeOfType<PluginCommandActivationReservationOutcome.Reserved>();
        await reservation.Reservation.DisposeAsync();

        _ = runtime.Publish(lifecycle, new RecordingWorker());
        dispatch.Current.Commands.ShouldNotBeEmpty();
        _ = runtime.Publish(
            lifecycle with
            {
                ActiveRuntime = null,
                Phase = PluginLifecyclePhase.Removed,
            },
            worker: null
        );

        dispatch.Current.Commands.ShouldBeEmpty();
        dispatch.Current.Events.ShouldBeEmpty();
        dispatch.Current.Schedules.ShouldBeEmpty();
    }

    [Test]
    public async Task EnabledDegradedFeature_AdmitsIndependentCommandButNotTwitchEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var setup = Setup(new PluginFeatureReadiness.EnabledDegraded(DegradedReason()));
        var command = setup.Dispatch.Current.Commands.Values.ShouldHaveSingleItem();
        var twitchEvent = setup.Dispatch.Current.Events.ShouldHaveSingleItem();
        var context = Context(command);

        var commandOutcome = await setup.Invoker.InvokeCommandAsync(
            command,
            context,
            new PluginValue.String("command"),
            timeout.Token
        );
        var eventOutcome = await setup.Invoker.InvokeEventAsync(
            twitchEvent,
            context,
            new PluginValue.String("event"),
            timeout.Token
        );

        _ = commandOutcome.ShouldBeOfType<PluginDispatchInvocationOutcome.Returned>();
        var rejection = eventOutcome.ShouldBeOfType<PluginDispatchInvocationOutcome.Rejected>();
        rejection.Code.ShouldBe(PluginDispatchInvocationRejectionCode.FeatureUnavailable);
        _ = setup
            .Worker.Invocations.ShouldHaveSingleItem()
            .ShouldBeOfType<PluginLiveInvocation.Command>();
        _ = setup.Worker.Identities.ShouldHaveSingleItem().Activation.ShouldNotBeNull();
        setup.Worker.Identities[0].Activation!.FeatureGeneration.Value.ShouldBe(1UL);
    }

    [Test]
    public async Task GenerationChange_DropsACompletedWorkerResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var worker = new ControlledWorker();
        var setup = Setup(new PluginFeatureReadiness.Ready(), worker);
        var command = setup.Dispatch.Current.Commands.Values.ShouldHaveSingleItem();
        var invocation = setup
            .Invoker.InvokeCommandAsync(
                command,
                Context(command),
                new PluginValue.String("command"),
                timeout.Token
            )
            .AsTask();
        await worker.Started.Task.WaitAsync(timeout.Token);

        setup.Features.Publish(
            command.State with
            {
                Generation = Generation(2),
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = Revision(2),
            }
        );
        worker.Complete();

        _ = (await invocation).ShouldBeOfType<PluginDispatchInvocationOutcome.Stale>();
        setup.Dispatch.Current.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task FeatureStop_CancelsEveryActiveInvocationAndDrainsItsGeneration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var worker = new CancellingWorker(expectedInvocations: 2);
        var setup = Setup(new PluginFeatureReadiness.Ready(), worker);
        var command = setup.Dispatch.Current.Commands.Values.ShouldHaveSingleItem();
        var first = setup
            .Invoker.InvokeCommandAsync(
                command,
                Context(command),
                new PluginValue.String("first"),
                timeout.Token
            )
            .AsTask();
        var second = setup
            .Invoker.InvokeCommandAsync(
                command,
                Context(command),
                new PluginValue.String("second"),
                timeout.Token
            )
            .AsTask();
        await worker.AllStarted.Task.WaitAsync(timeout.Token);

        await setup.Work.CancelAndDrainAsync(command.State, timeout.Token);

        _ = (await first).ShouldBeOfType<PluginDispatchInvocationOutcome.Cancelled>();
        _ = (await second).ShouldBeOfType<PluginDispatchInvocationOutcome.Cancelled>();
        worker.CancellationCount.ShouldBe(2);
        _ = setup
            .Work.Admit(command.State, timeout.Token)
            .ShouldBeOfType<PluginDispatchWorkAdmission.Stopping>();
    }

    [Test]
    public async Task AutomationInvocation_UsesTypedIpcForEveryKindAndRejectsAnotherHost()
    {
        var manifest = AutomationManifest();
        var fence = PluginFeatureTestContext.Fence();
        var state = AutomationState(manifest, fence, new PluginFeatureReadiness.Ready());
        var declaration = Declaration(manifest, fence);
        var worker = new RecordingWorker();
        var (invoker, _, _) = AutomationSetup(manifest, state, worker);
        PluginValue input = new PluginValue.Map([
            new("array", new PluginValue.Array([new PluginValue.Number(4)])),
            new("map", new PluginValue.Map([new("enabled", new PluginValue.Boolean(true))])),
        ]);

        foreach (var descriptor in manifest.Manifest.AutomationDefinitions)
        {
            var endpoint = new PluginAutomationEndpoint(declaration, state, descriptor);
            var outcome = await invoker.InvokeAutomationAsync(
                endpoint,
                AutomationContext(endpoint),
                input,
                CancellationToken.None
            );

            _ = outcome.ShouldBeOfType<PluginDispatchInvocationOutcome.Returned>();
        }

        worker
            .Invocations.Select(static invocation =>
                invocation.ShouldBeOfType<PluginLiveInvocation.Automation>().Kind
            )
            .ShouldBe(
                [
                    PluginAutomationDefinitionKind.Source,
                    PluginAutomationDefinitionKind.Action,
                    PluginAutomationDefinitionKind.Value,
                    PluginAutomationDefinitionKind.Control,
                    PluginAutomationDefinitionKind.Transform,
                ],
                ignoreOrder: true
            );
        worker.Invocations.ShouldAllBe(invocation => invocation.Input == input);
        var first = new PluginAutomationEndpoint(
            declaration,
            state,
            manifest.Manifest.AutomationDefinitions[0]
        );
        var invalidContext = AutomationContext(first) with { Host = Host(2) };

        var rejected = await invoker.InvokeAutomationAsync(
            first,
            invalidContext,
            input,
            CancellationToken.None
        );

        rejected
            .ShouldBeOfType<PluginDispatchInvocationOutcome.Rejected>()
            .Code.ShouldBe(PluginDispatchInvocationRejectionCode.InvalidContext);
        worker.Invocations.Count.ShouldBe(5);
    }

    [Test]
    public async Task AutomationInvocation_IsCancelledByFeatureDrainAndDropsAStaleResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var manifest = AutomationManifest();
        var fence = PluginFeatureTestContext.Fence();
        var state = AutomationState(manifest, fence, new PluginFeatureReadiness.Ready());
        var declaration = Declaration(manifest, fence);
        var descriptor = manifest.Manifest.AutomationDefinitions[0];
        var endpoint = new PluginAutomationEndpoint(declaration, state, descriptor);
        var cancellingWorker = new CancellingWorker(expectedInvocations: 1);
        var (cancellingInvoker, _, cancellingWork) = AutomationSetup(
            manifest,
            state,
            cancellingWorker
        );
        var cancelled = cancellingInvoker
            .InvokeAutomationAsync(
                endpoint,
                AutomationContext(endpoint),
                new PluginValue.Map([]),
                timeout.Token
            )
            .AsTask();
        await cancellingWorker.AllStarted.Task.WaitAsync(timeout.Token);

        await cancellingWork.CancelAndDrainAsync(state, timeout.Token);

        _ = (await cancelled).ShouldBeOfType<PluginDispatchInvocationOutcome.Cancelled>();

        var staleWorker = new ControlledWorker();
        var (staleInvoker, staleFeatures, _) = AutomationSetup(manifest, state, staleWorker);
        var stale = staleInvoker
            .InvokeAutomationAsync(
                endpoint,
                AutomationContext(endpoint),
                new PluginValue.Map([]),
                timeout.Token
            )
            .AsTask();
        await staleWorker.Started.Task.WaitAsync(timeout.Token);
        staleFeatures.Publish(
            state with
            {
                Generation = Generation(2),
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = Revision(2),
            }
        );
        staleWorker.Complete();

        _ = (await stale).ShouldBeOfType<PluginDispatchInvocationOutcome.Stale>();
    }

    [Test]
    public void CallbackResult_ParsesTypedSourceEmissionsFromTheReturnedValue()
    {
        PluginValue value = new PluginValue.Map([
            new(
                "$automationSources",
                new PluginValue.Array([
                    new PluginValue.Map([
                        new("definition", new PluginValue.String("queued-link")),
                        new(
                            "outputs",
                            new PluginValue.Map([
                                new(
                                    "items",
                                    new PluginValue.Array([
                                        new PluginValue.Map([
                                            new("name", new PluginValue.String("first")),
                                        ]),
                                    ])
                                ),
                            ])
                        ),
                    ]),
                ])
            ),
        ]);

        var emission = PluginAutomationCallbackResult.Emissions(value).ShouldHaveSingleItem();

        emission.DefinitionId.Value.ShouldBe("queued-link");
        _ = emission
            .Outputs.Properties.Single()
            .Value.ShouldBeOfType<PluginValue.Array>()
            .Items.ShouldHaveSingleItem()
            .ShouldBeOfType<PluginValue.Map>();
    }

    [Test]
    public async Task WebhookAuthenticationAndHandler_ShareOneCurrentAdmissionAndWorkLease()
    {
        var worker = new WebWorker(authentication: true);
        var setup = Setup(new PluginFeatureReadiness.Ready(), worker);
        var endpoint = setup.Dispatch.Current.Webhooks.Values.ShouldHaveSingleItem();
        var context = Context(endpoint) with
        {
            Web = new(PluginWebInvocationKind.Webhook, endpoint.Descriptor.Id.Value, "POST"),
        };

        var outcome = await setup.Invoker.InvokeWebhookAsync(
            endpoint,
            context,
            new PluginValue.Map([]),
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<PluginWebDispatchOutcome.Returned>();
        worker.Invocations.Count.ShouldBe(2);
        foreach (var invocation in worker.Invocations)
        {
            _ = invocation.ShouldBeOfType<PluginLiveInvocation.HostAction>();
        }
        worker
            .Identities.Select(static identity => identity.Activation)
            .Distinct()
            .Count()
            .ShouldBe(1);
        worker.Identities.ShouldAllBe(identity => identity.Context == context);
    }

    [Test]
    public async Task WebhookAuthentication_DenialAndGenerationChangeNeverRunTheHandler()
    {
        var deniedWorker = new WebWorker(authentication: false);
        var denied = Setup(new PluginFeatureReadiness.Ready(), deniedWorker);
        var deniedEndpoint = denied.Dispatch.Current.Webhooks.Values.ShouldHaveSingleItem();
        var deniedOutcome = await denied.Invoker.InvokeWebhookAsync(
            deniedEndpoint,
            Context(deniedEndpoint),
            new PluginValue.Map([]),
            CancellationToken.None
        );
        _ = deniedOutcome.ShouldBeOfType<PluginWebDispatchOutcome.AuthenticationRejected>();
        deniedWorker.Invocations.Count.ShouldBe(1);

        var staleWorker = new WebWorker(authentication: true);
        var stale = Setup(new PluginFeatureReadiness.Ready(), staleWorker);
        var staleEndpoint = stale.Dispatch.Current.Webhooks.Values.ShouldHaveSingleItem();
        staleWorker.AfterAuthentication = () =>
            stale.Features.Publish(
                staleEndpoint.State with
                {
                    Generation = Generation(2),
                    Readiness = new PluginFeatureReadiness.Disabled(),
                    Revision = Revision(2),
                }
            );
        var staleOutcome = await stale.Invoker.InvokeWebhookAsync(
            staleEndpoint,
            Context(staleEndpoint),
            new PluginValue.Map([]),
            CancellationToken.None
        );
        _ = staleOutcome.ShouldBeOfType<PluginWebDispatchOutcome.Stale>();
        staleWorker.Invocations.Count.ShouldBe(1);
    }

    [Test]
    public async Task DeliberatelyPublicWebhook_SkipsAuthenticationButKeepsGenerationFencing()
    {
        var worker = new WebWorker(authentication: true);
        var setup = Setup(new PluginFeatureReadiness.Ready(), worker);
        var endpoint = setup.Dispatch.Current.Webhooks.Values.ShouldHaveSingleItem();
        endpoint = new(
            endpoint.Declaration,
            endpoint.State,
            endpoint.Descriptor with
            {
                Authentication = new PluginWebhookAuthentication.Public(),
            }
        );

        var outcome = await setup.Invoker.InvokeWebhookAsync(
            endpoint,
            Context(endpoint),
            new PluginValue.Map([]),
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<PluginWebDispatchOutcome.Returned>();
        worker.Invocations.Count.ShouldBe(1);
    }

    private static DynamicDispatchSetup Setup(
        PluginFeatureReadiness readiness,
        RecordingWorker? worker = null
    )
    {
        var runtime = new PluginRuntimeSnapshotRegistry();
        var dispatch = new PluginDispatchSnapshotRegistry(runtime);
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var work = new PluginDispatchWorkRegistry();
        var manifest = Manifest();
        var fence = PluginFeatureTestContext.Fence();
        var state = State(manifest, fence, 1, 1, readiness);
        var activeWorker = worker ?? new RecordingWorker();
        declarations.Publish(manifest, fence);
        features.Publish(state);
        _ = runtime.Publish(Lifecycle(state, manifest), activeWorker);
        var invoker = new PluginDispatchInvoker(
            new(features, runtime),
            runtime,
            work,
            TimeProvider.System
        );
        return new(dispatch, features, work, invoker, activeWorker);
    }

    private static (
        PluginDispatchInvoker Invoker,
        PluginFeatureSnapshotRegistry Features,
        PluginDispatchWorkRegistry Work
    ) AutomationSetup(
        ValidatedPluginManifest manifest,
        PluginFeatureState state,
        RecordingWorker worker
    )
    {
        var runtime = new PluginRuntimeSnapshotRegistry();
        var features = new PluginFeatureSnapshotRegistry();
        var work = new PluginDispatchWorkRegistry();
        features.Publish(state);
        _ = runtime.Publish(Lifecycle(state, manifest), worker);
        return (new(new(features, runtime), runtime, work, TimeProvider.System), features, work);
    }

    private static PluginInvocationContext.Automation AutomationContext(
        PluginAutomationEndpoint endpoint
    )
    {
        PluginAutomationInvocationId.TryCreate(Guid.NewGuid(), out var invocationId).ShouldBeTrue();
        return new(
            endpoint.Declaration.Installation,
            endpoint.State.Key.HostId,
            endpoint.State.Key.FeatureId,
            endpoint.Descriptor.Id,
            invocationId
        );
    }

    private static ValidatedPluginManifest AutomationManifest()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestToml.Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var source = accepted.Manifest.AutomationDefinitions[0];
        var fields = source.Outputs;
        System.Collections.Immutable.ImmutableArray<PluginAutomationDefinitionDescriptor> definitions =
        [
            .. accepted.Manifest.AutomationDefinitions,
            source with
            {
                Id = AutomationDefinition("value-node"),
                Kind = PluginAutomationDefinitionKind.Value,
                EntryPoint = "value_node",
                Name = "Value node",
            },
            source with
            {
                Id = AutomationDefinition("control-node"),
                Kind = PluginAutomationDefinitionKind.Control,
                EntryPoint = "control_node",
                Name = "Control node",
                Inputs = fields,
                Outputs = [],
            },
            source with
            {
                Id = AutomationDefinition("transform-node"),
                Kind = PluginAutomationDefinitionKind.Transform,
                EntryPoint = "transform_node",
                Name = "Transform node",
                Inputs = fields,
                Outputs = [fields[0] with { Id = AutomationField("result") }],
            },
        ];
        var modified = accepted.Manifest with { AutomationDefinitions = definitions };
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(modified, PluginContractFixtures.CompatibleHost())
        ).Manifest;
    }

    private static PluginLifecycleActivationContext Activation(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence
    )
    {
        var installation = new PluginInstallationIdentity(
            manifest.Manifest.Id,
            manifest.Manifest.Release
        );
        var prepared = new PreparedPluginWorkerPackage(
            new(
                installation,
                PluginRuntimeIdentifier.LinuxX64,
                manifest.Manifest.EntryModule,
                [
                    .. manifest.Manifest.LuaModules.Select(module => new PluginWorkerLuaModule(
                        module.Id,
                        module.Path
                    )),
                ]
            ),
            "/packages/test-plugin"
        )
        {
            Manifest = manifest,
        };
        return new(
            installation,
            fence,
            new(
                installation,
                PluginPackageOperationId.FromLifecycleOperation(fence.OperationId),
                prepared,
                "/state/test-plugin",
                null!,
                null!
            )
        );
    }

    private static PluginFeatureState AutomationState(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence,
        PluginFeatureReadiness readiness
    )
    {
        var feature = manifest.Manifest.Features.Single(item => item.Id.Value == "publishing");
        return new(
            new(manifest.Manifest.Id, feature.Id, Host(1)),
            fence,
            Generation(1),
            readiness,
            Revision(1)
        );
    }

    private static PluginAutomationDefinitionId AutomationDefinition(string value)
    {
        PluginAutomationDefinitionId.TryCreate(value, out var id).ShouldBeTrue();
        return id;
    }

    private static PluginAutomationFieldId AutomationField(string value)
    {
        PluginAutomationFieldId.TryCreate(value, out var id).ShouldBeTrue();
        return id;
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
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("handle", out var operation).ShouldBeTrue();
        PluginEventHandlerId.TryCreate("stream-online", out var eventId).ShouldBeTrue();
        PluginScheduleHandlerId.TryCreate("refresh", out var scheduleId).ShouldBeTrue();
        PluginWebhookId.TryCreate("incoming", out var webhookId).ShouldBeTrue();
        PluginActionId.TryCreate("refresh", out var actionId).ShouldBeTrue();
        PluginActionId.TryCreate("page-refresh", out var pageActionId).ShouldBeTrue();
        PluginPageActionInputId.TryCreate("query", out var pageInputId).ShouldBeTrue();
        PluginHostOperationId.TryCreate("handle_page", out var pageOperation).ShouldBeTrue();
        PluginHostOperationId.TryCreate("authenticate", out var authentication).ShouldBeTrue();
        var modified = accepted.Manifest with
        {
            Features = accepted.Manifest.Features.Replace(
                feature,
                feature with
                {
                    Twitch = feature.Twitch with
                    {
                        EventSubTypes = feature.Twitch.EventSubTypes.Add("stream.online"),
                    },
                    Dispatch = new(
                        [
                            new(
                                "plugin-route",
                                module,
                                operation,
                                PluginCallbackRequirements.Independent
                            ),
                        ],
                        [
                            new(
                                eventId,
                                new PluginEventSource.Twitch(PluginTwitchEventKind.StreamOnline),
                                module,
                                operation,
                                PluginCallbackRequirements.Twitch
                            ),
                        ],
                        [
                            new(
                                scheduleId,
                                module,
                                operation,
                                PluginCallbackRequirements.Independent
                            ),
                        ],
                        [
                            new(
                                webhookId,
                                module,
                                operation,
                                PluginCallbackRequirements.Independent,
                                new PluginWebhookAuthentication.Callback(module, authentication)
                            ),
                        ],
                        [
                            new PluginActionDescriptor.Http(
                                actionId,
                                module,
                                operation,
                                PluginCallbackRequirements.Independent
                            ),
                            new PluginActionDescriptor.Page(
                                pageActionId,
                                module,
                                pageOperation,
                                PluginCallbackRequirements.Independent,
                                [new(pageInputId, "Query", PluginValueKind.String, true)]
                            ),
                        ]
                    ),
                }
            ),
        };
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(modified, PluginContractFixtures.CompatibleHost())
        ).Manifest;
    }

    private static PluginFeatureDeclaration Declaration(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence
    ) => new(new(manifest.Manifest.Id, manifest.Manifest.Release), fence, manifest.Manifest);

    private static PluginFeatureState State(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence,
        int hostId,
        ulong generation,
        PluginFeatureReadiness readiness
    )
    {
        var feature = manifest.Manifest.Features.Single(item => item.Id.Value == "collection");
        return new(
            new(manifest.Manifest.Id, feature.Id, Host(hostId)),
            fence,
            Generation(generation),
            readiness,
            Revision(1)
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

    private static PluginInvocationContext.Channel Context(PluginDispatchEndpoint endpoint) =>
        new(endpoint.Declaration.Installation, endpoint.State.Key.HostId);

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
                PluginReadinessReasonCode.ReconciliationPending,
                PluginRecoveryAction.Retry,
                "Twitch setup is pending.",
                out var reason
            )
            .ShouldBeTrue();
        return reason;
    }

    private sealed record DynamicDispatchSetup(
        PluginDispatchSnapshotRegistry Dispatch,
        PluginFeatureSnapshotRegistry Features,
        PluginDispatchWorkRegistry Work,
        PluginDispatchInvoker Invoker,
        RecordingWorker Worker
    );

    private class RecordingWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ).Task;

        internal List<PluginWorkerInvocationIdentity> Identities { get; } = [];

        internal List<PluginLiveInvocation> Invocations { get; } = [];

        public virtual ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            Identities.Add(identity);
            Invocations.Add(invocation);
            return ValueTask.FromResult(Returned());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        protected static PluginWorkerInvocationResult Returned() =>
            new(
                new PluginWorkerInvocationOutcome.Returned(new PluginValue.String("handled")),
                PluginWorkerInvocationMetrics.Empty,
                []
            );
    }

    private sealed class ControlledWorker : RecordingWorker
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Complete() => _release.TrySetResult();

        public override async ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            _ = base.InvokeAsync(identity, invocation, cancellationToken);
            _ = Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return Returned();
        }
    }

    private sealed class CancellingWorker(int expectedInvocations) : RecordingWorker
    {
        private int _cancellationCount;
        private int _started;

        internal TaskCompletionSource AllStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int CancellationCount => Volatile.Read(ref _cancellationCount);

        public override async ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            _ = base.InvokeAsync(identity, invocation, cancellationToken);
            if (Interlocked.Increment(ref _started) == expectedInvocations)
            {
                _ = AllStarted.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "An infinite delay completed without cancellation."
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ = Interlocked.Increment(ref _cancellationCount);
                return new(
                    new PluginWorkerInvocationOutcome.Cancelled(
                        PluginCancellationReason.CallerRequested,
                        false
                    ),
                    PluginWorkerInvocationMetrics.Empty,
                    []
                );
            }
        }
    }

    private sealed class WebWorker(bool authentication) : RecordingWorker
    {
        internal Action? AfterAuthentication { get; set; }

        public override ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            Identities.Add(identity);
            Invocations.Add(invocation);
            if (Invocations.Count == 1)
            {
                AfterAuthentication?.Invoke();
                return ValueTask.FromResult(
                    new PluginWorkerInvocationResult(
                        new PluginWorkerInvocationOutcome.Returned(
                            new PluginValue.Boolean(authentication)
                        ),
                        PluginWorkerInvocationMetrics.Empty,
                        []
                    )
                );
            }

            return ValueTask.FromResult(
                new PluginWorkerInvocationResult(
                    new PluginWorkerInvocationOutcome.Returned(
                        new PluginValue.Map([
                            new("status", new PluginValue.Number(200)),
                            new("body", new PluginValue.String("ok")),
                        ])
                    ),
                    PluginWorkerInvocationMetrics.Empty,
                    []
                )
            );
        }
    }
}

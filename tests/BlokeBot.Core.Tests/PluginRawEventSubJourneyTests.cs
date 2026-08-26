using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginRawEventSubJourneyTests
{
    [Test]
    public async Task PendingWorkCancellation_UsesExactActiveFenceAndCentralEventSubOwner()
    {
        var manifest = Manifest();
        var fence = Fence();
        var host = Host();
        var state = State(
            manifest,
            fence,
            host,
            generation: 1,
            new PluginFeatureReadiness.Ready(),
            revision: 1
        );
        var declarations = new PluginFeatureDeclarationRegistry();
        declarations.Publish(manifest, fence);
        var trigger = new RecordingReconciliationTrigger();
        var reconciler = new PluginFeatureEventSubReconciler(
            new HostResolver(host),
            new ReadyTokenStatusProvider(),
            trigger
        );
        var canceller = new PluginFeaturePendingWorkCanceller(
            new FixedFeatureStore([state]),
            reconciler,
            declarations
        );
        var staleFence = Fence();

        _ = (
            await canceller.CancelAsync(manifest.Manifest.Id, staleFence, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        declarations.Current.Declarations.ShouldContainKey(manifest.Manifest.Id);
        trigger.Calls.ShouldBe(0);

        _ = (
            await canceller.CancelAsync(manifest.Manifest.Id, fence, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        declarations.Current.Declarations.ShouldNotContainKey(manifest.Manifest.Id);
        trigger.Calls.ShouldBe(1);
    }

    [Test]
    public async Task CentralStatusPublishedDuringReconciliation_SupersedesStaleHealthyReadiness()
    {
        var manifest = Manifest();
        var feature = manifest.Manifest.Features.Single(item => item.Id.Value == "collection");
        var fence = Fence();
        var host = Host();
        var state = State(manifest, fence, host, generation: 1, Degraded(), revision: 1);
        var statuses = new MutableStatus(HealthyChannel());
        var trigger = new RecordingReconciliationTrigger(() =>
            statuses.Publish(RecoveringChannel())
        );
        var reconciler = new PluginFeatureEventSubReconciler(
            new HostResolver(host),
            new ReadyTokenStatusProvider(),
            trigger,
            statuses
        );

        var result = await reconciler.ReconcileAsync(
            new(state.Key, fence, state.Generation, feature.Twitch),
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<PluginFeatureReconciliationResult.Pending>();
        trigger.Calls.ShouldBe(1);
    }

    [Test]
    public async Task ChannelBan_ProvisionsBeforeReadyAndDeliversOnlyThroughCurrentBoundedActivation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manifest = Manifest();
        var feature = manifest.Manifest.Features.Single(item => item.Id.Value == "collection");
        var fence = Fence();
        var host = Host();
        var degraded = State(manifest, fence, host, generation: 1, Degraded(), revision: 1);
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        declarations.Publish(manifest, fence);
        features.Publish(degraded);
        var runtime = new PluginRuntimeSnapshotRegistry();
        var worker = new RecordingWorker();
        _ = runtime.Publish(Lifecycle(degraded, manifest), worker);
        var requirements = new PluginEventSubRequirementSource(
            new HostResolver(host),
            declarations,
            features,
            runtime
        );
        var trigger = new RecordingReconciliationTrigger();
        var reconciler = new PluginFeatureEventSubReconciler(
            new HostResolver(host),
            new ReadyTokenStatusProvider(),
            trigger,
            new HealthyStatus()
        );

        var reconciliation = await reconciler.ReconcileAsync(
            new(degraded.Key, fence, degraded.Generation, feature.Twitch),
            timeout.Token
        );

        _ = reconciliation.ShouldBeOfType<PluginFeatureReconciliationResult.Ready>();
        (await requirements.GetRequirementsAsync("streamer", timeout.Token)).ShouldBe([
            new EventSubExactSubscription("channel.ban", "1"),
        ]);
        trigger.Calls.ShouldBe(1);

        var invoker = new PluginDispatchInvoker(
            new(features, runtime),
            runtime,
            new PluginDispatchWorkRegistry(),
            TimeProvider.System
        );
        var rawDelivery = new EventSubRawDelivery([
            new PluginRawEventSubBridge(new HostResolver(host), dispatch, invoker),
        ]);
        var notification = Envelope(
            "event-1",
            "channel.ban",
            "1",
            """
            {
              "broadcaster_user_login": "streamer",
              "user_login": "viewer",
              "reason": "spam"
            }
            """
        );

        await rawDelivery.DispatchAsync(notification, timeout.Token);
        worker.Invocations.ShouldBeEmpty();
        var ready = degraded with
        {
            Readiness = new PluginFeatureReadiness.Ready(),
            Revision = Revision(2),
        };
        features.Publish(ready);
        await rawDelivery.DispatchAsync(notification, timeout.Token);

        var identity = worker.Identities.ShouldHaveSingleItem();
        identity.Host.ShouldBe(host);
        identity.Feature.ShouldBe(ready.Key.FeatureId);
        _ = identity.Activation.ShouldNotBeNull();
        identity.Activation!.FeatureGeneration.Value.ShouldBe(1UL);
        _ = identity.CancellationId.ShouldNotBeNull();
        var invocation = worker
            .Invocations.ShouldHaveSingleItem()
            .ShouldBeOfType<PluginLiveInvocation.Event>();
        var envelope = invocation.Input.ShouldBeOfType<PluginValue.Map>();
        PropertyMap(envelope, "subscription")
            .Properties.Single(property => property.Name == "type")
            .Value.ShouldBe(new PluginValue.String("channel.ban"));
        PropertyMap(envelope, "subscription")
            .Properties.Single(property => property.Name == "version")
            .Value.ShouldBe(new PluginValue.String("1"));
        PropertyMap(envelope, "event")
            .Properties.Single(property => property.Name == "reason")
            .Value.ShouldBe(new PluginValue.String("spam"));

        var staleEndpoint = dispatch.Current.Events.ShouldHaveSingleItem();
        features.Publish(
            ready with
            {
                Generation = Generation(2),
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = Revision(3),
            }
        );
        var stale = await invoker.InvokeEventAsync(
            staleEndpoint,
            new(
                staleEndpoint.Declaration.Installation,
                host,
                Event: new(
                    staleEndpoint.Descriptor.Id,
                    "channel.ban",
                    "event-stale",
                    DateTimeOffset.UtcNow
                )
            ),
            envelope,
            timeout.Token
        );
        stale
            .ShouldBeOfType<PluginDispatchInvocationOutcome.Rejected>()
            .Code.ShouldBe(PluginDispatchInvocationRejectionCode.FeatureUnavailable);
        worker.Invocations.Count.ShouldBe(1);

        var nextReady = ready with { Generation = Generation(3), Revision = Revision(4) };
        features.Publish(nextReady);
        _ = (
            await reconciler.ReconcileAsync(
                new(nextReady.Key, fence, nextReady.Generation, feature.Twitch),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFeatureReconciliationResult.Ready>();
        await rawDelivery.DispatchAsync(
            Envelope(
                "event-large",
                "channel.ban",
                "1",
                $$"""
                {
                  "broadcaster_user_login": "streamer",
                  "reason": "{{new string(
                    'x',
                    PluginContractLimits.MaximumPluginValueStringBytes + 1
                )}}"
                }
                """
            ),
            timeout.Token
        );
        worker.Invocations.Count.ShouldBe(1);
    }

    private static ValidatedPluginManifest Manifest()
    {
        var accepted = PluginManifestJson
            .Validate(
                PluginContractFixtures.CompleteManifestJson(),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("channel_ban", out var operation).ShouldBeTrue();
        PluginEventHandlerId.TryCreate("channel-ban", out var handler).ShouldBeTrue();
        var modified = accepted.Manifest with
        {
            Features = accepted.Manifest.Features.Replace(
                feature,
                feature with
                {
                    Twitch = feature.Twitch with
                    {
                        Scopes = feature.Twitch.Scopes.Add("moderator:read:banned_users"),
                        EventSubTypes = feature.Twitch.EventSubTypes.Add("channel.ban"),
                    },
                    Dispatch = new(
                        [],
                        [
                            new(
                                handler,
                                new PluginEventSource.TwitchRaw("channel.ban", "1"),
                                module,
                                operation,
                                PluginCallbackRequirements.Twitch
                            ),
                        ],
                        []
                    ),
                }
            ),
        };
        return PluginManifestValidator
            .Validate(modified, PluginContractFixtures.CompatibleHost())
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
    }

    private static EventSubEnvelope Envelope(
        string messageId,
        string subscriptionType,
        string subscriptionVersion,
        string eventJson
    )
    {
        using var document = JsonDocument.Parse(eventJson);
        return new()
        {
            Event = document.RootElement.Clone(),
            Metadata = new()
            {
                MessageId = messageId,
                MessageType = "notification",
                MessageTimestamp = DateTimeOffset.UtcNow,
                SubscriptionType = subscriptionType,
                SubscriptionVersion = subscriptionVersion,
            },
        };
    }

    private static PluginValue.Map PropertyMap(PluginValue.Map map, string name) =>
        map
            .Properties.Single(property => property.Name == name)
            .Value.ShouldBeOfType<PluginValue.Map>();

    private static PluginFeatureState State(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence,
        PluginHostId host,
        ulong generation,
        PluginFeatureReadiness readiness,
        long revision
    )
    {
        var feature = manifest.Manifest.Features.Single(item => item.Id.Value == "collection");
        return new(
            new(manifest.Manifest.Id, feature.Id, host),
            fence,
            Generation(generation),
            readiness,
            Revision(revision)
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

    private static PluginFeatureReadiness Degraded()
    {
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.ReconciliationPending,
                PluginRecoveryAction.Retry,
                "Twitch setup is pending.",
                out var reason
            )
            .ShouldBeTrue();
        return new PluginFeatureReadiness.EnabledDegraded(reason);
    }

    private static PluginHostId Host()
    {
        PluginHostId.TryCreate(1, out var host).ShouldBeTrue();
        return host;
    }

    private static PluginLifecycleFence Fence()
    {
        PluginWorkerGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), generation);
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

    private sealed class HostResolver(PluginHostId host) : IPluginHostContextResolver
    {
        public ValueTask<PluginHostContext?> FindAsync(
            PluginHostId hostId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostContext?>(hostId == host ? new(host, "streamer") : null);

        public ValueTask<PluginHostContext?> FindAsync(
            string channelLogin,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostContext?>(
                string.Equals(channelLogin, "streamer", StringComparison.OrdinalIgnoreCase)
                    ? new(host, "streamer")
                    : null
            );
    }

    private sealed class ReadyTokenStatusProvider : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            var scopes = requiredScopes.OfType<string>().ToImmutableArray();
            var validation = new TokenValidation("bot-user-1", "bot", OAuthScopeSet.Create(scopes));
            return Task.FromResult(
                new ActiveBotAccountTokenStatus
                {
                    BotLogin = "bot",
                    Status = new TokenStatus.Ready("token", validation, scopes, scopes),
                }
            );
        }
    }

    private sealed class HealthyStatus : IEventSubChannelStatusAccessor
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public EventSubChannelStatusSnapshot Current { get; } =
            new()
            {
                Channels =
                [
                    new EventSubChannelStatus.Healthy
                    {
                        Channel = "streamer",
                        Phase = EventSubChannelPhase.Reconciliation,
                        Attempt = 1,
                        ChangedAt = DateTimeOffset.UtcNow,
                        Trigger = EventSubChannelRecoveryTrigger.Explicit,
                    },
                ],
            };
    }

    private sealed class MutableStatus(EventSubChannelStatus initial)
        : IEventSubChannelStatusAccessor
    {
        private EventSubChannelStatusSnapshot _current = new() { Channels = [initial] };

        public event Action? Changed;

        public EventSubChannelStatusSnapshot Current => Volatile.Read(ref _current);

        internal void Publish(EventSubChannelStatus status)
        {
            Volatile.Write(ref _current, new() { Channels = [status] });
            Changed?.Invoke();
        }
    }

    private sealed class RecordingReconciliationTrigger(Action? reconcile = null)
        : IEventSubChannelReconciliationTrigger
    {
        internal int Calls { get; private set; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            Calls++;
            reconcile?.Invoke();
            return Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private static EventSubChannelStatus HealthyChannel() =>
        new EventSubChannelStatus.Healthy
        {
            Channel = "streamer",
            Phase = EventSubChannelPhase.Reconciliation,
            Attempt = 1,
            ChangedAt = DateTimeOffset.UtcNow,
            Trigger = EventSubChannelRecoveryTrigger.Explicit,
        };

    private static EventSubChannelStatus RecoveringChannel() =>
        new EventSubChannelStatus.Recovering
        {
            Channel = "streamer",
            Phase = EventSubChannelPhase.SubscriptionSetup,
            Attempt = 1,
            ChangedAt = DateTimeOffset.UtcNow,
            Trigger = EventSubChannelRecoveryTrigger.Explicit,
            Failure = new()
            {
                Classification = EventSubChannelFailureClassification.Transient,
                FailureType = typeof(IOException).FullName!,
            },
            NextAction = EventSubChannelNextAction.BeginRecoveryCycle,
        };

    private sealed class RecordingWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ).Task;

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
                    new PluginWorkerInvocationOutcome.Returned(new PluginValue.Nil()),
                    PluginWorkerInvocationMetrics.Empty,
                    []
                )
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedFeatureStore(IReadOnlyList<PluginFeatureState> states)
        : IPluginFeatureStore
    {
        public ValueTask<IReadOnlyList<PluginFeatureState>> LoadFeatureStatesAsync(
            PluginId? pluginId,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(states);

        public ValueTask<PluginConfigurationState> LoadConfigurationAsync(
            PluginConfigurationOwner owner,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginConfigurationStoreWriteOutcome> WriteConfigurationAsync(
            PluginConfigurationStoreWrite write,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginFeatureState?> LoadFeatureStateAsync(
            PluginFeatureKey key,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginFeatureEnableStoreOutcome> EnableAsync(
            PluginFeatureEnableStoreRequest request,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginFeatureStateStoreWriteOutcome> WriteFeatureStateAsync(
            PluginFeatureState expected,
            PluginFeatureState next,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask RemovePluginDataAsync(
            PluginId pluginId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<bool> HasFormat1IncompatibleStateAsync(
            PluginHostId hostId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}

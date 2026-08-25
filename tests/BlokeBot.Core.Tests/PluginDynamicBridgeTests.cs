using System.Collections.Immutable;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginDynamicBridgeTests
{
    [Test]
    public async Task CommandDispatcher_BuiltInThenCustomRoutesShadowAReadyPluginRoute()
    {
        var dispatch = new PluginDispatchSnapshotRegistry();
        var manifest = ManifestWithCommands("fixed-shadow", "custom-shadow", "plugin-only");
        var fence = Fence();
        var state = State(manifest, fence, generation: 1);
        dispatch.PublishDeclaration(Declaration(manifest, fence));
        var featureStates = new PluginFeatureSnapshotRegistry(dispatch);
        featureStates.Publish(state);
        var plugin = new RecordingDispatchInvoker();
        var builtInCount = 0;
        var custom = new CustomShadowModule("custom-shadow");
        var registry = new ChatCommandRegistry(
            [
                new()
                {
                    Configure = commands =>
                        commands.Map(
                            "fixed-shadow",
                            (_, _, _) =>
                            {
                                builtInCount++;
                                return ValueTask.CompletedTask;
                            }
                        ),
                },
            ],
            [custom, new PluginCommandModule(new HostResolver(state.Key.HostId), dispatch, plugin)],
            []
        );
        var dispatcher = new ChatCommandDispatcher(registry);
        var responder = new CommandResponder((_, _) => ValueTask.CompletedTask);

        await dispatcher.DispatchResponsesAsync(
            Message("!fixed-shadow"),
            responder,
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("!custom-shadow"),
            responder,
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("!plugin-only one two"),
            responder,
            CancellationToken.None
        );

        builtInCount.ShouldBe(1);
        custom.HandledCount.ShouldBe(1);
        var pluginContext = plugin.Contexts.ShouldHaveSingleItem();
        _ = pluginContext.Command.ShouldNotBeNull();
        pluginContext.Command!.Route.ShouldBe("plugin-only");
        pluginContext.Command.Arguments.ShouldBe(["one", "two"]);
        dispatch.Current.Commands.Count.ShouldBe(3);
        dispatch.Current.Commands.Values.ShouldAllBe(endpoint => endpoint.State.Enabled);
    }

    [Test]
    public async Task ActiveRoundSharedAlias_WinsRealDispatcherCollisionWithAPluginRoute()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, activeProfileId) = await SeedSharedGuessingAliasRoundAsync(database);
        PluginHostId.TryCreate(hostId, out var pluginHostId).ShouldBeTrue();
        var dispatch = new PluginDispatchSnapshotRegistry();
        var manifest = ManifestWithCommands("shared-guesses");
        var fence = Fence();
        var state = State(manifest, fence, generation: 1, pluginHostId);
        dispatch.PublishDeclaration(Declaration(manifest, fence));
        var featureStates = new PluginFeatureSnapshotRegistry(dispatch);
        featureStates.Publish(state);
        var plugin = new RecordingDispatchInvoker();
        List<(GuessCommandKind Kind, AppCommandRouteState State)> guessingCalls = [];
        var strategies = Enum.GetValues<GuessCommandKind>()
            .Select(kind =>
                (ICommandStrategy<GuessCommandKind, AppCommandRouteState>)
                    new RecordingGuessingStrategy(kind, guessingCalls)
            )
            .ToArray();
        var guessing = new CommandStrategyModule<GuessCommandKind, AppCommandRouteState>(
            new GuessingCommandRouteResolver(
                new AppCommandAliasResolver(database),
                new HostFeatureService(
                    database,
                    new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                    []
                ),
                database
            ),
            new(new(strategies))
        );
        var registry = new ChatCommandRegistry(
            [],
            [guessing, new PluginCommandModule(new HostResolver(pluginHostId), dispatch, plugin)],
            []
        );
        var dispatcher = new ChatCommandDispatcher(registry);

        await dispatcher.DispatchResponsesAsync(
            Message("!shared-guesses"),
            new CommandResponder((_, _) => ValueTask.CompletedTask),
            CancellationToken.None
        );

        var routed = guessingCalls.ShouldHaveSingleItem();
        routed.Kind.ShouldBe(GuessCommandKind.Guesses);
        routed
            .State.ShouldBeOfType<AppCommandRouteState.GuessingProfile>()
            .ProfileId.ShouldBe(activeProfileId);
        plugin.Contexts.ShouldBeEmpty();
        dispatch.Current.Commands.ShouldContainKey(new(pluginHostId, "shared-guesses"));
    }

    [Test]
    public async Task TwoHostCommands_HostActionsKeepTheirInvocationContextAndFence()
    {
        var firstHost = Host(1);
        var secondHost = Host(2);
        var hosts = new MultiHostResolver((firstHost, "alpha"), (secondHost, "beta"));
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var runtime = new PluginRuntimeSnapshotRegistry();
        var manifest = ManifestWithCommands("plugin-action");
        var fence = Fence();
        var first = State(manifest, fence, generation: 1, firstHost);
        var second = State(manifest, fence, generation: 2, secondHost);
        declarations.Publish(manifest, fence);
        features.Hydrate([first, second]);
        var chat = new RecordingChatSender();
        var catalog = new PluginHostModuleCatalog(
            [new PluginChatHostModule(hosts, chat)],
            new(features, runtime),
            NullLogger<PluginHostModuleCatalog>.Instance
        );
        var worker = new HostActionWorker(catalog, firstHost, secondHost);
        _ = runtime.Publish(Lifecycle(first, manifest), worker);
        var invoker = new PluginDispatchInvoker(
            new(features, runtime),
            runtime,
            new(),
            TimeProvider.System
        );
        var registry = new ChatCommandRegistry(
            [],
            [new PluginCommandModule(hosts, dispatch, invoker)],
            []
        );
        var dispatcher = new ChatCommandDispatcher(registry);

        await dispatcher.DispatchResponsesAsync(
            Message("alpha", "!plugin-action"),
            new CommandResponder((_, _) => ValueTask.CompletedTask),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("beta", "!plugin-action"),
            new CommandResponder((_, _) => ValueTask.CompletedTask),
            CancellationToken.None
        );

        chat.Messages.ShouldBe([("alpha", "host-1"), ("beta", "host-2")]);
        worker
            .Identities.Select(identity =>
                (identity.Host.Value, identity.Activation!.FeatureGeneration.Value)
            )
            .ShouldBe([(1, 1UL), (2, 2UL)]);
        worker
            .CrossHostOutcome.ShouldBeOfType<PluginHostCallOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginHostFailureCode.ContextNotPermitted);
        worker
            .CrossFenceOutcome.ShouldBeOfType<PluginHostCallOutcome.Cancelled>()
            .Reason.ShouldBe(PluginCancellationReason.PluginDisabled);
    }

    [Test]
    public async Task StandardHostModules_InvokeChatOverlayPointsAndTwitchEffects()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        PluginHostId.TryCreate(hostId, out var pluginHostId).ShouldBeTrue();
        var context = new PluginInvocationContext.Channel(
            Installation(),
            pluginHostId,
            new("moderator", "Moderator", "user-1", false, true, false)
        );
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var chat = new RecordingChatSender();
        var overlays = new RecordingOverlayAdmissions();
        var twitch = new RecordingClipMarkerOperations();
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();

        var chatOutcome = await new PluginChatHostModule(
            new HostResolver(pluginHostId),
            chat
        ).InvokeAsync(
            Call(PluginStandardHostModules.Chat, 0, context, new PluginValue.String("hello")),
            CancellationToken.None
        );
        var overlayOutcome = await new PluginOverlayHostModule(features, overlays).InvokeAsync(
            Call(
                PluginStandardHostModules.Overlay,
                0,
                context,
                new PluginValue.String(targetId.ToString("D")),
                new PluginValue.String(cueId.ToString("D"))
            ),
            CancellationToken.None
        );
        var pointsOutcome = await new PluginPointsHostModule(
            features,
            new PointBalanceService(database)
        ).InvokeAsync(
            Call(
                PluginStandardHostModules.Points,
                0,
                context,
                new PluginValue.String("viewer"),
                new PluginValue.String("7"),
                new PluginValue.String("plugin reward")
            ),
            CancellationToken.None
        );
        var twitchOutcome = await new PluginTwitchHostModule(twitch).InvokeAsync(
            Call(
                PluginStandardHostModules.Twitch,
                0,
                context,
                new PluginValue.String("plugin marker")
            ),
            CancellationToken.None
        );

        _ = chatOutcome.ShouldBeOfType<PluginHostCallOutcome.Returned>();
        _ = overlayOutcome.ShouldBeOfType<PluginHostCallOutcome.Returned>();
        var points = pointsOutcome.ShouldBeOfType<PluginHostCallOutcome.Returned>();
        points.Value.ShouldBe(new PluginValue.String("7"));
        _ = twitchOutcome.ShouldBeOfType<PluginHostCallOutcome.Returned>();
        chat.Messages.ShouldBe([("streamer", "hello")]);
        overlays.Requests.ShouldBe([(hostId, targetId, cueId)]);
        twitch.Markers.ShouldBe([(hostId, "plugin marker")]);
        await using var db = await database.CreateDbContextAsync();
        var balance = await db.PointBalances.SingleAsync(CancellationToken.None);
        balance.Login.ShouldBe("viewer");
        balance.Amount.ShouldBe("7");
    }

    [Test]
    public async Task HotPublishedTwitchEventHandler_ReceivesTheTypedEventWithoutRestart()
    {
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var manifest = ManifestWithEvent();
        var fence = Fence();
        var state = State(manifest, fence, generation: 1);
        var invoker = new RecordingDispatchInvoker();
        var bridge = new PluginTwitchEventBridge(
            new HostResolver(state.Key.HostId),
            dispatch,
            invoker
        );
        declarations.Publish(manifest, fence);
        features.Publish(state);
        var occurredAt = DateTimeOffset.UtcNow;

        await bridge.StreamOnlineAsync(
            new(
                "event-1",
                occurredAt,
                "host-user-1",
                "streamer",
                "Streamer",
                "stream-1",
                "live",
                occurredAt
            ),
            CancellationToken.None
        );

        var context = invoker.EventContexts.ShouldHaveSingleItem();
        _ = context.Event.ShouldNotBeNull();
        context.Event!.EventId.ShouldBe("event-1");
        context.Event.HandlerId.Value.ShouldBe("stream-online");
        context.Stream.ShouldBe(new PluginStreamContext("stream-1", true));
        invoker.Events.ShouldHaveSingleItem().State.Key.ShouldBe(state.Key);
    }

    [Test]
    public async Task ReadyTwitchHandler_RequiresItsEventSubGroupAndTriggersReconciliation()
    {
        var dispatch = new PluginDispatchSnapshotRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch);
        var features = new PluginFeatureSnapshotRegistry(dispatch);
        var runtime = new PluginRuntimeSnapshotRegistry();
        var manifest = ManifestWithEvent();
        var fence = Fence();
        var state = State(manifest, fence, generation: 1);
        declarations.Publish(manifest, fence);
        features.Publish(state);
        _ = runtime.Publish(Lifecycle(state, manifest), new PassiveWorker());
        var requirements = new PluginEventSubRequirementSource(
            new HostResolver(state.Key.HostId),
            declarations,
            features,
            runtime
        );
        var trigger = new RecordingReconciliationTrigger();
        var reconciler = new PluginFeatureEventSubReconciler(
            new HostResolver(state.Key.HostId),
            new ReadyTokenStatusProvider(),
            trigger
        );
        var feature = manifest.Manifest.Features.Single(item => item.Id == state.Key.FeatureId);

        var required = await requirements.RequiresAsync(
            "streamer",
            AutomationEventSubRequirement.Stream,
            CancellationToken.None
        );

        var outcome = await reconciler.ReconcileAsync(
            new(state.Key, state.Fence, state.Generation, feature.Twitch),
            CancellationToken.None
        );

        required.ShouldBeTrue();
        _ = outcome.ShouldBeOfType<PluginFeatureReconciliationResult.Pending>();
        trigger.Calls.ShouldBe(1);
    }

    [Test]
    public async Task DurableSchedule_ReopensWithItsExactActivationFenceAndPayload()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-plugin-schedule-{Guid.NewGuid():N}"
        );
        var path = Path.Combine(directory, "plugin-schedules.json");
        var entry = ScheduleEntry();
        try
        {
            using (var first = new PluginScheduleFileStore(path))
            {
                await first.UpsertAsync(entry, CancellationToken.None);
            }

            using var restarted = new PluginScheduleFileStore(path);
            var restored = (
                await restarted.LoadAsync(CancellationToken.None)
            ).ShouldHaveSingleItem();

            (restored with { Input = entry.Input }).ShouldBe(entry);
            PluginValueComparer.SemanticallyEquals(restored.Input, entry.Input).ShouldBeTrue();
            await restarted.RemoveFeatureAsync(entry.Feature, entry.Fence, CancellationToken.None);
            (await restarted.LoadAsync(CancellationToken.None)).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task RestartedSchedule_WaitsForFeatureHydrationThenInvokesTheCurrentHandler()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-plugin-schedule-worker-{Guid.NewGuid():N}"
        );
        var path = Path.Combine(directory, "plugin-schedules.json");
        var manifest = ManifestWithSchedule();
        var fence = Fence();
        var state = State(manifest, fence, generation: 1);
        PluginScheduleHandlerId.TryCreate("refresh", out var handlerId).ShouldBeTrue();
        var entry = new PluginScheduleEntry(
            Guid.NewGuid(),
            state.Key,
            new(state.Fence, state.Generation),
            handlerId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            new PluginValue.Map([new("cursor", new PluginValue.String("restart"))])
        );
        try
        {
            using (var beforeRestart = new PluginScheduleFileStore(path))
            {
                await beforeRestart.UpsertAsync(entry, timeout.Token);
            }

            using var restarted = new PluginScheduleFileStore(path);
            var dispatch = new PluginDispatchSnapshotRegistry();
            var declarations = new PluginFeatureDeclarationRegistry(dispatch);
            var features = new PluginFeatureSnapshotRegistry(dispatch);
            var invoker = new RecordingDispatchInvoker();
            var worker = new PluginScheduleWorker(
                restarted,
                dispatch,
                features,
                invoker,
                TimeProvider.System,
                NullLogger<PluginScheduleWorker>.Instance
            );
            await worker.StartAsync(timeout.Token);

            declarations.Publish(manifest, fence);
            features.Publish(state);
            await invoker.ScheduleInvoked.Task.WaitAsync(timeout.Token);
            while ((await restarted.LoadAsync(timeout.Token)).Count != 0)
            {
                await Task.Delay(10, timeout.Token);
            }
            await worker.StopAsync(timeout.Token);

            invoker.Schedules.ShouldHaveSingleItem().State.Key.ShouldBe(state.Key);
            PluginValueComparer
                .SemanticallyEquals(invoker.ScheduleInputs.ShouldHaveSingleItem(), entry.Input)
                .ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task RecurringSchedule_CancelledDuringCallback_DoesNotReappearAfterCompletion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-plugin-schedule-cancel-{Guid.NewGuid():N}"
        );
        var path = Path.Combine(directory, "plugin-schedules.json");
        var manifest = ManifestWithSchedule();
        var fence = Fence();
        var state = State(manifest, fence, generation: 1);
        PluginScheduleHandlerId.TryCreate("refresh", out var handlerId).ShouldBeTrue();
        var entry = new PluginScheduleEntry(
            Guid.NewGuid(),
            state.Key,
            new(state.Fence, state.Generation),
            handlerId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            3_600,
            new PluginValue.Map([])
        );
        try
        {
            using var store = new PluginScheduleFileStore(path);
            await store.UpsertAsync(entry, timeout.Token);
            var dispatch = new PluginDispatchSnapshotRegistry();
            var declarations = new PluginFeatureDeclarationRegistry(dispatch);
            var features = new PluginFeatureSnapshotRegistry(dispatch);
            declarations.Publish(manifest, fence);
            features.Publish(state);
            var invoker = new BlockingScheduleDispatchInvoker();
            var worker = new PluginScheduleWorker(
                store,
                dispatch,
                features,
                invoker,
                TimeProvider.System,
                NullLogger<PluginScheduleWorker>.Instance
            );
            await worker.StartAsync(timeout.Token);
            await invoker.ScheduleInvoked.Task.WaitAsync(timeout.Token);
            (await store.LoadAsync(timeout.Token))
                .ShouldHaveSingleItem()
                .DueAtUtc.ShouldBeGreaterThan(entry.DueAtUtc);
            var context = new PluginInvocationContext.Channel(
                Declaration(manifest, fence).Installation,
                state.Key.HostId
            );
            var schedules = new PluginSchedulesHostModule(store, dispatch, TimeProvider.System);

            var cancelled = await schedules.InvokeAsync(
                Identity(context, state, state.Generation.Value),
                Call(
                    PluginStandardHostModules.Schedules,
                    2,
                    context,
                    new PluginValue.String(entry.Id.ToString("D"))
                ),
                timeout.Token
            );
            invoker.Complete();
            await invoker.ScheduleCompleted.Task.WaitAsync(timeout.Token);
            await worker.StopAsync(timeout.Token);

            _ = cancelled.ShouldBeOfType<PluginHostCallOutcome.Returned>();
            (await store.LoadAsync(timeout.Token)).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task AbruptStopAfterDurableConsumption_RestartDoesNotReplayTheOccurrence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-plugin-schedule-abrupt-{Guid.NewGuid():N}"
        );
        var path = Path.Combine(directory, "plugin-schedules.json");
        var manifest = ManifestWithSchedule();
        var fence = Fence();
        var state = State(manifest, fence, generation: 1);
        PluginScheduleHandlerId.TryCreate("refresh", out var handlerId).ShouldBeTrue();
        var entry = new PluginScheduleEntry(
            Guid.NewGuid(),
            state.Key,
            new(state.Fence, state.Generation),
            handlerId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            new PluginValue.Map([])
        );
        try
        {
            var dispatch = new PluginDispatchSnapshotRegistry();
            var declarations = new PluginFeatureDeclarationRegistry(dispatch);
            var features = new PluginFeatureSnapshotRegistry(dispatch);
            declarations.Publish(manifest, fence);
            features.Publish(state);
            var firstInvoker = new RecordingDispatchInvoker();
            using (var fileStore = new PluginScheduleFileStore(path))
            {
                await fileStore.UpsertAsync(entry, timeout.Token);
                var interruptedStore = new InterruptAfterConsumptionStore(fileStore);
                var interruptedWorker = new PluginScheduleWorker(
                    interruptedStore,
                    dispatch,
                    features,
                    firstInvoker,
                    TimeProvider.System,
                    NullLogger<PluginScheduleWorker>.Instance
                );
                await interruptedWorker.StartAsync(timeout.Token);
                await interruptedStore.Consumed.Task.WaitAsync(timeout.Token);
                await interruptedWorker.StopAsync(timeout.Token);
            }

            using var restartedFileStore = new PluginScheduleFileStore(path);
            var restartedStore = new LoadObservingScheduleStore(restartedFileStore);
            var restartedInvoker = new RecordingDispatchInvoker();
            var restartedWorker = new PluginScheduleWorker(
                restartedStore,
                dispatch,
                features,
                restartedInvoker,
                TimeProvider.System,
                NullLogger<PluginScheduleWorker>.Instance
            );
            await restartedWorker.StartAsync(timeout.Token);
            await restartedStore.Loaded.Task.WaitAsync(timeout.Token);
            await restartedWorker.StopAsync(timeout.Token);

            firstInvoker.Schedules.ShouldBeEmpty();
            restartedInvoker.Schedules.ShouldBeEmpty();
            (await restartedFileStore.LoadAsync(timeout.Token)).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task StaleActivation_HostCatalogRejectsBeforeTheEffectRuns()
    {
        var module = new CountingHostModule();
        var features = new PluginFeatureSnapshotRegistry();
        var manifest = ManifestWithCommands("plugin-only");
        var fence = Fence();
        var current = State(manifest, fence, generation: 2);
        features.Publish(current);
        var catalog = new PluginHostModuleCatalog(
            [module],
            new(features, new UnexpectedRuntime()),
            NullLogger<PluginHostModuleCatalog>.Instance
        );
        var context = new PluginInvocationContext.Channel(
            Declaration(manifest, fence).Installation,
            current.Key.HostId
        );
        var identity = Identity(context, current, featureGeneration: 1);

        var outcome = await catalog.DispatchAsync(
            identity,
            Call(module.Descriptor, 0, context, new PluginValue.String("stale")),
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<PluginHostCallOutcome.Cancelled>();
        module.InvocationCount.ShouldBe(0);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "host-1",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<(int HostId, int ActiveProfileId)> SeedSharedGuessingAliasRoundAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        var hostId = await SeedHostAsync(database);
        await using var db = await database.CreateDbContextAsync();
        var first = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "First",
            Slug = "first",
            IsDefault = true,
            Revision = 1,
        };
        var active = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "Active",
            Slug = "active",
            Revision = 1,
        };
        db.Profiles.AddRange(first, active);
        _ = await db.SaveChangesAsync();
        db.CommandAliases.AddRange(
            new CommandAlias
            {
                HostId = hostId,
                GuessRoundProfileId = first.Id,
                Kind = AppCommandKind.Guesses,
                Alias = "shared-guesses",
            },
            new CommandAlias
            {
                HostId = hostId,
                GuessRoundProfileId = active.Id,
                Kind = AppCommandKind.Guesses,
                Alias = "shared-guesses",
            }
        );
        _ = db.Rounds.Add(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = active.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return (hostId, active.Id);
    }

    private static ValidatedPluginManifest ManifestWithCommands(params string[] routes)
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("handle", out var operation).ShouldBeTrue();
        var modified = accepted.Manifest with
        {
            Features = accepted.Manifest.Features.Replace(
                feature,
                feature with
                {
                    Dispatch = new(
                        [
                            .. routes.Select(route => new PluginCommandDescriptor(
                                route,
                                module,
                                operation,
                                PluginCallbackRequirements.Independent
                            )),
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

    private static ValidatedPluginManifest ManifestWithSchedule()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("refresh", out var operation).ShouldBeTrue();
        PluginScheduleHandlerId.TryCreate("refresh", out var handler).ShouldBeTrue();
        var modified = accepted.Manifest with
        {
            Features = accepted.Manifest.Features.Replace(
                feature,
                feature with
                {
                    Dispatch = new(
                        [],
                        [],
                        [new(handler, module, operation, PluginCallbackRequirements.Independent)]
                    ),
                }
            ),
        };
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(modified, PluginContractFixtures.CompatibleHost())
        ).Manifest;
    }

    private static ValidatedPluginManifest ManifestWithEvent()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("stream_online", out var operation).ShouldBeTrue();
        PluginEventHandlerId.TryCreate("stream-online", out var handler).ShouldBeTrue();
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
                        [],
                        [
                            new(
                                handler,
                                new PluginEventSource.Twitch(PluginTwitchEventKind.StreamOnline),
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
        ulong generation
    ) => State(manifest, fence, generation, Host(1));

    private static PluginFeatureState State(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence,
        ulong generation,
        PluginHostId host
    )
    {
        PluginFeatureGeneration.TryCreate(generation, out var featureGeneration).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(checked((long)generation), out var revision).ShouldBeTrue();
        var feature = manifest.Manifest.Features.Single(item => item.Id.Value == "collection");
        return new(
            new(manifest.Manifest.Id, feature.Id, host),
            fence,
            featureGeneration,
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

    private static PluginHostId Host(int value)
    {
        PluginHostId.TryCreate(value, out var host).ShouldBeTrue();
        return host;
    }

    private static PluginLifecycleFence Fence()
    {
        PluginWorkerGeneration.TryCreate(1, out var workerGeneration).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), workerGeneration);
    }

    private static PluginInstallationIdentity Installation()
    {
        PluginGitTag.TryCreate("community-link-queue", out var tag).ShouldBeTrue();
        SemanticVersion.TryCreate("1.2.0", out var version).ShouldBeTrue();
        return new(PluginContractFixtures.PluginId("community.link-queue"), new(version, tag));
    }

    private static PluginHostCall Call(
        PluginHostModuleDescriptor module,
        int operation,
        PluginInvocationContext context,
        params PluginValue[] arguments
    ) =>
        new(
            PluginContractFixtures.HostCallId(),
            PluginContractFixtures.CoroutineId(),
            module.Id,
            module.Operations[operation].Id,
            context,
            [.. arguments]
        );

    private static PluginWorkerInvocationIdentity Identity(
        PluginInvocationContext.Channel context,
        PluginFeatureState state,
        ulong featureGeneration
    )
    {
        PluginFeatureActivationGeneration
            .TryCreate(featureGeneration, out var activationGeneration)
            .ShouldBeTrue();
        PluginActivationOperationId
            .TryCreate(state.Fence.OperationId.Value, out var activationOperation)
            .ShouldBeTrue();
        PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId).ShouldBeTrue();
        PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellationId).ShouldBeTrue();
        return new(
            context.Plugin,
            state.Key.FeatureId,
            state.Key.HostId,
            context,
            invocationId,
            PluginContractFixtures.CoroutineId(),
            state.Fence.Generation,
            PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddSeconds(10)),
            cancellationId,
            new(activationOperation, state.Fence.Generation, activationGeneration)
        );
    }

    private static PluginScheduleEntry ScheduleEntry()
    {
        PluginFeatureId.TryCreate("collection", out var featureId).ShouldBeTrue();
        PluginHostId.TryCreate(1, out var hostId).ShouldBeTrue();
        PluginScheduleHandlerId.TryCreate("refresh", out var handlerId).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(4, out var featureGeneration).ShouldBeTrue();
        PluginWorkerGeneration.TryCreate(3, out var workerGeneration).ShouldBeTrue();
        return new(
            Guid.NewGuid(),
            new(PluginContractFixtures.PluginId("community.link-queue"), featureId, hostId),
            new(new(PluginLifecycleOperationId.New(), workerGeneration), featureGeneration),
            handlerId,
            DateTimeOffset.UtcNow.AddMinutes(5),
            60,
            new PluginValue.Map([new("cursor", new PluginValue.String("next"))])
        );
    }

    private static ChatMessage Message(string text) => Message("streamer", text);

    private static ChatMessage Message(string channel, string text) =>
        new(
            "viewer",
            channel,
            text,
            $":viewer!user@host PRIVMSG #{channel} :{text}",
            new Dictionary<string, string> { ["display-name"] = "Viewer" }
        );

    private sealed class CustomShadowModule(string route) : IChatCommandModule
    {
        internal int HandledCount { get; private set; }

        public void AddCommands(IChatCommandBuilder commands) => commands.MapDynamic(HandleAsync);

        private ValueTask<CommandHandlingOutcome> HandleAsync(
            ChatCommandContext context,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken
        )
        {
            if (!string.Equals(context.CommandName, route, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<CommandHandlingOutcome>(
                    new CommandHandlingOutcome.Unhandled()
                );
            }
            HandledCount++;
            return ValueTask.FromResult<CommandHandlingOutcome>(
                new CommandHandlingOutcome.Handled()
            );
        }
    }

    private class RecordingDispatchInvoker : IPluginDispatchInvoker
    {
        internal List<PluginInvocationContext.Channel> Contexts { get; } = [];

        internal List<PluginDispatchEndpoint.Schedule> Schedules { get; } = [];

        internal List<PluginDispatchEndpoint.Event> Events { get; } = [];

        internal List<PluginInvocationContext.Channel> EventContexts { get; } = [];

        internal List<PluginValue> ScheduleInputs { get; } = [];

        internal TaskCompletionSource ScheduleInvoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<PluginDispatchInvocationOutcome> InvokeCommandAsync(
            PluginDispatchEndpoint.Command endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Contexts.Add(context);
            return ValueTask.FromResult<PluginDispatchInvocationOutcome>(
                new PluginDispatchInvocationOutcome.Returned(new PluginValue.Nil())
            );
        }

        public ValueTask<PluginDispatchInvocationOutcome> InvokeEventAsync(
            PluginDispatchEndpoint.Event endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Events.Add(endpoint);
            EventContexts.Add(context);
            return ValueTask.FromResult<PluginDispatchInvocationOutcome>(
                new PluginDispatchInvocationOutcome.Returned(new PluginValue.Nil())
            );
        }

        public virtual ValueTask<PluginDispatchInvocationOutcome> InvokeScheduleAsync(
            PluginDispatchEndpoint.Schedule endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Schedules.Add(endpoint);
            ScheduleInputs.Add(input);
            _ = ScheduleInvoked.TrySetResult();
            return ValueTask.FromResult<PluginDispatchInvocationOutcome>(
                new PluginDispatchInvocationOutcome.Returned(new PluginValue.Nil())
            );
        }
    }

    private sealed class BlockingScheduleDispatchInvoker : RecordingDispatchInvoker
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal TaskCompletionSource ScheduleCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Complete() => _release.TrySetResult();

        public override async ValueTask<PluginDispatchInvocationOutcome> InvokeScheduleAsync(
            PluginDispatchEndpoint.Schedule endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            _ = base.InvokeScheduleAsync(endpoint, context, input, cancellationToken);
            await _release.Task.WaitAsync(cancellationToken);
            _ = ScheduleCompleted.TrySetResult();
            return new PluginDispatchInvocationOutcome.Returned(new PluginValue.Nil());
        }
    }

    private abstract class DelegatingScheduleStore(IPluginScheduleStore inner)
        : IPluginScheduleStore
    {
        protected IPluginScheduleStore Inner { get; } = inner;

        public virtual ValueTask<IReadOnlyList<PluginScheduleEntry>> LoadAsync(
            CancellationToken cancellationToken
        ) => Inner.LoadAsync(cancellationToken);

        public ValueTask UpsertAsync(
            PluginScheduleEntry entry,
            CancellationToken cancellationToken
        ) => Inner.UpsertAsync(entry, cancellationToken);

        public virtual ValueTask<bool> TryConsumeOccurrenceAsync(
            PluginScheduleEntry observed,
            DateTimeOffset? nextDueAtUtc,
            CancellationToken cancellationToken
        ) => Inner.TryConsumeOccurrenceAsync(observed, nextDueAtUtc, cancellationToken);

        public ValueTask RemoveAsync(Guid scheduleId, CancellationToken cancellationToken) =>
            Inner.RemoveAsync(scheduleId, cancellationToken);

        public ValueTask RemoveFeatureAsync(
            PluginFeatureKey feature,
            PluginFeatureFence fence,
            CancellationToken cancellationToken
        ) => Inner.RemoveFeatureAsync(feature, fence, cancellationToken);

        public ValueTask RemovePluginAsync(
            PluginId pluginId,
            CancellationToken cancellationToken
        ) => Inner.RemovePluginAsync(pluginId, cancellationToken);
    }

    private sealed class InterruptAfterConsumptionStore(IPluginScheduleStore inner)
        : DelegatingScheduleStore(inner)
    {
        internal TaskCompletionSource Consumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<bool> TryConsumeOccurrenceAsync(
            PluginScheduleEntry observed,
            DateTimeOffset? nextDueAtUtc,
            CancellationToken cancellationToken
        )
        {
            var consumed = await Inner.TryConsumeOccurrenceAsync(
                observed,
                nextDueAtUtc,
                cancellationToken
            );
            if (!consumed)
            {
                return false;
            }
            _ = Consumed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }
    }

    private sealed class LoadObservingScheduleStore(IPluginScheduleStore inner)
        : DelegatingScheduleStore(inner)
    {
        internal TaskCompletionSource Loaded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<IReadOnlyList<PluginScheduleEntry>> LoadAsync(
            CancellationToken cancellationToken
        )
        {
            var entries = await Inner.LoadAsync(cancellationToken);
            _ = Loaded.TrySetResult();
            return entries;
        }
    }

    private sealed class RecordingGuessingStrategy(
        GuessCommandKind kind,
        List<(GuessCommandKind Kind, AppCommandRouteState State)> calls
    ) : ICommandStrategy<GuessCommandKind, AppCommandRouteState>
    {
        public GuessCommandKind Kind { get; } = kind;

        public IReadOnlyList<string> DefaultAliases => [];

        public CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access { get; } =
            new CommandStrategyAccess<GuessCommandKind, AppCommandRouteState>.Everyone();

        public ValueTask ExecuteAsync(
            CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
            CancellationToken cancellationToken
        )
        {
            calls.Add((context.Kind, context.State));
            return ValueTask.CompletedTask;
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

    private sealed class MultiHostResolver(params (PluginHostId Id, string Login)[] configuredHosts)
        : IPluginHostContextResolver
    {
        private readonly IReadOnlyDictionary<PluginHostId, string> _hosts =
            configuredHosts.ToDictionary(static host => host.Id, static host => host.Login);

        public ValueTask<PluginHostContext?> FindAsync(
            PluginHostId hostId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostContext?>(
                _hosts.TryGetValue(hostId, out var login) ? new(hostId, login) : null
            );

        public ValueTask<PluginHostContext?> FindAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            var host = _hosts.FirstOrDefault(pair =>
                string.Equals(pair.Value, channelLogin, StringComparison.OrdinalIgnoreCase)
            );
            return ValueTask.FromResult<PluginHostContext?>(
                host.Key is null ? null : new(host.Key, host.Value)
            );
        }
    }

    private sealed class RecordingChatSender : IPublicChatMessageSender
    {
        internal List<(string Channel, string Message)> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Add((channel, message));
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
        }
    }

    private sealed class RecordingOverlayAdmissions : IOverlayCueAdmissionService
    {
        internal List<(int HostId, Guid TargetId, Guid CueId)> Requests { get; } = [];

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add((request.HostId, request.TargetOverlayId, request.CueId));
            return Task.FromResult<OverlayCueReferenceOutcome>(
                new OverlayCueReferenceOutcome.Available()
            );
        }

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new OverlayCueAdmissionCatalog(
                    [],
                    Requests.Count == 0
                        ? []
                        : [new(Requests[^1].CueId, "Plugin cue", OverlayCueQueuePolicy.Enqueue)]
                )
            );

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<OverlayCueAdmissionOutcome>(
                new OverlayCueAdmissionOutcome.Running(Guid.NewGuid())
            );
    }

    private sealed class RecordingClipMarkerOperations : IClipMarkerDashboardOperations
    {
        internal List<(int HostId, string Description)> Markers { get; } = [];

        public Task<ClipMarkerOperationOutcome> CreateMarkerAsync(
            int hostId,
            string description,
            CancellationToken cancellationToken
        )
        {
            Markers.Add((hostId, description));
            return Task.FromResult<ClipMarkerOperationOutcome>(
                new ClipMarkerOperationOutcome.MarkerCreated(
                    new(
                        new(1),
                        "Succeeded",
                        "marker-1",
                        description,
                        1,
                        null,
                        null,
                        null,
                        DateTime.UtcNow
                    )
                )
            );
        }

        public Task<ClipMarkerDashboardState> LoadAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ClipMarkerOperationOutcome> CreateClipAsync(
            int hostId,
            bool hasDelay,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ClipMarkerOperationOutcome> RetryClipAsync(
            int hostId,
            ClipAttemptReference attempt,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ClipMarkerOperationOutcome> RetryMarkerAsync(
            int hostId,
            StreamMarkerAttemptReference attempt,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class CountingHostModule : IPluginHostModule
    {
        public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Chat;

        internal int InvocationCount { get; private set; }

        public ValueTask<PluginHostCallOutcome> InvokeAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        )
        {
            InvocationCount++;
            return ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Returned(new PluginValue.Nil())
            );
        }
    }

    private sealed class HostActionWorker(
        IPluginHostCallDispatcher hostCalls,
        PluginHostId firstHost,
        PluginHostId secondHost
    ) : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ).Task;

        internal List<PluginWorkerInvocationIdentity> Identities { get; } = [];

        internal PluginHostCallOutcome? CrossHostOutcome { get; private set; }

        internal PluginHostCallOutcome? CrossFenceOutcome { get; private set; }

        public async ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            Identities.Add(identity);
            var context = (PluginInvocationContext.Channel)identity.Context;
            if (identity.Host == firstHost)
            {
                var crossHostContext = context with { Host = secondHost };
                CrossHostOutcome = await hostCalls.DispatchAsync(
                    identity,
                    Call(
                        PluginStandardHostModules.Chat,
                        0,
                        crossHostContext,
                        new PluginValue.String("cross-host")
                    ),
                    cancellationToken
                );
                CrossFenceOutcome = await hostCalls.DispatchAsync(
                    identity with
                    {
                        Host = secondHost,
                        Context = crossHostContext,
                    },
                    Call(
                        PluginStandardHostModules.Chat,
                        0,
                        crossHostContext,
                        new PluginValue.String("cross-fence")
                    ),
                    cancellationToken
                );
            }
            var outcome = await hostCalls.DispatchAsync(
                identity,
                Call(
                    PluginStandardHostModules.Chat,
                    0,
                    context,
                    new PluginValue.String($"host-{identity.Host.Value}")
                ),
                cancellationToken
            );
            return new(
                outcome is PluginHostCallOutcome.Returned returned
                    ? new PluginWorkerInvocationOutcome.Returned(returned.Value)
                    : new PluginWorkerInvocationOutcome.Failed(
                        new(
                            PluginWorkerFailureCode.ProtocolViolation,
                            "The host action journey failed."
                        )
                    ),
                PluginWorkerInvocationMetrics.Empty,
                []
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PassiveWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ).Task;

        public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("The passive worker received an invocation.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnexpectedRuntime : IPluginRuntimeSnapshotProvider
    {
        public PluginRuntimeSnapshot Current => PluginRuntimeSnapshot.Empty;

        public PluginAdmissionOutcome Admit(
            PluginId pluginId,
            PluginLifecycleFence expected,
            PluginFeatureAdmissionReadiness readiness
        ) => throw new InvalidOperationException("A stale feature reached runtime admission.");

        public PluginFenceOutcome ValidateCallbackCompletion(
            PluginId pluginId,
            PluginLifecycleFence fence
        ) => throw new NotSupportedException();

        public PluginFenceOutcome ValidateWorkerResult(
            PluginId pluginId,
            PluginLifecycleFence fence
        ) => throw new NotSupportedException();

        public PluginFenceOutcome ValidateCancellation(
            PluginId pluginId,
            PluginLifecycleFence fence
        ) => throw new NotSupportedException();

        public PluginAdmissionOutcome AdmitDurableRun(
            PluginId pluginId,
            PluginLifecycleFence expected,
            PluginFeatureAdmissionReadiness readiness
        ) => throw new NotSupportedException();
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

    private sealed class RecordingReconciliationTrigger : IEventSubChannelReconciliationTrigger
    {
        internal int Calls { get; private set; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}

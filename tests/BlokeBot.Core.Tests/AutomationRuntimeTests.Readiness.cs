using System.Collections.Immutable;
using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(ChatMatchKind.Keyword, "tea", "TEA!")]
    [Arguments(ChatMatchKind.Phrase, "hello tea", "Well HELLO TEA!")]
    [Arguments(ChatMatchKind.Emote, "25", "Kappa")]
    public async Task Readiness_OrdinaryOfflineChatAndFirstObservedKeepDistinctAdmission(
        ChatMatchKind kind,
        string match,
        string text
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            $$"""{"kind":"{{kind}}","match":"{{match}}"}""",
            "ordinary"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = ExpandedFixtureFor(f);
        var readiness = ReadinessFor(f, x.Runtime, x.Tokens);
        var initial = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = Configured(initial, "First observed chatter")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationStreamObservations.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        await x.Runtime.TickAsync(CancellationToken.None);
        var offline = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = Configured(offline, $"{kind}: {match}")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        Configured(offline, "First observed chatter")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
            .Reason.ShouldContain("confirmed offline");
        var message = ReadinessChat(f, "offline", text) with { EmoteIds = ["25"] };
        await x.Runtime.ChatReceivedAsync(message, CancellationToken.None);
        f.Chat.Messages.ShouldBe(["ordinary"]);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "actual-stream",
            f.Clock.GetUtcNow()
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        _ = Configured(await ReadinessSourcesAsync(readiness, f.HostId), "First observed chatter")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        await x.Runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "live",
            },
            CancellationToken.None
        );
        f.Chat.Messages.Count(static value => value == "first").ShouldBe(1);
        await x.Runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "seen",
            },
            CancellationToken.None
        );
        f.Chat.Messages.Count(static value => value == "first").ShouldBe(1);
    }

    [Test]
    public async Task Readiness_InitialUnknownAndReconnectedStreamNeedActualObservation()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "stream-uptime",
            """{"kind":"Threshold","duration-seconds":600}""",
            "uptime"
        );
        var x = ExpandedFixtureFor(f);
        var readiness = ReadinessFor(f, x.Runtime, x.Tokens);
        x.Runtime.ObserveConnectionChanges(true);
        try
        {
            x.Streams.Current = new HostStreamLivenessOutcome.Live(
                "actual-stream",
                f.Clock.GetUtcNow()
            );
            Configured(await ReadinessSourcesAsync(readiness, f.HostId), "First observed chatter")
                .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
                .Reason.ShouldContain("Waiting");
            await x.Runtime.TickAsync(CancellationToken.None);
            _ = Configured(
                await ReadinessSourcesAsync(readiness, f.HostId),
                "First observed chatter"
            )
                .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
            x.Connection.Current = new BotRuntimeStatus.Authorized();
            Configured(await ReadinessSourcesAsync(readiness, f.HostId), "First observed chatter")
                .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
                .Reason.ShouldContain("not connected");
            x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
            Configured(await ReadinessSourcesAsync(readiness, f.HostId), "First observed chatter")
                .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
                .Reason.ShouldContain("Waiting");
            x.Streams.Current = new HostStreamLivenessOutcome.Unavailable(
                HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                new HttpRequestException()
            );
            await x.Runtime.TickAsync(CancellationToken.None);
            Configured(await ReadinessSourcesAsync(readiness, f.HostId), "First observed chatter")
                .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
                .Reason.ShouldContain("live status is unavailable");
            f.Chat.Messages.ShouldBeEmpty();
        }
        finally
        {
            x.Runtime.ObserveConnectionChanges(false);
        }
    }

    [Test]
    public async Task Readiness_GrantsFollowEligibleCompleteFlowsIncludingCalledBackingFeatures()
    {
        var overlays = new HostBoundOverlayCues();
        await using var f = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var target = Guid.NewGuid();
        var cue = Guid.NewGuid();
        overlays.AddTarget(f.HostId, target, OverlayType.CuePlayer);
        overlays.AddCue(f.HostId, cue, OverlayCueQueuePolicy.Replace);
        var reusable = Subflow(f.HostId);
        var action = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}"""
        );
        reusable = reusable with
        {
            Graph = reusable.Graph with
            {
                Nodes = [reusable.Graph.Nodes[0], action, reusable.Graph.Nodes[1]],
                Edges =
                [
                    Edge(reusable.Graph.Nodes[0], "complete", action),
                    Edge(action, "complete", reusable.Graph.Nodes[1]),
                ],
            },
        };
        var revision = await Publish(Subflows(f), reusable);
        var source = Node("twitch-goal", """{"event":"Milestone","milestone":10}""");
        var call = Invoke(revision);
        _ = await f.SaveAsync([source, call], [Edge(source, "flow", call)]);
        var cheer = await SaveExpandedChatAsync(f, "cheer", """{"minimum-bits":1}""", "cheer");
        var ads = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"Started","offset-seconds":0}""",
            "ads"
        );
        _ = await f.Flows.SetEnabledAsync(new(f.HostId), ads, false, CancellationToken.None);
        _ = await f.Features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Overlays,
            CancellationToken.None
        );
        var x = ExpandedFixtureFor(f);
        var tokens = new ReadinessTokens();
        tokens.Granted.ExceptWith([
            "bits:read",
            "channel:read:hype_train",
            "channel:read:ads",
            "channel:read:goals",
        ]);
        var readiness = ReadinessFor(f, x.Runtime, tokens);
        var available = await ReadinessSourcesAsync(readiness, f.HostId);
        available.MissingBroadcasterScopes.ShouldBe(["bits:read"]);
        var goal = available.Sources.Single(static row =>
            row.DefinitionId == AutomationDefinitionIds.GoalSource
        );
        goal.UsedByEnabledFlow.ShouldBeFalse();
        goal.Configurations.Single()
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Unavailable>()
            .Reason.ShouldContain("Overlays");
        _ = available
            .Sources.Single(static row =>
                row.DefinitionId == AutomationDefinitionIds.AdTimingSource
            )
            .Configurations.Single()
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Disabled>();
        (await x.Runtime.GetRequirementsAsync("streamer", CancellationToken.None)).ShouldBeEmpty();
        var scopes = await readiness.AuthorizationScopesAsync(f.HostId, CancellationToken.None);
        scopes.ShouldContain("bits:read"); // unchanged OAuth baseline
        scopes.ShouldNotContain("channel:read:ads");
        scopes.ShouldNotContain("channel:read:goals");
        await x.Runtime.GoalChangedAsync(
            new(
                "goal",
                f.Clock.GetUtcNow(),
                "streamer-id",
                "identity",
                "follower",
                EventSubGoalStage.Progress,
                10,
                100,
                false
            ),
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBeEmpty();
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        _ = await f.Flows.SetEnabledAsync(new(f.HostId), cheer, false, CancellationToken.None);
        (await ReadinessSourcesAsync(readiness, f.HostId)).MissingBroadcasterScopes.ShouldBeEmpty();
    }

    [Test]
    public async Task Readiness_InvalidFlowAndUnknownGrantAreNotReadyOrReconnectAuthority()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var flow = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"Started","offset-seconds":0}""",
            "ads"
        );
        var x = ExpandedFixtureFor(f);
        var tokens = new ReadinessTokens { Unknown = true };
        var readiness = ReadinessFor(f, x.Runtime, tokens);
        var before = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = before
            .Sources.Single(static row =>
                row.DefinitionId == AutomationDefinitionIds.AdTimingSource
            )
            .Configurations.Single()
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>();
        before.MissingBroadcasterScopes.ShouldBeEmpty();
        await using var db = await f.Database.CreateDbContextAsync();
        _ = await db
            .AutomationFlows.Where(value => value.Id == flow.Value)
            .ExecuteUpdateAsync(set => set.SetProperty(value => value.SchemaVersion, 999));
        var after = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = after
            .Sources.Single(static row =>
                row.DefinitionId == AutomationDefinitionIds.AdTimingSource
            )
            .Configurations.Single()
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Unavailable>();
        (await x.Runtime.GetRequirementsAsync("streamer", CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Readiness_ObservationNotificationsAreHostScopedAndStableTicksStayQuiet()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = ExpandedFixtureFor(f);
        var changed = 0;
        var foreign = 0;
        using var own = f.UiChanges.Subscribe(
            new AutomationUiChange.SourceReadiness(new(f.HostId)),
            ObserverIdentity.Named("readiness"),
            (_, _) =>
            {
                changed++;
                return ValueTask.CompletedTask;
            }
        );
        using var other = f.UiChanges.Subscribe(
            new AutomationUiChange.SourceReadiness(new(f.HostId + 1)),
            ObserverIdentity.Named("other-readiness"),
            (_, _) =>
            {
                foreign++;
                return ValueTask.CompletedTask;
            }
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        changed.ShouldBe(1);
        await x.Runtime.TickAsync(CancellationToken.None);
        changed.ShouldBe(1);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "actual-stream",
            f.Clock.GetUtcNow()
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        changed.ShouldBe(2);
        await x.Runtime.TickAsync(CancellationToken.None);
        changed.ShouldBe(2);
        await x.Runtime.StreamEndedAsync(
            new("offline", f.Clock.GetUtcNow(), "streamer-id", "streamer", "Streamer"),
            CancellationToken.None
        );
        changed.ShouldBe(3);
        foreign.ShouldBe(0);
    }

    [Test]
    public async Task Readiness_ConfigurationObserversSeeCommittedSaveEnableDeleteAndCannotFailThem()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var states = new List<(int Count, bool Enabled)>();
        using var reading = f.UiChanges.Subscribe(
            new AutomationUiChange.SourceReadiness(new(f.HostId)),
            ObserverIdentity.Named("committed"),
            async (_, _) =>
            {
                await using var db = await f.Database.CreateDbContextAsync();
                var flows = await db.AutomationFlows.AsNoTracking().ToArrayAsync();
                states.Add((flows.Length, flows.FirstOrDefault()?.IsEnabled ?? false));
            }
        );
        using var failing = f.UiChanges.Subscribe(
            new AutomationUiChange.SourceReadiness(new(f.HostId)),
            ObserverIdentity.Named("failure"),
            (_, _) => throw new InvalidOperationException("UI observer failure")
        );
        var flow = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"Keyword","match":"tea"}""",
            "tea"
        );
        _ = (
            await f.Flows.SetEnabledAsync(new(f.HostId), flow, false, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        _ = (
            await f.Flows.DeleteAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDeleteOutcome.Deleted>();
        states.ShouldBe([(1, true), (1, false), (0, false)]);
    }

    [Test]
    public async Task Readiness_PageRefreshesMixedConfigurationSessionConnectionAndSelectiveGrants()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"Keyword","match":"tea"}""",
            "ordinary"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var channels = new ReadinessChannels();
        var x = ExpandedFixtureFor(f, channels);
        using var observation = new ReadinessConnectionObservation(x.Runtime);
        var tokens = new ReadinessTokens();
        tokens.Granted.ExceptWith(["bits:read", "channel:read:hype_train"]);
        using var ui = ReadinessUiFor(f, x, tokens, channels);
        var page = ui.Render<AutomationEventsPage>();
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(1)
        );
        page.Markup.ShouldContain("First observed chatter");
        page.Markup.ShouldNotContain("Connect this channel again");
        await x.Runtime.TickAsync(CancellationToken.None);
        page.WaitForAssertion(() => page.Markup.ShouldContain("confirmed offline"));
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "actual-stream",
            f.Clock.GetUtcNow()
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(2)
        );
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(0)
        );
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(1)
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(2)
        );
        channels.SetHealthy(false);
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(0)
        );
        channels.SetHealthy(true);
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(1)
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        page.WaitForAssertion(() =>
            page.FindAll("[data-configured-state=ready]").Count.ShouldBe(2)
        );
        var cheer = await SaveExpandedChatAsync(f, "cheer", """{"minimum-bits":1}""", "cheer");
        page.WaitForAssertion(() =>
            page.Markup.ShouldContain("Your enabled flows need bits:read.")
        );
        page.Markup.ShouldNotContain("Your enabled flows need bits:read, channel:read:hype_train");
        _ = await f.Flows.SetEnabledAsync(new(f.HostId), cheer, false, CancellationToken.None);
        page.WaitForAssertion(() => page.Markup.ShouldNotContain("Connect this channel again"));
        page.FindAll("[data-configured-state=disabled]").Count.ShouldBe(1);
        _ = await f.Flows.DeleteAsync(new(f.HostId), cheer, CancellationToken.None);
        page.WaitForAssertion(() => page.FindAll("[data-configured-state]").Count.ShouldBe(2));
        _ = await f.Features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        _ = await ui
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.HostedChannelsChanged, CancellationToken.None);
        page.WaitForAssertion(() => page.Markup.ShouldContain("Automations are off"));
    }

    [Test]
    public async Task Readiness_PageSwitchUnsubscribesOldHostFencesLateLoadsAndDisposal()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"Keyword","match":"tea"}""",
            "ordinary"
        );
        var otherHost = await f.SeedHostAsync("other", HostFeatureFlags.Automations);
        var otherSource = Node("chat-match", """{"kind":"Phrase","match":"other phrase"}""");
        _ = (
            await f.Flows.SaveAsync(Draft(otherHost, [otherSource], []), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var channels = new ReadinessChannels();
        var x = ExpandedFixtureFor(f, channels);
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer", "other"]);
        var tokens = new ReadinessTokens();
        using var ui = ReadinessUiFor(f, x, tokens, channels);
        var page = ui.Render<AutomationEventsPage>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Keyword: tea"));
        var calls = tokens.Calls;
        await f.UiChanges.SourceReadinessChangedAsync(new(otherHost));
        tokens.Calls.ShouldBe(calls);
        tokens.HoldHost = f.HostId;
        var oldLoad = f.UiChanges.SourceReadinessChangedAsync(new(f.HostId));
        await tokens.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var authorization =
            ui.Services.GetRequiredService<Bunit.TestDoubles.BunitAuthorizationContext>();
        SelectReadinessHost(authorization, otherHost, "other");
        _ = await ui
            .Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.HostedChannelsChanged, CancellationToken.None);
        page.WaitForAssertion(() => page.Markup.ShouldContain("Phrase: other phrase"));
        _ = tokens.Release.TrySetResult();
        await oldLoad;
        page.Markup.ShouldContain("Phrase: other phrase");
        page.Markup.ShouldNotContain("Keyword: tea");
        calls = tokens.Calls;
        await f.UiChanges.SourceReadinessChangedAsync(new(f.HostId));
        tokens.Calls.ShouldBe(calls);
        await ui.DisposeComponentsAsync();
        calls = tokens.Calls;
        await f.UiChanges.SourceReadinessChangedAsync(new(otherHost));
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        channels.Notify();
        tokens.Calls.ShouldBe(calls);
    }

    [Test]
    public async Task Readiness_DirectAdStartDoesNotInheritDerivedScheduleObservation()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"Started","offset-seconds":0}""",
            "started"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"BeforeScheduled","offset-seconds":10}""",
            "before"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"ExpectedFinish","offset-seconds":0}""",
            "finish"
        );
        var x = ExpandedFixtureFor(f);
        var tokens = new ReadinessTokens();
        _ = tokens.Granted.Add("channel:read:ads");
        var readiness = ReadinessFor(f, x.Runtime, tokens);
        var initial = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = Configured(initial, "Ad Started: 00:00:00")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        Configured(initial, "Ad BeforeScheduled: 00:00:10")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
            .Reason.ShouldContain("Waiting");
        x.Http.Code = System.Net.HttpStatusCode.ServiceUnavailable;
        await x.Runtime.TickAsync(CancellationToken.None);
        var failed = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = Configured(failed, "Ad Started: 00:00:00")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        Configured(failed, "Ad BeforeScheduled: 00:00:10")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
            .Reason.ShouldContain("unavailable");
        f.Chat.Messages.ShouldBeEmpty();
        x.Http.Code = System.Net.HttpStatusCode.OK;
        x.Http.Body = AdScheduleJson(f.Clock.GetUtcNow().AddMinutes(1), null, 0);
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await x.Runtime.TickAsync(CancellationToken.None);
        var schedule = await ReadinessSourcesAsync(readiness, f.HostId);
        _ = Configured(schedule, "Ad BeforeScheduled: 00:00:10")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        Configured(schedule, "Ad ExpectedFinish: 00:00:00")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
            .Reason.ShouldContain("ad break duration");
        await x.Runtime.AdBreakStartedAsync(
            new("ad", f.Clock.GetUtcNow(), "streamer-id", f.Clock.GetUtcNow(), 30, false),
            CancellationToken.None
        );
        _ = Configured(
            await ReadinessSourcesAsync(readiness, f.HostId),
            "Ad ExpectedFinish: 00:00:00"
        )
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        f.Chat.Messages.ShouldBe(["started"]);
    }

    private sealed class ReadinessConnectionObservation : IDisposable
    {
        private readonly ExpandedAutomationRuntime _runtime;

        internal ReadinessConnectionObservation(ExpandedAutomationRuntime runtime)
        {
            _runtime = runtime;
            _runtime.ObserveConnectionChanges(true);
        }

        public void Dispose() => _runtime.ObserveConnectionChanges(false);
    }

    private static TwitchEventSourceReadinessService ReadinessFor(
        RuntimeFixture f,
        ExpandedAutomationRuntime runtime,
        IHostBroadcasterTokenStatusProvider tokens
    ) => new(f.Database, f.Catalog, f.Runtime, tokens, runtime);

    private static async Task<TwitchEventSourceReadinessOutcome.Available> ReadinessSourcesAsync(
        TwitchEventSourceReadinessService readiness,
        int hostId
    ) =>
        (
            await readiness.LoadAsync(new(hostId), CancellationToken.None)
        ).ShouldBeOfType<TwitchEventSourceReadinessOutcome.Available>();

    private static TwitchConfiguredSourceReadiness Configured(
        TwitchEventSourceReadinessOutcome.Available available,
        string name
    ) =>
        available
            .Sources.SelectMany(static source => source.Configurations)
            .Single(source => source.ConfigurationName == name);

    private static EventSubObservedChatEvent ReadinessChat(
        RuntimeFixture f,
        string id,
        string text
    ) =>
        new(
            id,
            f.Clock.GetUtcNow(),
            "streamer-id",
            "streamer-id",
            "viewer-id",
            "viewer",
            "Viewer",
            text,
            []
        );

    private static BunitContext ReadinessUiFor(
        RuntimeFixture f,
        ExpandedFixture x,
        ReadinessTokens tokens,
        ReadinessChannels channels
    )
    {
        var ui = UiTestContextFactory.CreateWithAuthorization(f.Database, f.HostId);
        var context = ui.Context;
        _ = context.Services.AddSingleton(ui.Authorization);
        _ = context.Services.AddSingleton(f.UiChanges);
        _ = context.Services.AddSingleton<IAutomationUiChangePublisher>(f.UiChanges);
        _ = context.Services.AddSingleton(ReadinessFor(f, x.Runtime, tokens));
        _ = context.Services.AddSingleton<IBotRuntimeStatusAccessor>(x.Connection);
        _ = context.Services.AddSingleton<IEventSubChannelStatusAccessor>(channels);
        return context;
    }

    private static void SelectReadinessHost(
        Bunit.TestDoubles.BunitAuthorizationContext authorization,
        int hostId,
        string login
    )
    {
        var host = new BotHostChoice(hostId, login, login, AuthRole.Streamer);
        _ = authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, $"{login}-id"),
            new Claim(ClaimTypes.Name, login),
            new Claim(AuthClaims.Login, login),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Streamer)),
            new Claim(AuthClaims.CanCreateHost, "false"),
            new Claim(AuthClaims.IsBotAdmin, "false"),
            new Claim(AuthClaims.IsBotAccount, "false"),
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host))
        );
    }

    private sealed class ReadinessTokens : IHostBroadcasterTokenStatusProvider
    {
        internal HashSet<string> Granted { get; } =
            new(HostBroadcasterAuthorizationService.MilestoneScopes, StringComparer.Ordinal);
        internal bool Unknown { get; init; }
        internal int Calls { get; private set; }
        internal int? HoldHost { get; set; }
        internal TaskCompletionSource Held { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TokenStatus> GetTokenStatusAsync(
            int host,
            IEnumerable<string?> scopes,
            CancellationToken ct
        )
        {
            Calls++;
            if (HoldHost == host)
            {
                _ = Held.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            ImmutableArray<string> requested = [.. scopes.OfType<string>()];
            if (Unknown)
            {
                return new TokenStatus.Unknown(
                    new TokenStatusError.ValidationUnavailable(
                        TokenStatusTransportFailureReason.RequestFailed,
                        "TestFailure",
                        requested
                    )
                );
            }
            ImmutableArray<string> missing =
            [
                .. requested.Where(scope => !Granted.Contains(scope)),
            ];
            var identity = new TokenValidation($"{host}-id", "streamer", OAuthScopeSet.Empty);
            return missing.IsEmpty
                ? new TokenStatus.Ready("test-token", identity, requested, [.. Granted])
                : new TokenStatus.MissingScopes(
                    "test-token",
                    identity,
                    requested,
                    [.. Granted],
                    missing
                );
        }

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(string login) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new(login, "test-token")
                    )
                )
            );
    }

    private sealed class ReadinessChannels : IEventSubChannelStatusAccessor
    {
        public event Action? Changed;
        public EventSubChannelStatusSnapshot Current { get; private set; } =
            new()
            {
                Channels =
                [
                    .. new[] { "streamer", "other" }.Select(
                        channel => new EventSubChannelStatus.Healthy
                        {
                            Channel = channel,
                            Phase = EventSubChannelPhase.Reconciliation,
                            Attempt = 0,
                            ChangedAt = DateTimeOffset.UtcNow,
                            Trigger = EventSubChannelRecoveryTrigger.Startup,
                        }
                    ),
                ],
            };

        internal void SetHealthy(bool healthy)
        {
            Current = healthy
                ? new()
                {
                    Channels =
                    [
                        new EventSubChannelStatus.Healthy
                        {
                            Channel = "streamer",
                            Phase = EventSubChannelPhase.Reconciliation,
                            Attempt = 0,
                            ChangedAt = DateTimeOffset.UtcNow,
                            Trigger = EventSubChannelRecoveryTrigger.Explicit,
                        },
                    ],
                }
                : new() { Channels = [] };
            Notify();
        }

        internal void Notify() => Changed?.Invoke();
    }
}

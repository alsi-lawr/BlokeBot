using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TwitchEventAutomationTests
{
    private static readonly DateTimeOffset _start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task StreamOnline_DuplicateMessageIdsCreateOneRunUntilTheTenMinuteBoundary()
    {
        await using var fixture = await EventFixture.CreateAsync();
        var source = Node("stream-online", "{}");
        var action = Node("send-chat", """{"message":"Live!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("delivery-1"),
            CancellationToken.None
        );
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("delivery-1"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.Count.ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("delivery-1"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("delivery-1"),
            CancellationToken.None
        );

        // At exactly ten minutes the receipt has no deduplication authority; the redelivery is a
        // new occurrence.
        (await fixture.RunCountAsync()).ShouldBe(2);
        fixture.Chat.Messages.Count.ShouldBe(2);
    }

    [Test]
    public async Task Cheer_StartsFlowsOnlyAtOrAboveTheConfiguredMinimumBits()
    {
        await using var fixture = await EventFixture.CreateAsync();
        var source = Node("cheer", """{"minimum-bits":100}""");
        var action = Node("send-chat", """{"message":"Cheers!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.CheerReceivedAsync(
            fixture.Cheer("cheer-1", bits: 99),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(0);

        await fixture.Runtime.CheerReceivedAsync(
            fixture.Cheer("cheer-2", bits: 100),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task IncomingRaid_StartsFlowsOnlyAtOrAboveTheConfiguredMinimumViewers()
    {
        await using var fixture = await EventFixture.CreateAsync();
        var source = Node("incoming-raid", """{"minimum-viewers":25}""");
        var action = Node("send-chat", """{"message":"Raid!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-1", viewers: 24),
            CancellationToken.None
        );
        await fixture.Runtime.IncomingRaidReceivedAsync(
            fixture.IncomingRaid("raid-2", viewers: 25),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task ChatNotification_FiltersByNoticeTypeWithAnyMatchingEveryTypedNotice()
    {
        await using var fixture = await EventFixture.CreateAsync();
        var announcementSource = Node("chat-notification", """{"notice-type":"announcement"}""");
        var announcementAction = Node("send-chat", """{"message":"Announcement"}""");
        var anySource = Node("chat-notification", """{"notice-type":"any"}""");
        var anyAction = Node("send-chat", """{"message":"Any"}""");
        _ = await fixture.SaveAsync(
            [announcementSource, announcementAction],
            [Edge(announcementSource, "flow", announcementAction)]
        );
        _ = await fixture.SaveAsync([anySource, anyAction], [Edge(anySource, "flow", anyAction)]);

        await fixture.Runtime.ChatNotificationReceivedAsync(
            fixture.ChatNotification("notice-1", "announcement"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(2);

        await fixture.Runtime.ChatNotificationReceivedAsync(
            fixture.ChatNotification("notice-2", "resub"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(3);
        fixture.Chat.Messages.Count(static message => message == "Any").ShouldBe(2);
        fixture.Chat.Messages.Count(static message => message == "Announcement").ShouldBe(1);
    }

    [Test]
    public async Task DisabledAutomations_BlockDeliveriesBeforeMutationAndNeverReplayThem()
    {
        await using var fixture = await EventFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.CustomCommands
        );
        await fixture.EnableAutomationsAsync(true);
        var source = Node("stream-online", "{}");
        var action = Node("send-chat", """{"message":"Live!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await fixture.EnableAutomationsAsync(false);

        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("suppressed-1"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);
        fixture.Chat.Messages.ShouldBeEmpty();

        await fixture.EnableAutomationsAsync(true);

        // Re-enabling replays nothing; only a fresh delivery starts a run.
        (await fixture.RunCountAsync()).ShouldBe(0);
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("fresh-1"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task ReceiptClaimWinsBeforeDisable_DedupesWithoutStartingARejectedRun()
    {
        var interleaving = new ReceiptTransactionInterleaving(ReceiptTransactionPause.AfterCommit);
        await using var fixture = await EventFixture.CreateAsync(
            databaseInterceptors: [interleaving]
        );
        var source = Node("stream-online", "{}");
        var action = Node("send-chat", """{"message":"Live!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await fixture.FlowRuntime.InitializeAsync(CancellationToken.None);
        interleaving.Arm();

        var delivery = fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("receipt-first"),
            CancellationToken.None
        );
        await interleaving.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.EnableAutomationsAsync(false);
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
        (await fixture.RunCountAsync()).ShouldBe(0);
        interleaving.Release();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));

        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
        await fixture.EnableAutomationsAsync(true);
        (await fixture.RunCountAsync()).ShouldBe(0);
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("receipt-first"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task DisableWinsBeforeReceipt_WritesNothingUntilAnExplicitRedelivery()
    {
        var interleaving = new ReceiptTransactionInterleaving(ReceiptTransactionPause.BeforeStart);
        await using var fixture = await EventFixture.CreateAsync(
            databaseInterceptors: [interleaving]
        );
        var source = Node("stream-online", "{}");
        var action = Node("send-chat", """{"message":"Live!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await fixture.FlowRuntime.InitializeAsync(CancellationToken.None);
        interleaving.Arm();

        var delivery = fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("disable-first"),
            CancellationToken.None
        );
        await interleaving.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.EnableAutomationsAsync(false);
        interleaving.Release();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));

        (await fixture.RunCountAsync()).ShouldBe(0);
        (await fixture.ReceiptCountAsync()).ShouldBe(0);
        fixture.Chat.Messages.ShouldBeEmpty();
        await fixture.EnableAutomationsAsync(true);
        (await fixture.RunCountAsync()).ShouldBe(0);
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("disable-first"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(1);
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["Live!"]);
    }

    [Test]
    public async Task AdmittedReceipt_DrainsAcrossDisableAndNeverReplaysAfterEnable()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(async cancellationToken =>
        {
            _ = entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        });
        await using var fixture = await EventFixture.CreateAsync(chat: chat);
        var source = Node("stream-online", "{}");
        var action = Node("send-chat", """{"message":"Live!"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var delivery = fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("admitted-1"),
            CancellationToken.None
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
        (await fixture.RunCountAsync()).ShouldBe(1);
        await fixture.EnableAutomationsAsync(false);
        _ = release.TrySetResult();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                AutomationFlowRunStatus.Completed
            );
        }
        fixture.Chat.Messages.ShouldBe(["Live!"]);
        await fixture.EnableAutomationsAsync(true);
        (await fixture.RunCountAsync()).ShouldBe(1);
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("admitted-1"),
            CancellationToken.None
        );

        (await fixture.RunCountAsync()).ShouldBe(1);
        (await fixture.ReceiptCountAsync()).ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["Live!"]);
    }

    [Test]
    public async Task Deliveries_ResolveExactlyOneHostAndReceiptsAreHostIsolated()
    {
        await using var fixture = await EventFixture.CreateAsync();
        var otherHostId = await fixture.SeedHostAsync(
            "other",
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );
        var source = Node("follow", "{}");
        var action = Node("send-chat", """{"message":"Followed"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var otherSource = Node("follow", "{}");
        var otherAction = Node("send-chat", """{"message":"Other followed"}""");
        _ = await fixture.SaveAsync(
            [otherSource, otherAction],
            [Edge(otherSource, "flow", otherAction)],
            otherHostId
        );

        await fixture.Runtime.FollowReceivedAsync(
            fixture.Follow("follow-1", hostId: otherHostId),
            CancellationToken.None
        );

        (await fixture.RunCountAsync(otherHostId)).ShouldBe(1);
        (await fixture.RunCountAsync()).ShouldBe(0);

        // The same Twitch message id delivered for a different host is a distinct receipt.
        await fixture.Runtime.FollowReceivedAsync(
            fixture.Follow("follow-1"),
            CancellationToken.None
        );
        (await fixture.RunCountAsync()).ShouldBe(1);
        (await fixture.ReceiptCountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task ReceiptCleanup_RemovesOnlyExpiredRowsAndRunsAtLeastEveryFiveMinutes()
    {
        TwitchEventAutomationRuntime.ReceiptAuthorityWindow.ShouldBe(TimeSpan.FromMinutes(10));
        TwitchEventAutomationRuntime.ReceiptCleanupInterval.ShouldBeLessThanOrEqualTo(
            TimeSpan.FromMinutes(5)
        );

        await using var fixture = await EventFixture.CreateAsync();
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("old-1"),
            CancellationToken.None
        );
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        await fixture.Runtime.StreamOnlineAsync(
            fixture.StreamOnline("fresh-1"),
            CancellationToken.None
        );

        await fixture.Runtime.CleanupExpiredReceiptsAsync(CancellationToken.None);

        await using var db = await fixture.Database.CreateDbContextAsync();
        var receipts = await db.AutomationEventReceipts.AsNoTracking().ToArrayAsync();
        _ = receipts.ShouldHaveSingleItem();
        receipts[0].ProviderMessageId.ShouldBe("fresh-1");
    }

    [Test]
    public async Task Requirements_FollowEnabledFlowsAndTheAutomationsSwitch()
    {
        await using var fixture = await EventFixture.CreateAsync();

        (
            await fixture.Runtime.RequiresAsync(
                fixture.HostLogin,
                AutomationEventSubRequirement.Cheers,
                CancellationToken.None
            )
        ).ShouldBeFalse();

        var source = Node("cheer", """{"minimum-bits":1}""");
        var action = Node("send-chat", """{"message":"Cheers"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        (
            await fixture.Runtime.RequiresAsync(
                fixture.HostLogin,
                AutomationEventSubRequirement.Cheers,
                CancellationToken.None
            )
        ).ShouldBeTrue();
        (
            await fixture.Runtime.RequiresAsync(
                fixture.HostLogin,
                AutomationEventSubRequirement.Stream,
                CancellationToken.None
            )
        ).ShouldBeFalse();
        (
            await fixture.Runtime.RequiresAsync(
                "unknown-channel",
                AutomationEventSubRequirement.Cheers,
                CancellationToken.None
            )
        ).ShouldBeFalse();

        await fixture.EnableAutomationsAsync(false);
        (
            await fixture.Runtime.RequiresAsync(
                fixture.HostLogin,
                AutomationEventSubRequirement.Cheers,
                CancellationToken.None
            )
        ).ShouldBeFalse();
    }

    [Test]
    public async Task Readiness_SurfacesExactMissingScopesPerSourceAndTheDisabledSwitch()
    {
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain(
            "channel:read:subscriptions"
        );
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain("bits:read");
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldContain(
            "channel:read:hype_train"
        );

        await using var fixture = await EventFixture.CreateAsync();
        var cheerSource = Node("cheer", """{"minimum-bits":1}""");
        var cheerAction = Node("send-chat", """{"message":"Cheers"}""");
        _ = await fixture.SaveAsync(
            [cheerSource, cheerAction],
            [Edge(cheerSource, "flow", cheerAction)]
        );
        var readiness = new TwitchEventSourceReadinessService(
            fixture.Database,
            fixture.Catalog,
            fixture.FlowRuntime,
            new FakeBroadcasterTokens(
                new TokenStatus.MissingScopes(
                    "token",
                    new("host-user-id", fixture.HostLogin, OAuthScopeSet.Empty),
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                    [],
                    ["bits:read", "channel:read:hype_train"]
                )
            )
        );

        var outcome = await readiness.LoadAsync(new(fixture.HostId), CancellationToken.None);

        var available = outcome.ShouldBeOfType<TwitchEventSourceReadinessOutcome.Available>();
        available.BroadcasterConnected.ShouldBeTrue();
        Source(available, "cheer")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.MissingScopes>()
            .Scopes.ShouldBe(["bits:read"]);
        Source(available, "hype-train-begin")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.MissingScopes>()
            .Scopes.ShouldBe(["channel:read:hype_train"]);
        _ = Source(available, "subscription")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        _ = Source(available, "follow")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        _ = Source(available, "chat-notification")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        Source(available, "cheer").UsedByEnabledFlow.ShouldBeTrue();
        Source(available, "stream-online").UsedByEnabledFlow.ShouldBeFalse();

        var disconnected = new TwitchEventSourceReadinessService(
            fixture.Database,
            fixture.Catalog,
            fixture.FlowRuntime,
            new FakeBroadcasterTokens(
                new TokenStatus.Unavailable(
                    AccessTokenUnavailableReason.MissingRefreshToken,
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            )
        );
        var disconnectedOutcome = (TwitchEventSourceReadinessOutcome.Available)
            await disconnected.LoadAsync(new(fixture.HostId), CancellationToken.None);
        disconnectedOutcome.BroadcasterConnected.ShouldBeFalse();
        _ = Source(disconnectedOutcome, "cheer")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.BroadcasterNotConnected>();
        _ = Source(disconnectedOutcome, "stream-online")
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();

        await fixture.EnableAutomationsAsync(false);
        _ = (
            await readiness.LoadAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<TwitchEventSourceReadinessOutcome.FeatureDisabled>();
    }

    [Test]
    public async Task Context_MapsBoundedDocumentedFieldsWithoutTokensOrHeaders()
    {
        await using var fixture = await EventFixture.CreateAsync();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var host = await db.Hosts.AsNoTracking().SingleAsync(h => h.Id == fixture.HostId);

        var anonymousCheer = TwitchEventAutomationContext.Cheer(
            host,
            fixture.Cheer("cheer-1", bits: 250, anonymous: true, message: new string('x', 800)),
            _start
        );

        anonymousCheer.Actor.ShouldBeNull();
        anonymousCheer.HostId.Value.ShouldBe(fixture.HostId);
        anonymousCheer.Event.SourceDefinitionId.ShouldBe(AutomationDefinitionIds.CheerSource);
        var safeValues = anonymousCheer.Variables.SafeForExternalUse();
        safeValues[new("bits")].ShouldBeOfType<AutomationValue.Number>().Value.ShouldBe(250);
        safeValues.Keys.ShouldNotContain(new AutomationVariableName("cheer_message"));

        var follow = TwitchEventAutomationContext.Follow(host, fixture.Follow("follow-1"), _start);
        var actor = follow.Actor.ShouldNotBeNull();
        actor.Login.ShouldBe("viewer");
        follow.Timestamps.ReceivedAtUtc.ShouldBe(_start);
    }

    [Test]
    public async Task FlowChangesAndAutomationSwitchChanges_TriggerEventSubReconciliation()
    {
        await using var fixture = await EventFixture.CreateAsync();
        var trigger = new RecordingReconciliationTrigger();
        var flows = new AutomationFlowService(
            fixture.Database,
            fixture.Catalog,
            fixture.Expressions,
            new NoOverlayCues(),
            fixture.Clock,
            trigger
        );
        var source = Node("stream-online", "{}");
        var action = Node("send-chat", """{"message":"Live!"}""");

        var saved = (
            (AutomationFlowSaveOutcome.Saved)
                await flows.SaveAsync(
                    Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
                    CancellationToken.None
                )
        ).FlowId;
        trigger.Reconciliations.ShouldBe(1);

        _ = await flows.SetEnabledAsync(
            new(fixture.HostId),
            saved,
            enabled: false,
            CancellationToken.None
        );
        trigger.Reconciliations.ShouldBe(2);

        var observer = new AutomationEventSubReconciliationObserver(trigger);
        _ = await observer.ApplyAsync(
            new(fixture.HostId, HostFeatureFlags.Automations, HostFeatureActivationState.Disabled),
            CancellationToken.None
        );
        trigger.Reconciliations.ShouldBe(3);
        _ = await observer.ApplyAsync(
            new(fixture.HostId, HostFeatureFlags.Points, HostFeatureActivationState.Disabled),
            CancellationToken.None
        );
        trigger.Reconciliations.ShouldBe(3);
    }

    private static TwitchEventSourceReadiness Source(
        TwitchEventSourceReadinessOutcome.Available available,
        string definitionId
    ) => available.Sources.Single(source => source.DefinitionId.Value == definitionId);

    private static AutomationFlowDraft Draft(
        int hostId,
        ImmutableArray<AutomationFlowDraftNode> nodes,
        ImmutableArray<AutomationFlowDraftEdge> edges
    ) => new(null, new(hostId), "Flow", 1, true, nodes, edges);

    private static AutomationFlowDraftNode Node(string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(
            new(Guid.NewGuid()),
            new(type, 1, document.RootElement.Clone()),
            AutomationExpressionLanguage.CurrentVersion,
            AutomationNodeFailurePolicy.Stop,
            type == "send-chat"
                ? ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationInputBinding
                >.Empty.Add(new("message"), new(AutomationInputBindingMode.Fixed, Expression: null))
                : ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
        );
    }

    private static AutomationFlowDraftEdge Edge(
        AutomationFlowDraftNode source,
        string sourcePort,
        AutomationFlowDraftNode target
    ) =>
        new(
            Guid.NewGuid(),
            AutomationEdgeKind.Flow,
            source.Id,
            new(sourcePort),
            target.Id,
            new("flow")
        );

    private sealed class RecordingReconciliationTrigger : IEventSubChannelReconciliationTrigger
    {
        internal int Reconciliations { get; private set; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            Reconciliations++;
            return Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class FakeBroadcasterTokens(TokenStatus status)
        : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) => Task.FromResult(status);

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(static _ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
    }

    private sealed class EventFixture : IAsyncDisposable
    {
        private EventFixture(
            SqliteBlokeBotDbFactory database,
            MutableTimeProvider clock,
            RecordingChatSender chat,
            HostFeatureService features,
            AutomationCatalogService catalog,
            AutomationExpressionService expressions,
            AutomationRuntimeService flowRuntime,
            AutomationFlowService flows,
            TwitchEventAutomationRuntime runtime
        )
        {
            Database = database;
            Clock = clock;
            Chat = chat;
            Features = features;
            Catalog = catalog;
            Expressions = expressions;
            FlowRuntime = flowRuntime;
            Flows = flows;
            Runtime = runtime;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal MutableTimeProvider Clock { get; }
        internal RecordingChatSender Chat { get; }
        internal HostFeatureService Features { get; }
        internal AutomationCatalogService Catalog { get; }
        internal AutomationExpressionService Expressions { get; }
        internal AutomationRuntimeService FlowRuntime { get; }
        internal AutomationFlowService Flows { get; }
        internal TwitchEventAutomationRuntime Runtime { get; }
        internal int HostId { get; private set; }

        internal string HostLogin => "streamer";

        internal string HostUserId(int? hostId = null) =>
            hostId is { } value && value != HostId ? "other-id" : "streamer-id";

        internal static async Task<EventFixture> CreateAsync(
            HostFeatureFlags hostFeatures =
                HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands,
            IInterceptor[]? databaseInterceptors = null,
            RecordingChatSender? chat = null
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync(databaseInterceptors ?? []);
            var clock = new MutableTimeProvider(_start);
            chat ??= new RecordingChatSender();
            var features = TestHostFeatureServices.Create(
                database,
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                [],
                clock
            );
            var catalog = new AutomationCatalogService(
                new([new CoreAutomationCatalogModule(), new TwitchEventAutomationCatalogModule()]),
                features
            );
            var expressions = new AutomationExpressionService();
            var overlays = new NoOverlayCues();
            var actions = new AutomationActionExecutor(features, chat, overlays, expressions);
            var flows = new AutomationFlowService(database, catalog, expressions, overlays, clock);
            var flowRuntime = new AutomationRuntimeService(
                database,
                catalog,
                flows,
                actions,
                clock
            );
            var runtime = new TwitchEventAutomationRuntime(
                database,
                flowRuntime,
                clock,
                NullLogger<TwitchEventAutomationRuntime>.Instance
            );
            var fixture = new EventFixture(
                database,
                clock,
                chat,
                features,
                catalog,
                expressions,
                flowRuntime,
                flows,
                runtime
            );
            fixture.HostId = await fixture.SeedHostAsync("streamer", hostFeatures);
            return fixture;
        }

        internal async Task<int> SeedHostAsync(string login, HostFeatureFlags enabledFeatures)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = login == "streamer" ? "streamer-id" : "other-id",
                Login = login,
                DisplayName = login,
                EnabledFeatures = enabledFeatures,
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            return host.Id;
        }

        internal async Task EnableAutomationsAsync(bool enabled) =>
            _ = enabled
                ? await Features.EnableAsync(
                    HostId,
                    HostFeatureFlags.Automations,
                    CancellationToken.None
                )
                : await Features.DisableAsync(
                    HostId,
                    HostFeatureFlags.Automations,
                    CancellationToken.None
                );

        internal async Task<AutomationFlowId> SaveAsync(
            ImmutableArray<AutomationFlowDraftNode> nodes,
            ImmutableArray<AutomationFlowDraftEdge> edges,
            int? hostId = null
        ) =>
            (await Flows.SaveAsync(Draft(hostId ?? HostId, nodes, edges), CancellationToken.None))
                .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
                .FlowId;

        internal async Task<int> RunCountAsync(int? hostId = null)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = hostId ?? HostId;
            return await db.AutomationFlowRuns.CountAsync(run => run.HostId == host);
        }

        internal async Task<int> ReceiptCountAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            return await db.AutomationEventReceipts.CountAsync();
        }

        internal EventSubStreamOnlineEvent StreamOnline(string messageId) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                HostUserId(),
                HostLogin,
                "Streamer",
                "stream-1",
                "live",
                Clock.GetUtcNow()
            );

        internal EventSubFollowEvent Follow(string messageId, int? hostId = null) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                "viewer-id",
                "viewer",
                "Viewer",
                HostUserId(hostId),
                hostId is { } value && value != HostId ? "other" : HostLogin,
                "Streamer",
                Clock.GetUtcNow()
            );

        internal EventSubCheerEvent Cheer(
            string messageId,
            int bits,
            bool anonymous = false,
            string message = "cheer"
        ) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                anonymous ? null : "viewer-id",
                anonymous ? null : "viewer",
                anonymous ? null : "Viewer",
                HostUserId(),
                HostLogin,
                "Streamer",
                bits,
                message,
                anonymous
            );

        internal EventSubIncomingRaidEvent IncomingRaid(string messageId, int viewers) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                "raider-id",
                "raider",
                "Raider",
                HostUserId(),
                HostLogin,
                "Streamer",
                viewers
            );

        internal EventSubChatNotificationEvent ChatNotification(
            string messageId,
            string noticeType
        ) =>
            new(
                messageId,
                Clock.GetUtcNow(),
                HostUserId(),
                HostLogin,
                "Streamer",
                "viewer-id",
                "viewer",
                "Viewer",
                false,
                noticeType,
                "system message",
                "notice text"
            );

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private sealed class RecordingChatSender(Func<CancellationToken, ValueTask>? beforeSend = null)
        : IPublicChatMessageSender
    {
        internal ConcurrentQueue<string> Messages { get; } = [];

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Enqueue(message);
            if (beforeSend is not null)
            {
                await beforeSend(cancellationToken);
            }

            return new PublicChatSendOutcome.Accepted();
        }
    }

    private sealed class ReceiptTransactionInterleaving(ReceiptTransactionPause pause)
        : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _intercepted;

        internal Task Entered => _entered.Task;

        internal void Arm() => Volatile.Write(ref _armed, 1);

        internal void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            if (TryEnter(ReceiptTransactionPause.BeforeStart))
            {
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (TryEnter(ReceiptTransactionPause.AfterCommit))
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
        }

        private bool TryEnter(ReceiptTransactionPause expected)
        {
            if (
                pause != expected
                || Volatile.Read(ref _armed) == 0
                || Interlocked.CompareExchange(ref _intercepted, 1, 0) != 0
            )
            {
                return false;
            }

            _ = _entered.TrySetResult();
            return true;
        }
    }

    private enum ReceiptTransactionPause
    {
        BeforeStart,
        AfterCommit,
    }

    private sealed class NoOverlayCues : IOverlayCueAdmissionService
    {
        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<OverlayCueReferenceOutcome>(
                new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue)
            );

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan duration) => now += duration;
    }
}

using System.Globalization;
using System.Text.Json;
using BlokeBot.Commands;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchEventAutomationEventSubTests : EventSubChannelRecoveryTestBase
{
    private static readonly DateTimeOffset _timestamp = DateTimeOffset.Parse(
        "2026-08-07T12:00:00.5000000Z",
        CultureInfo.InvariantCulture
    );

    [Test]
    public void StreamOnlineEnvelope_ParsingTypedNotification_MapsBoundedIdentity()
    {
        var notification = Parse(
            """
            {
              "subscription": { "type": "stream.online", "version": "1" },
              "event": {
                "id": "stream-1",
                "broadcaster_user_id": "host-id",
                "broadcaster_user_login": "host_login",
                "broadcaster_user_name": "Host Display",
                "type": "live",
                "started_at": "2026-08-07T11:59:00Z"
              }
            }
            """
        );

        var streamOnline = notification.ShouldBeOfType<EventSubNotification.StreamOnline>().Event;
        streamOnline.MessageId.ShouldBe("automation-message-1");
        streamOnline.MessageTimestamp.ShouldBe(_timestamp);
        streamOnline.BroadcasterUserId.ShouldBe("host-id");
        streamOnline.BroadcasterUserLogin.ShouldBe("host_login");
        streamOnline.StreamId.ShouldBe("stream-1");
        streamOnline.StreamType.ShouldBe("live");
        streamOnline.StartedAt.ShouldBe(
            DateTimeOffset.Parse("2026-08-07T11:59:00Z", CultureInfo.InvariantCulture)
        );
    }

    [Test]
    public void FollowEnvelope_ParsingTypedNotification_MapsFollowerAndFollowedAt()
    {
        var notification = Parse(
            """
            {
              "subscription": { "type": "channel.follow", "version": "2" },
              "event": {
                "user_id": "viewer-id", "user_login": "viewer", "user_name": "Viewer",
                "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                "broadcaster_user_name": "Host", "followed_at": "2026-08-07T11:58:00Z"
              }
            }
            """
        );

        var follow = notification.ShouldBeOfType<EventSubNotification.Follow>().Event;
        follow.UserId.ShouldBe("viewer-id");
        follow.UserLogin.ShouldBe("viewer");
        follow.BroadcasterUserId.ShouldBe("host-id");
        follow.FollowedAt.ShouldBe(
            DateTimeOffset.Parse("2026-08-07T11:58:00Z", CultureInfo.InvariantCulture)
        );
    }

    [Test]
    public void SubscriptionEnvelopes_ParsingTypedNotifications_MapTierGiftAndAnonymity()
    {
        var subscription = Parse(
                """
                {
                  "subscription": { "type": "channel.subscribe", "version": "1" },
                  "event": {
                    "user_id": "viewer-id", "user_login": "viewer", "user_name": "Viewer",
                    "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                    "broadcaster_user_name": "Host", "tier": "2000", "is_gift": true
                  }
                }
                """
            )
            .ShouldBeOfType<EventSubNotification.Subscription>()
            .Event;
        subscription.Tier.ShouldBe("2000");
        subscription.IsGift.ShouldBeTrue();

        var anonymousGift = Parse(
                """
                {
                  "subscription": { "type": "channel.subscription.gift", "version": "1" },
                  "event": {
                    "user_id": null, "user_login": null, "user_name": null,
                    "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                    "broadcaster_user_name": "Host", "total": 5, "tier": "1000",
                    "cumulative_total": null, "is_anonymous": true
                  }
                }
                """
            )
            .ShouldBeOfType<EventSubNotification.SubscriptionGift>()
            .Event;
        anonymousGift.Total.ShouldBe(5);
        anonymousGift.IsAnonymous.ShouldBeTrue();
        anonymousGift.UserId.ShouldBeNull();
        anonymousGift.UserLogin.ShouldBeNull();
    }

    [Test]
    public void CheerEnvelope_ParsingTypedNotification_MapsBitsAndAnonymity()
    {
        var cheer = Parse(
                """
                {
                  "subscription": { "type": "channel.cheer", "version": "1" },
                  "event": {
                    "is_anonymous": true, "user_id": null, "user_login": null, "user_name": null,
                    "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                    "broadcaster_user_name": "Host", "message": "cheer100 great stream", "bits": 100
                  }
                }
                """
            )
            .ShouldBeOfType<EventSubNotification.Cheer>()
            .Event;
        cheer.Bits.ShouldBe(100);
        cheer.IsAnonymous.ShouldBeTrue();
        cheer.UserId.ShouldBeNull();
        cheer.Message.ShouldBe("cheer100 great stream");
    }

    [Test]
    [Arguments("channel.hype_train.begin", EventSubHypeTrainStage.Begin)]
    [Arguments("channel.hype_train.progress", EventSubHypeTrainStage.Progress)]
    [Arguments("channel.hype_train.end", EventSubHypeTrainStage.End)]
    public void HypeTrainEnvelopes_ParsingTypedNotifications_MapStageLevelAndTotal(
        string subscriptionType,
        EventSubHypeTrainStage expectedStage
    )
    {
        var hypeTrain = Parse(
                $$"""
                {
                  "subscription": { "type": "{{subscriptionType}}", "version": "2" },
                  "event": {
                    "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                    "broadcaster_user_name": "Host", "level": 3, "total": 1200
                  }
                }
                """
            )
            .ShouldBeOfType<EventSubNotification.HypeTrain>()
            .Event;
        hypeTrain.Stage.ShouldBe(expectedStage);
        hypeTrain.Level.ShouldBe(3);
        hypeTrain.Total.ShouldBe(1200);
    }

    [Test]
    public void ChatNotificationEnvelope_ParsingTypedNotification_MapsNoticeAndChatter()
    {
        var notification = Parse(
                """
                {
                  "subscription": { "type": "channel.chat.notification", "version": "1" },
                  "event": {
                    "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                    "broadcaster_user_name": "Host",
                    "chatter_user_id": "viewer-id", "chatter_user_login": "viewer",
                    "chatter_user_name": "Viewer", "chatter_is_anonymous": false,
                    "notice_type": "announcement",
                    "system_message": "Viewer made an announcement",
                    "message": { "text": "big news" }
                  }
                }
                """
            )
            .ShouldBeOfType<EventSubNotification.ChatNotification>()
            .Event;
        notification.NoticeType.ShouldBe("announcement");
        notification.ChatterUserLogin.ShouldBe("viewer");
        notification.ChatterIsAnonymous.ShouldBeFalse();
        notification.SystemMessage.ShouldBe("Viewer made an announcement");
        notification.MessageText.ShouldBe("big news");
    }

    [Test]
    public void OrdinaryChatMessage_ParsesAsChatDeliveryAndNeverAsAnAutomationNotification()
    {
        var notification = Parse(
            """
            {
              "subscription": { "type": "channel.chat.message", "version": "1" },
              "event": {
                "broadcaster_user_id": "host-id", "broadcaster_user_login": "host_login",
                "chatter_user_id": "viewer-id", "chatter_user_login": "viewer",
                "message_id": "chat-message-1", "message": { "text": "hello" }
              }
            }
            """
        );

        // Ordinary chat messages stay on the chat-command path; only typed
        // channel.chat.notification deliveries reach automation observers.
        _ = notification.ShouldBeOfType<EventSubNotification.Chat>();
    }

    [Test]
    public async Task AutomationNotifications_DispatchToAutomationObservers()
    {
        var observer = new RecordingAutomationObserver();
        var handler = CreateHandler(observer);
        var streamOnline = Envelope(
            """
            {
              "subscription": { "type": "stream.online", "version": "1" },
              "event": {
                "id": "stream-1", "broadcaster_user_id": "host-id",
                "broadcaster_user_login": "host_login", "broadcaster_user_name": "Host",
                "type": "live", "started_at": "2026-08-07T11:59:00Z"
              }
            }
            """
        );

        await handler.DispatchNotificationAsync(streamOnline, "{}", CancellationToken.None);

        observer.Deliveries.ShouldBe(["stream-online"]);
    }

    [Test]
    public async Task IncomingRaid_ReachesAutomationObserversWithoutTheShoutoutsGate()
    {
        var observer = new RecordingAutomationObserver();
        var raidObserver = new RecordingRaidObserver();
        var handler = new EventSubDeliveryHandler(
            null!,
            null!,
            new DisabledNativeTwitchFeatures(),
            [],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            incomingRaidObservers: [raidObserver],
            automationObservers: [observer]
        );

        await handler.DispatchNotificationAsync(
            EventSubNotificationTests.IncomingRaidEnvelope(),
            "{}",
            CancellationToken.None
        );

        observer.Deliveries.ShouldBe(["incoming-raid"]);
        raidObserver.Deliveries.ShouldBe(0);
    }

    [Test]
    public async Task AutomationSubscriptionLifecycle_FollowsRequirementsAndAuthorities()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchFeatureEnabled(
            "channel",
            EventSubOperationSubscriptionKind.AutomationStream,
            true
        );
        operations.SetNativeTwitchFeatureEnabled(
            "channel",
            EventSubOperationSubscriptionKind.AutomationCheers,
            true
        );
        operations.EnqueueBroadcasterAccountResult("channel", "channel");
        await using var harness = CreateHarness(operations, attemptLimit: 1);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        operations
            .OperationKinds("channel")
            .ShouldBe([
                null,
                EventSubOperationSubscriptionKind.AutomationStream,
                EventSubOperationSubscriptionKind.AutomationCheers,
            ]);
        operations.CreateCount("channel").ShouldBe(3);
        var authorizations = operations.Authorizations("channel");
        _ = authorizations[1]
            .ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBotOperations>();
        authorizations[2]
            .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
            .Operation.ShouldBe(EventSubBroadcasterOperationKind.AutomationCheers);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);

        operations.SetNativeTwitchFeatureEnabled(
            "channel",
            EventSubOperationSubscriptionKind.AutomationStream,
            false
        );
        operations.SetNativeTwitchFeatureEnabled(
            "channel",
            EventSubOperationSubscriptionKind.AutomationCheers,
            false
        );
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CreateCount("channel").ShouldBe(3);
    }

    private static EventSubDeliveryHandler CreateHandler(RecordingAutomationObserver observer) =>
        new(
            null!,
            null!,
            new DisabledNativeTwitchFeatures(),
            [],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            automationObservers: [observer]
        );

    private static EventSubNotification Parse(string json) =>
        EventSubNotification.Parse(
            Envelope(json),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

    private static EventSubEnvelope Envelope(string json)
    {
        var envelope = JsonSerializer.Deserialize<EventSubEnvelope>(json)!;
        var subscription = envelope.Subscription!.Value;
        envelope.Metadata = new EventSubMetadata
        {
            MessageId = "automation-message-1",
            MessageType = "notification",
            SubscriptionType = subscription.GetProperty("type").GetString()!,
            SubscriptionVersion = subscription.GetProperty("version").GetString()!,
            MessageTimestamp = _timestamp,
        };
        return envelope;
    }

    private sealed class DisabledNativeTwitchFeatures : INativeTwitchFeatureStateProvider
    {
        public ValueTask<bool> IsEnabledAsync(
            string channel,
            NativeTwitchFeature feature,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(false);
    }

    private sealed class RecordingRaidObserver : IIncomingRaidEventObserver
    {
        internal int Deliveries { get; private set; }

        public Task IncomingRaidReceivedAsync(
            EventSubIncomingRaidEvent incomingRaid,
            CancellationToken cancellationToken
        )
        {
            Deliveries++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAutomationObserver : ITwitchEventAutomationObserver
    {
        internal List<string> Deliveries { get; } = [];

        public Task StreamOnlineAsync(
            EventSubStreamOnlineEvent streamOnline,
            CancellationToken cancellation
        ) => Record("stream-online");

        public Task StreamOfflineAsync(
            EventSubStreamOfflineEvent streamOffline,
            CancellationToken cancellation
        ) => Record("stream-offline");

        public Task FollowReceivedAsync(
            EventSubFollowEvent follow,
            CancellationToken cancellation
        ) => Record("follow");

        public Task SubscriptionReceivedAsync(
            EventSubSubscriptionEvent subscription,
            CancellationToken cancellation
        ) => Record("subscription");

        public Task SubscriptionGiftReceivedAsync(
            EventSubSubscriptionGiftEvent gift,
            CancellationToken cancellation
        ) => Record("subscription-gift");

        public Task CheerReceivedAsync(EventSubCheerEvent cheer, CancellationToken cancellation) =>
            Record("cheer");

        public Task IncomingRaidReceivedAsync(
            EventSubIncomingRaidEvent incomingRaid,
            CancellationToken cancellation
        ) => Record("incoming-raid");

        public Task HypeTrainChangedAsync(
            EventSubHypeTrainEvent hypeTrain,
            CancellationToken cancellation
        ) => Record("hype-train");

        public Task ChatNotificationReceivedAsync(
            EventSubChatNotificationEvent notification,
            CancellationToken cancellation
        ) => Record("chat-notification");

        private Task Record(string kind)
        {
            Deliveries.Add(kind);
            return Task.CompletedTask;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using BlokeBot.Commands;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class NativeEventDispatchTests
{
    [Test]
    public async Task RestoredNativeEvents_AreGatedBeforeObserverDispatch()
    {
        var gate = new MutableNativeTwitchFeatureStateProvider();
        var channelPoints = new RecordingChannelPointsObserver();
        var predictions = new RecordingPredictionObserver();
        var session = new EventSubDeliveryHandler(
            null!,
            null!,
            gate,
            [],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            channelPointsObservers: [channelPoints],
            predictionObservers: [predictions]
        );
        var redemption = new EventSubRewardRedemptionEvent(
            "broadcaster-id",
            "channel",
            "redemption-id",
            "reward-id",
            "Reward",
            500,
            "viewer-id",
            "viewer",
            "Viewer",
            "input",
            HelixRewardRedemptionStatus.Unfulfilled,
            DateTimeOffset.UtcNow,
            "redemption-message",
            true
        );
        var prediction = new EventSubPredictionEvent(
            "broadcaster-id",
            "channel",
            "prediction-id",
            "Prediction",
            [],
            "active",
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "prediction-message",
            EventSubPredictionStage.Begin
        );

        await session.DispatchRewardRedemptionAsync(redemption, CancellationToken.None);
        await session.DispatchPredictionAsync(prediction, CancellationToken.None);

        channelPoints.Deliveries.ShouldBe(0);
        predictions.Deliveries.ShouldBe(0);

        _ = gate.EnabledFeatures.Add(NativeTwitchFeature.RewardsAndRedemptions);
        await session.DispatchRewardRedemptionAsync(redemption, CancellationToken.None);
        await session.DispatchPredictionAsync(prediction, CancellationToken.None);

        channelPoints.Deliveries.ShouldBe(1);
        predictions.Deliveries.ShouldBe(0);

        _ = gate.EnabledFeatures.Add(NativeTwitchFeature.Predictions);
        await session.DispatchPredictionAsync(prediction, CancellationToken.None);

        predictions.Deliveries.ShouldBe(1);
        gate.Requests.Select(static request => request.Feature)
            .ShouldBe([
                NativeTwitchFeature.RewardsAndRedemptions,
                NativeTwitchFeature.Predictions,
                NativeTwitchFeature.RewardsAndRedemptions,
                NativeTwitchFeature.Predictions,
                NativeTwitchFeature.Predictions,
            ]);
    }

    [Test]
    public async Task IncomingRaid_TransportNeutralHandlerDispatchesEachAdmittedDelivery()
    {
        var firstGate = new MutableNativeTwitchFeatureStateProvider
        {
            EnabledChannel = "target_login",
        };
        var firstObserver = new RecordingIncomingRaidObserver();
        var firstSession = CreateSession(firstGate, firstObserver);
        var envelope = EventSubNotificationTests.IncomingRaidEnvelope();

        await firstSession.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);
        await firstSession.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);

        firstGate.Channels.ShouldBe(["target_login", "target_login"]);
        firstObserver.Events.Count.ShouldBe(2);
        firstObserver.Events.ShouldAllBe(@event => @event.MessageId == "raid-message-1");

        var secondGate = new MutableNativeTwitchFeatureStateProvider
        {
            EnabledChannel = "target_login",
        };
        var secondObserver = new RecordingIncomingRaidObserver();
        var secondSession = CreateSession(secondGate, secondObserver);

        await secondSession.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);

        _ = secondObserver.Events.ShouldHaveSingleItem();
    }

    [Test]
    public async Task IncomingRaid_DisabledOrWrongTargetInvokesNoObserver()
    {
        var disabledGate = new MutableNativeTwitchFeatureStateProvider();
        var disabledObserver = new RecordingIncomingRaidObserver();
        var disabledSession = CreateSession(disabledGate, disabledObserver);

        await disabledSession.DispatchNotificationAsync(
            EventSubNotificationTests.IncomingRaidEnvelope(),
            "{}",
            CancellationToken.None
        );

        disabledGate.Requests.ShouldBe([("target_login", NativeTwitchFeature.RaidCollaboration)]);
        disabledObserver.Events.ShouldBeEmpty();

        var wrongTargetGate = new MutableNativeTwitchFeatureStateProvider
        {
            EnabledChannel = "source_login",
        };
        var wrongTargetObserver = new RecordingIncomingRaidObserver();
        var wrongTargetSession = CreateSession(wrongTargetGate, wrongTargetObserver);

        await wrongTargetSession.DispatchNotificationAsync(
            EventSubNotificationTests.IncomingRaidEnvelope(),
            "{}",
            CancellationToken.None
        );

        wrongTargetGate.Requests.ShouldBe([
            ("target_login", NativeTwitchFeature.RaidCollaboration),
        ]);
        wrongTargetObserver.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task OutgoingRaid_GatesCollaborationObserverBySourceChannel()
    {
        var envelope = EventSubNotificationTests.IncomingRaidEnvelope();
        var subscription = JsonNode.Parse(envelope.Subscription!.Value.GetRawText())!.AsObject();
        subscription["condition"] = new JsonObject
        {
            ["from_broadcaster_user_id"] = "source-id",
            ["to_broadcaster_user_id"] = string.Empty,
        };
        envelope = envelope with { Subscription = JsonSerializer.SerializeToElement(subscription) };
        var gate = new MutableNativeTwitchFeatureStateProvider { EnabledChannel = "source_login" };
        var observer = new RecordingIncomingRaidObserver();

        await CreateSession(gate, observer)
            .DispatchNotificationAsync(envelope, "{}", CancellationToken.None);

        gate.Requests.ShouldContain(("source_login", NativeTwitchFeature.RaidCollaboration));
        observer
            .Events.ShouldHaveSingleItem()
            .SubscriptionDirection.ShouldBe(EventSubRaidSubscriptionDirection.Outgoing);
    }

    private static EventSubDeliveryHandler CreateSession(
        INativeTwitchFeatureStateProvider gate,
        IIncomingRaidEventObserver observer
    ) =>
        new(
            null!,
            null!,
            gate,
            [],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            incomingRaidObservers: [observer]
        );

    private sealed class MutableNativeTwitchFeatureStateProvider : INativeTwitchFeatureStateProvider
    {
        internal HashSet<NativeTwitchFeature> EnabledFeatures { get; } = [];

        internal string? EnabledChannel { get; init; }

        internal List<string> Channels { get; } = [];

        internal List<(string Channel, NativeTwitchFeature Feature)> Requests { get; } = [];

        public ValueTask<bool> IsEnabledAsync(
            string channel,
            NativeTwitchFeature feature,
            CancellationToken cancellationToken
        )
        {
            Channels.Add(channel);
            Requests.Add((channel, feature));
            return ValueTask.FromResult(
                EnabledFeatures.Contains(feature)
                    || channel.Equals(EnabledChannel, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    private sealed class RecordingIncomingRaidObserver : IIncomingRaidEventObserver
    {
        internal List<EventSubIncomingRaidEvent> Events { get; } = [];

        public Task IncomingRaidReceivedAsync(
            EventSubIncomingRaidEvent incomingRaid,
            CancellationToken cancellationToken
        )
        {
            Events.Add(incomingRaid);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingChannelPointsObserver : IChannelPointsEventObserver
    {
        internal int Deliveries { get; private set; }

        public Task RedemptionReceivedAsync(
            EventSubRewardRedemptionEvent redemption,
            CancellationToken cancellationToken
        )
        {
            Deliveries++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPredictionObserver : IPredictionEventObserver
    {
        internal int Deliveries { get; private set; }

        public Task PredictionReceivedAsync(
            EventSubPredictionEvent prediction,
            CancellationToken cancellationToken
        )
        {
            Deliveries++;
            return Task.CompletedTask;
        }
    }
}

using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Twitch;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class NativeEventDispatchTests
{
    [Test]
    public async Task RestoredNativeEvents_AreGatedBeforeObserverDispatch()
    {
        var gate = new MutableNativeTwitchFeatureStateProvider();
        var channelPoints = new RecordingChannelPointsObserver();
        var predictions = new RecordingPredictionObserver();
        var session = new EventSubConnectionSession(
            null!,
            null!,
            null!,
            null!,
            new BotRuntimeStatusStore(),
            gate,
            new EventSubChannelReconciliationTrigger(null!),
            [],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            NullLogger<EventSubConnectionSession>.Instance,
            channelPointsObservers: [channelPoints],
            predictionObservers: [predictions]
        );
        var redemption = new EventSubRewardRedemptionEvent(
            "broadcaster-id",
            "channel",
            "redemption-id",
            "reward-id",
            "Reward",
            "viewer-id",
            "viewer",
            "input",
            HelixRewardRedemptionStatus.Unfulfilled,
            DateTimeOffset.UtcNow,
            "redemption-message"
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
            "prediction-message"
        );

        await session.DispatchRewardRedemptionAsync(redemption, CancellationToken.None);
        await session.DispatchPredictionAsync(prediction, CancellationToken.None);

        channelPoints.Deliveries.ShouldBe(0);
        predictions.Deliveries.ShouldBe(0);

        gate.Enabled = true;
        await session.DispatchRewardRedemptionAsync(redemption, CancellationToken.None);
        await session.DispatchPredictionAsync(prediction, CancellationToken.None);

        channelPoints.Deliveries.ShouldBe(1);
        predictions.Deliveries.ShouldBe(1);
    }

    [Test]
    public async Task IncomingRaid_TargetGateAndConnectionLocalDuplicateSuppressionPrecedeObserver()
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

        firstGate.Channels.ShouldBe(["target_login"]);
        firstObserver.Events.ShouldHaveSingleItem().MessageId.ShouldBe("raid-message-1");

        var secondGate = new MutableNativeTwitchFeatureStateProvider
        {
            EnabledChannel = "target_login",
        };
        var secondObserver = new RecordingIncomingRaidObserver();
        var secondSession = CreateSession(secondGate, secondObserver);

        await secondSession.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);

        secondObserver.Events.ShouldHaveSingleItem();
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

        disabledGate.Channels.ShouldBe(["target_login"]);
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

        wrongTargetGate.Channels.ShouldBe(["target_login"]);
        wrongTargetObserver.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task IncomingRaid_MalformedPayloadOrTimestampInvokesNoGateOrObserver()
    {
        var gate = new MutableNativeTwitchFeatureStateProvider { EnabledChannel = "target_login" };
        var observer = new RecordingIncomingRaidObserver();
        var session = CreateSession(gate, observer);

        foreach (var envelope in EventSubNotificationTests.InvalidIncomingRaidEnvelopes())
        {
            await session.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);
        }

        gate.Channels.ShouldBeEmpty();
        observer.Events.ShouldBeEmpty();
    }

    private static EventSubConnectionSession CreateSession(
        INativeTwitchFeatureStateProvider gate,
        IIncomingRaidEventObserver observer
    )
    {
        return new(
            null!,
            null!,
            null!,
            null!,
            new BotRuntimeStatusStore(),
            gate,
            new EventSubChannelReconciliationTrigger(null!),
            [],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            NullLogger<EventSubConnectionSession>.Instance,
            incomingRaidObservers: [observer]
        );
    }

    private sealed class MutableNativeTwitchFeatureStateProvider : INativeTwitchFeatureStateProvider
    {
        internal bool Enabled { get; set; }

        internal string? EnabledChannel { get; init; }

        internal List<string> Channels { get; } = [];

        public ValueTask<bool> IsEnabledAsync(string channel, CancellationToken cancellationToken)
        {
            Channels.Add(channel);
            return ValueTask.FromResult(
                Enabled || channel.Equals(EnabledChannel, StringComparison.OrdinalIgnoreCase)
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

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

    private sealed class MutableNativeTwitchFeatureStateProvider : INativeTwitchFeatureStateProvider
    {
        internal bool Enabled { get; set; }

        public ValueTask<bool> IsEnabledAsync(string channel, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Enabled);
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

using System.Collections.ObjectModel;
using System.Text.Json;
using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

internal interface IEventSubDeliveryHandler
{
    Task DispatchNotificationAsync(
        EventSubEnvelope envelope,
        string rawJson,
        CancellationToken cancellationToken
    );
}

internal sealed class EventSubDeliveryHandler(
    ChatCommandDispatcher dispatcher,
    ICommandResponseSender responses,
    INativeTwitchFeatureStateProvider nativeTwitch,
    IEnumerable<IChatMessageObserver> messageObservers,
    ObserverFanOut<
        EventSubMessageObserverBoundary,
        ChatMessage,
        ChatObserverDeadLetter
    > messageObserverFanOut,
    IEnumerable<IShoutoutEventObserver>? shoutoutObservers = null,
    IEnumerable<IPollEventObserver>? pollObservers = null,
    IEnumerable<IChannelPointsEventObserver>? channelPointsObservers = null,
    IEnumerable<IPredictionEventObserver>? predictionObservers = null,
    IEnumerable<IIncomingRaidEventObserver>? incomingRaidObservers = null
) : IEventSubDeliveryHandler
{
    private static readonly ObserverEventIdentity _chatMessageEvent = ObserverEventIdentity.Named(
        "TwitchChatMessage"
    );
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IChatMessageObserver[] _messageObservers = [.. messageObservers];
    private readonly IShoutoutEventObserver[] _shoutoutObservers = [.. shoutoutObservers ?? []];
    private readonly IPollEventObserver[] _pollObservers = [.. pollObservers ?? []];
    private readonly IChannelPointsEventObserver[] _channelPointsObservers =
    [
        .. channelPointsObservers ?? [],
    ];
    private readonly IPredictionEventObserver[] _predictionObservers =
    [
        .. predictionObservers ?? [],
    ];
    private readonly IIncomingRaidEventObserver[] _incomingRaidObservers =
    [
        .. incomingRaidObservers ?? [],
    ];

    internal async Task DispatchChatMessageAsync(
        EventSubChatMessageEvent chatEvent,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        var text = chatEvent.Message?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var message = new ChatMessage(
            chatEvent.ChatterUserLogin,
            chatEvent.BroadcasterUserLogin,
            text,
            rawJson,
            CreateTags(chatEvent)
        );

        await NotifyMessageObserversAsync(message, cancellationToken);
        await dispatcher.DispatchResponsesAsync(
            message,
            async (response, ct) => await responses.SendAsync(message, response, ct),
            cancellationToken
        );
    }

    private async Task DispatchShoutoutAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                shoutout.BroadcasterUserLogin,
                NativeTwitchFeature.Shoutouts,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _shoutoutObservers)
        {
            await observer.ShoutoutReceivedAsync(shoutout, cancellationToken);
        }
    }

    public async Task DispatchNotificationAsync(
        EventSubEnvelope envelope,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        switch (EventSubNotification.Parse(envelope, _jsonOptions))
        {
            case EventSubNotification.Chat { Event: var chatEvent }:
                await DispatchChatMessageAsync(chatEvent, rawJson, cancellationToken);
                break;
            case EventSubNotification.Shoutout { Event: var shoutout }:
                await DispatchShoutoutAsync(shoutout, cancellationToken);
                break;
            case EventSubNotification.IncomingRaid { Event: var incomingRaid }:
                await DispatchIncomingRaidAsync(incomingRaid, cancellationToken);
                break;
            case EventSubNotification.Poll { Event: var poll }:
                await DispatchPollAsync(poll, cancellationToken);
                break;
            case EventSubNotification.Prediction { Event: var prediction }:
                await DispatchPredictionAsync(prediction, cancellationToken);
                break;
            case EventSubNotification.RewardRedemption { Event: var redemption }:
                await DispatchRewardRedemptionAsync(redemption, cancellationToken);
                break;
        }
    }

    private async ValueTask NotifyMessageObserversAsync(
        ChatMessage message,
        CancellationToken cancellationToken
    ) =>
        _ = await messageObserverFanOut.DispatchAsync(
            _messageObservers,
            _ => new ObserverDispatch<ChatMessage, ChatObserverDeadLetter>
            {
                Event = message,
                EventIdentity = _chatMessageEvent,
                DeadLetter = new ChatObserverDeadLetter(message.Channel),
            },
            observer => ObserverIdentity.For(observer.GetType()),
            static (observer, chatMessage, token) =>
                observer.MessageReceivedAsync(chatMessage, token),
            cancellationToken
        );

    private static IReadOnlyDictionary<string, string> CreateTags(
        EventSubChatMessageEvent chatEvent
    )
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = chatEvent.MessageId,
            ["user-id"] = chatEvent.ChatterUserId,
            ["badges"] = string.Join(
                ',',
                chatEvent.Badges.Select(static badge => $"{badge.SetId}/{badge.Id}")
            ),
        };

        if (
            chatEvent.Badges.Any(static badge =>
                badge.SetId.Equals("moderator", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            tags["mod"] = "1";
        }

        return new ReadOnlyDictionary<string, string>(tags);
    }

    private async Task DispatchPollAsync(
        EventSubPollEvent poll,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                poll.BroadcasterUserLogin,
                NativeTwitchFeature.Polls,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _pollObservers)
        {
            await observer.PollReceivedAsync(poll, cancellationToken);
        }
    }

    internal async Task DispatchIncomingRaidAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                incomingRaid.ToBroadcasterUserLogin,
                NativeTwitchFeature.Shoutouts,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _incomingRaidObservers)
        {
            await observer.IncomingRaidReceivedAsync(incomingRaid, cancellationToken);
        }
    }

    internal async Task DispatchPredictionAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                prediction.BroadcasterUserLogin,
                NativeTwitchFeature.Predictions,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _predictionObservers)
        {
            await observer.PredictionReceivedAsync(prediction, cancellationToken);
        }
    }

    internal async Task DispatchRewardRedemptionAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                redemption.BroadcasterUserLogin,
                NativeTwitchFeature.RewardsAndRedemptions,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _channelPointsObservers)
        {
            await observer.RedemptionReceivedAsync(redemption, cancellationToken);
        }
    }
}

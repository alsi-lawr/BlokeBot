using System.Collections.ObjectModel;
using System.Text.Json;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface IEventSubDeliveryHandler
{
    Task DispatchNotificationAsync(
        EventSubEnvelope envelope,
        string rawJson,
        CancellationToken cancellationToken
    );
}

internal sealed partial class EventSubDeliveryHandler(
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
    IEnumerable<IIncomingRaidEventObserver>? incomingRaidObservers = null,
    IEnumerable<ITwitchEventAutomationObserver>? automationObservers = null,
    IEnumerable<IPluginTwitchEventObserver>? pluginObservers = null,
    IEnumerable<IEventSubRawObserver>? rawObservers = null,
    IEnumerable<IExpandedTwitchEventObserver>? expandedObservers = null,
    Microsoft.Extensions.Logging.ILogger<EventSubDeliveryHandler>? log = null
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
    private readonly ITwitchEventAutomationObserver[] _automationObservers =
    [
        .. automationObservers ?? [],
    ];
    private readonly IPluginTwitchEventObserver[] _pluginObservers = [.. pluginObservers ?? []];
    private readonly IExpandedTwitchEventObserver[] _expandedObservers =
    [
        .. expandedObservers ?? [],
    ];
    private readonly EventSubRawDelivery _rawDelivery = new(rawObservers);

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

    public async Task DispatchNotificationAsync(
        EventSubEnvelope envelope,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        switch (EventSubNotification.Parse(envelope, _jsonOptions))
        {
            case EventSubNotification.AdBreak { Event: var ad }:
                await NotifyExpandedAsync(
                    (o, t) => o.AdBreakStartedAsync(ad, t),
                    cancellationToken
                );
                break;
            case EventSubNotification.Goal { Event: var goal }:
                await NotifyExpandedAsync((o, t) => o.GoalChangedAsync(goal, t), cancellationToken);
                break;
            case EventSubNotification.ChatSettings { Event: var settings }:
                await NotifyExpandedAsync(
                    (o, t) => o.ChatSettingsChangedAsync(settings, t),
                    cancellationToken
                );
                break;
            case EventSubNotification.Moderation { Event: var moderation }:
                await NotifyExpandedAsync(
                    (o, t) => o.ModerationOccurredAsync(moderation, t),
                    cancellationToken
                );
                break;
            case EventSubNotification.Chat { Event: var chatEvent }:
                if (
                    envelope.Metadata.MessageTimestamp is { } at
                    && !string.IsNullOrWhiteSpace(chatEvent.BroadcasterUserId)
                )
                {
                    await NotifyExpandedAsync(
                        (o, t) =>
                            o.ChatReceivedAsync(
                                new(
                                    chatEvent.MessageId,
                                    at,
                                    chatEvent.BroadcasterUserId,
                                    chatEvent.SourceBroadcasterUserId
                                        ?? chatEvent.BroadcasterUserId,
                                    chatEvent.ChatterUserId,
                                    chatEvent.ChatterUserLogin,
                                    chatEvent.ChatterUserName,
                                    chatEvent.Message?.Text ?? "",
                                    [
                                        .. chatEvent
                                            .Message?.Fragments.Where(f =>
                                                f.Type == "emote" && f.Emote is not null
                                            )
                                            .Select(f => f.Emote!.Id)
                                            ?? [],
                                    ]
                                ),
                                t
                            ),
                        cancellationToken
                    );
                }
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
            case EventSubNotification.StreamOnline { Event: var streamOnline }:
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.StreamOnlineAsync(streamOnline, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.StreamOffline { Event: var streamOffline }:
                await NotifyExpandedAsync(
                    (o, t) => o.StreamEndedAsync(streamOffline, t),
                    cancellationToken
                );
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.StreamOfflineAsync(streamOffline, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.ChannelUpdate { Event: var channelUpdate }:
                await NotifyExpandedAsync(
                    (o, t) => o.MetadataChangedAsync(channelUpdate, t),
                    cancellationToken
                );
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.ChannelUpdatedAsync(channelUpdate, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.Follow { Event: var follow }:
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.FollowReceivedAsync(follow, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.Subscription { Event: var subscription }:
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.SubscriptionReceivedAsync(subscription, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.SubscriptionGift { Event: var gift }:
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.SubscriptionGiftReceivedAsync(gift, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.Cheer { Event: var cheer }:
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.CheerReceivedAsync(cheer, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.HypeTrain { Event: var hypeTrain }:
                await NotifyAutomationObserversAsync(
                    (observer, token) => observer.HypeTrainChangedAsync(hypeTrain, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.ChatNotification { Event: var chatNotification }:
                await NotifyAutomationObserversAsync(
                    (observer, token) =>
                        observer.ChatNotificationReceivedAsync(chatNotification, token),
                    cancellationToken
                );
                break;
            case EventSubNotification.Unknown:
                await _rawDelivery.DispatchAsync(envelope, cancellationToken);
                break;
        }
    }

    private async Task NotifyExpandedAsync(
        Func<IExpandedTwitchEventObserver, CancellationToken, Task> notify,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in _expandedObservers)
        {
            try
            {
                await notify(observer, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                log?.LogError(
                    "Expanded Twitch automation observer failed ({FailureType}); the acknowledged delivery is not replayed.",
                    failure.GetType().Name
                );
            }
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
}

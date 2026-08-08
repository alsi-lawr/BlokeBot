using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Maps Twitch EventSub payloads to bounded automation contexts. Every context carries only the
/// documented fields below; access tokens and raw transport headers are never included.
/// <list type="bullet">
/// <item><c>stream-online</c>: stream identity plus safe <c>stream_type</c>.</item>
/// <item><c>stream-offline</c>: channel and timestamps only.</item>
/// <item><c>follow</c>: follower actor plus safe <c>followed_at</c>.</item>
/// <item><c>subscription</c>: subscriber actor plus safe <c>sub_tier</c> and <c>is_gift</c>.</item>
/// <item><c>subscription-gift</c>: optional gifter actor plus safe <c>gift_count</c>,
/// <c>sub_tier</c>, and <c>is_anonymous</c>.</item>
/// <item><c>cheer</c>: optional cheerer actor plus safe <c>bits</c> and <c>is_anonymous</c>, and
/// sensitive <c>cheer_message</c>.</item>
/// <item><c>incoming-raid</c>: raiding broadcaster actor plus safe <c>viewer_count</c>.</item>
/// <item><c>hype-train-*</c>: safe <c>hype_train_level</c> and <c>hype_train_total</c>.</item>
/// <item><c>chat-notification</c>: optional chatter actor plus safe <c>notice_type</c> and
/// sensitive <c>system_message</c> and <c>message_text</c>.</item>
/// </list>
/// </summary>
internal static class TwitchEventAutomationContext
{
    internal const int MaximumTextLength = 500;

    internal static AutomationContext StreamOnline(
        BotHost host,
        EventSubStreamOnlineEvent streamOnline,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.StreamOnlineSource,
            actor: null,
            stream: new(Bound(streamOnline.StreamId), null, null, streamOnline.StartedAt),
            streamOnline.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("stream_type")] = SafeText(Bound(streamOnline.StreamType)),
            }
        );

    internal static AutomationContext StreamOffline(
        BotHost host,
        EventSubStreamOfflineEvent streamOffline,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.StreamOfflineSource,
            actor: null,
            stream: null,
            streamOffline.MessageTimestamp,
            receivedAtUtc,
            []
        );

    internal static AutomationContext Follow(
        BotHost host,
        EventSubFollowEvent follow,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.FollowSource,
            Actor(follow.UserId, follow.UserLogin, follow.UserName),
            stream: null,
            follow.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("followed_at")] = SafeTimestamp(follow.FollowedAt),
            }
        );

    internal static AutomationContext Subscription(
        BotHost host,
        EventSubSubscriptionEvent subscription,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.SubscriptionSource,
            Actor(subscription.UserId, subscription.UserLogin, subscription.UserName),
            stream: null,
            subscription.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("sub_tier")] = SafeText(Bound(subscription.Tier)),
                [new("is_gift")] = SafeBoolean(subscription.IsGift),
            }
        );

    internal static AutomationContext SubscriptionGift(
        BotHost host,
        EventSubSubscriptionGiftEvent gift,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.SubscriptionGiftSource,
            OptionalActor(gift.UserId, gift.UserLogin, gift.UserName),
            stream: null,
            gift.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("gift_count")] = SafeNumber(gift.Total),
                [new("sub_tier")] = SafeText(Bound(gift.Tier)),
                [new("is_anonymous")] = SafeBoolean(gift.IsAnonymous),
            }
        );

    internal static AutomationContext Cheer(
        BotHost host,
        EventSubCheerEvent cheer,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.CheerSource,
            OptionalActor(cheer.UserId, cheer.UserLogin, cheer.UserName),
            stream: null,
            cheer.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("bits")] = SafeNumber(cheer.Bits),
                [new("is_anonymous")] = SafeBoolean(cheer.IsAnonymous),
                [new("cheer_message")] = SensitiveText(Bound(cheer.Message)),
            }
        );

    internal static AutomationContext IncomingRaid(
        BotHost host,
        EventSubIncomingRaidEvent incomingRaid,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.IncomingRaidSource,
            Actor(
                incomingRaid.FromBroadcasterUserId,
                incomingRaid.FromBroadcasterUserLogin,
                incomingRaid.FromBroadcasterUserName
            ),
            stream: null,
            incomingRaid.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("viewer_count")] = SafeNumber(incomingRaid.ViewerCount),
            }
        );

    internal static AutomationContext HypeTrain(
        BotHost host,
        AutomationDefinitionId definitionId,
        EventSubHypeTrainEvent hypeTrain,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            definitionId,
            actor: null,
            stream: null,
            hypeTrain.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("hype_train_level")] = SafeNumber(hypeTrain.Level),
                [new("hype_train_total")] = SafeNumber(hypeTrain.Total),
            }
        );

    internal static AutomationContext ChatNotification(
        BotHost host,
        EventSubChatNotificationEvent notification,
        DateTimeOffset receivedAtUtc
    ) =>
        Create(
            host,
            AutomationDefinitionIds.ChatNotificationSource,
            OptionalActor(
                notification.ChatterUserId,
                notification.ChatterUserLogin,
                notification.ChatterUserName
            ),
            stream: null,
            notification.MessageTimestamp,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("notice_type")] = SafeText(Bound(notification.NoticeType)),
                [new("system_message")] = SensitiveText(Bound(notification.SystemMessage)),
                [new("message_text")] = SensitiveText(Bound(notification.MessageText)),
            }
        );

    private static AutomationContext Create(
        BotHost host,
        AutomationDefinitionId definitionId,
        AutomationActor? actor,
        AutomationStream? stream,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc,
        IEnumerable<KeyValuePair<AutomationVariableName, AutomationVariable>> variables
    ) =>
        new(
            // The durable delivery receipt is the sole deduplication authority. A fresh occurrence
            // identity keeps a Twitch redelivery after the receipt window a new occurrence.
            new(Guid.NewGuid(), definitionId),
            actor,
            new(
                new(host.Id),
                host.TwitchUserId ?? string.Empty,
                host.Login,
                string.IsNullOrWhiteSpace(host.DisplayName) ? host.Login : host.DisplayName
            ),
            stream,
            new(occurredAtUtc, receivedAtUtc),
            [],
            new(variables)
        );

    private static AutomationActor Actor(string userId, string login, string displayName) =>
        new(
            Bound(userId),
            Bound(login),
            string.IsNullOrWhiteSpace(displayName) ? Bound(login) : Bound(displayName)
        );

    private static AutomationActor? OptionalActor(
        string? userId,
        string? login,
        string? displayName
    ) =>
        string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(login)
            ? null
            : Actor(userId, login, displayName ?? string.Empty);

    private static string Bound(string value) =>
        value.Length <= MaximumTextLength ? value : value[..MaximumTextLength];

    private static AutomationVariable SafeText(string value) =>
        new(new AutomationValue.Text(value), AutomationDataSensitivity.Safe);

    private static AutomationVariable SafeNumber(int value) =>
        new(new AutomationValue.Number(value), AutomationDataSensitivity.Safe);

    private static AutomationVariable SafeBoolean(bool value) =>
        new(new AutomationValue.Boolean(value), AutomationDataSensitivity.Safe);

    private static AutomationVariable SafeTimestamp(DateTimeOffset value) =>
        new(new AutomationValue.Timestamp(value), AutomationDataSensitivity.Safe);

    private static AutomationVariable SensitiveText(string value) =>
        new(new AutomationValue.Text(value), AutomationDataSensitivity.Sensitive);
}

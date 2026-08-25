using System.Diagnostics;
using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelOperations(
    BotSettings settings,
    IBotAccountProvider accounts,
    ChatIdentityResolver identities,
    IEventSubSubscriptionTransport subscriptions,
    IStartupChatMessageProvider startupMessages,
    IPublicChatMessageSender sender,
    IBotChannelLifecycleNotifier lifecycle,
    INativeTwitchFeatureStateProvider nativeTwitch,
    IBroadcasterAccountProvider? broadcasters = null,
    IAutomationEventSubRequirementSource? automationRequirements = null,
    IEnumerable<IEventSubRequirementSource>? eventRequirements = null,
    IEnumerable<IEventSubExactRequirementSource>? exactRequirements = null
) : IEventSubChannelOperations
{
    private readonly IEventSubRequirementSource[] _eventRequirements = [.. eventRequirements ?? []];
    private readonly IEventSubExactRequirementSource[] _exactRequirements =
    [
        .. exactRequirements ?? [],
    ];
    private static readonly IReadOnlyDictionary<
        EventSubBroadcasterOperationKind,
        IReadOnlyList<(string Type, string Version)>
    > _broadcasterOperationSubscriptions = new Dictionary<
        EventSubBroadcasterOperationKind,
        IReadOnlyList<(string Type, string Version)>
    >
    {
        [EventSubBroadcasterOperationKind.Polls] =
        [
            ("channel.poll.begin", "1"),
            ("channel.poll.progress", "1"),
            ("channel.poll.end", "1"),
        ],
        [EventSubBroadcasterOperationKind.RewardRedemptions] =
        [
            ("channel.channel_points_custom_reward_redemption.add", "1"),
            ("channel.channel_points_custom_reward_redemption.update", "1"),
        ],
        [EventSubBroadcasterOperationKind.Predictions] =
        [
            ("channel.prediction.begin", "1"),
            ("channel.prediction.progress", "1"),
            ("channel.prediction.lock", "1"),
            ("channel.prediction.end", "1"),
        ],
        [EventSubBroadcasterOperationKind.AutomationSubscriptions] =
        [
            ("channel.subscribe", "1"),
            ("channel.subscription.gift", "1"),
        ],
        [EventSubBroadcasterOperationKind.AutomationCheers] = [("channel.cheer", "1")],
        [EventSubBroadcasterOperationKind.AutomationHypeTrain] =
        [
            ("channel.hype_train.begin", "2"),
            ("channel.hype_train.progress", "2"),
            ("channel.hype_train.end", "2"),
        ],
    };

    public IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(
        string channel,
        EventSubAuthorizationContext authorization
    ) =>
        authorization.Match(
            _ => accounts.GetBotAccount(channel),
            _ => accounts.GetBotAccount(channel),
            _ =>
                broadcasters?.GetBroadcasterAccount(channel)
                ?? IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                    ValueTask.FromResult(
                        Result<BotAccount, AccessTokenUnavailableReason>.Error(
                            AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                        )
                    )
                )
        );

    public ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken cancellationToken,
        EventSubOperationSubscriptionKind? operationKind = null
    ) =>
        operationKind switch
        {
            EventSubOperationSubscriptionKind.Raids => CreateConfiguredBotRaidSubscriptionAsync(
                channel,
                authorization,
                account,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.OutgoingRaids =>
                CreateConfiguredBotOutgoingRaidSubscriptionAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationStream =>
                CreateAutomationStreamSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationChannelUpdates =>
                CreateBroadcasterOperationSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    [("channel.update", "2")],
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationFollows =>
                CreateAutomationBotConditionSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    [("channel.follow", "2", "moderator_user_id")],
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationChatNotifications =>
                CreateAutomationBotConditionSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    [("channel.chat.notification", "1", "user_id")],
                    cancellationToken
                ),
            _ => CreateAuthorizedSubscriptionAsync(
                channel,
                authorization,
                account,
                cancellationToken
            ),
        };

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateAuthorizedSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken cancellationToken
    ) =>
        authorization.Match(
            _ =>
                CreateConfiguredBotSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            _ =>
                CreateConfiguredBotOperationSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            broadcaster =>
                _broadcasterOperationSubscriptions.TryGetValue(
                    broadcaster.Operation,
                    out var subscriptionTypes
                )
                    ? CreateBroadcasterOperationSubscriptionsAsync(
                        channel,
                        authorization,
                        account,
                        subscriptionTypes,
                        cancellationToken
                    )
                    : throw new UnreachableException("Unknown broadcaster EventSub operation kind.")
        );
}

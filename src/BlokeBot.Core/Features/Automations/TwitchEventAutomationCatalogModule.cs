using System.Collections.Immutable;
using System.Text.Json;
using static BlokeBot.Core.Features.Automations.AutomationConfigurationJson;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Static metadata for the Twitch EventSub automation sources: the EventSub subscription group each
/// source needs and the broadcaster OAuth scopes required beyond the bot account's own grant.
/// </summary>
public sealed record TwitchEventAutomationSourceDescriptor(
    AutomationDefinitionId DefinitionId,
    AutomationEventSubRequirement Requirement,
    ImmutableArray<string> BroadcasterScopes,
    string SubscriptionTypes
);

public static class TwitchEventAutomationSources
{
    public const string SubscriptionsScope = "channel:read:subscriptions";
    public const string BitsScope = "bits:read";
    public const string HypeTrainScope = "channel:read:hype_train";
    public const string RedemptionsReadScope = "channel:read:redemptions";
    public const string RedemptionsManageScope = "channel:manage:redemptions";
    public const string PollsReadScope = "channel:read:polls";
    public const string PredictionsReadScope = "channel:read:predictions";

    public static ImmutableArray<TwitchEventAutomationSourceDescriptor> All { get; } =
    [
        new(
            AutomationDefinitionIds.StreamOnlineSource,
            AutomationEventSubRequirement.Stream,
            [],
            "stream.online"
        ),
        new(
            AutomationDefinitionIds.StreamOfflineSource,
            AutomationEventSubRequirement.Stream,
            [],
            "stream.offline"
        ),
        new(
            AutomationDefinitionIds.FollowSource,
            AutomationEventSubRequirement.Follows,
            [],
            "channel.follow"
        ),
        new(
            AutomationDefinitionIds.SubscriptionSource,
            AutomationEventSubRequirement.Subscriptions,
            [SubscriptionsScope],
            "channel.subscribe"
        ),
        new(
            AutomationDefinitionIds.SubscriptionGiftSource,
            AutomationEventSubRequirement.Subscriptions,
            [SubscriptionsScope],
            "channel.subscription.gift"
        ),
        new(
            AutomationDefinitionIds.CheerSource,
            AutomationEventSubRequirement.Cheers,
            [BitsScope],
            "channel.cheer"
        ),
        new(
            AutomationDefinitionIds.IncomingRaidSource,
            AutomationEventSubRequirement.IncomingRaids,
            [],
            "channel.raid"
        ),
        new(
            AutomationDefinitionIds.HypeTrainBeginSource,
            AutomationEventSubRequirement.HypeTrain,
            [HypeTrainScope],
            "channel.hype_train.begin"
        ),
        new(
            AutomationDefinitionIds.HypeTrainProgressSource,
            AutomationEventSubRequirement.HypeTrain,
            [HypeTrainScope],
            "channel.hype_train.progress"
        ),
        new(
            AutomationDefinitionIds.HypeTrainEndSource,
            AutomationEventSubRequirement.HypeTrain,
            [HypeTrainScope],
            "channel.hype_train.end"
        ),
        new(
            AutomationDefinitionIds.ChatNotificationSource,
            AutomationEventSubRequirement.ChatNotifications,
            [],
            "channel.chat.notification"
        ),
        // The redemption EventSub subscription lifecycle is owned by the Rewards & redemptions
        // feature; this descriptor only surfaces the subscription type and scope readiness. The
        // manage scope covers the Fulfil/Cancel actions and the source completion policy.
        new(
            AutomationDefinitionIds.RewardRedemptionSource,
            AutomationEventSubRequirement.Redemptions,
            [RedemptionsReadScope, RedemptionsManageScope],
            "channel.channel_points_custom_reward_redemption.add"
        ),
        // The shoutout EventSub subscription lifecycle is owned by the Raid & collaboration feature
        // and uses the configured bot account's moderator scopes rather than the broadcaster grant,
        // so no broadcaster scopes are listed here.
        new(
            AutomationDefinitionIds.ShoutoutSentSource,
            AutomationEventSubRequirement.Shoutouts,
            [],
            "channel.shoutout.create"
        ),
        new(
            AutomationDefinitionIds.ShoutoutReceivedSource,
            AutomationEventSubRequirement.Shoutouts,
            [],
            "channel.shoutout.receive"
        ),
        // The poll and prediction EventSub subscription lifecycles are owned by their Native
        // Twitch features; these descriptors only surface the subscription type and the
        // broadcaster read scope each subscription needs.
        new(
            AutomationDefinitionIds.PollStartedSource,
            AutomationEventSubRequirement.Polls,
            [PollsReadScope],
            "channel.poll.begin"
        ),
        new(
            AutomationDefinitionIds.PollProgressedSource,
            AutomationEventSubRequirement.Polls,
            [PollsReadScope],
            "channel.poll.progress"
        ),
        new(
            AutomationDefinitionIds.PollEndedSource,
            AutomationEventSubRequirement.Polls,
            [PollsReadScope],
            "channel.poll.end"
        ),
        new(
            AutomationDefinitionIds.PredictionStartedSource,
            AutomationEventSubRequirement.Predictions,
            [PredictionsReadScope],
            "channel.prediction.begin"
        ),
        new(
            AutomationDefinitionIds.PredictionProgressedSource,
            AutomationEventSubRequirement.Predictions,
            [PredictionsReadScope],
            "channel.prediction.progress"
        ),
        new(
            AutomationDefinitionIds.PredictionLockedSource,
            AutomationEventSubRequirement.Predictions,
            [PredictionsReadScope],
            "channel.prediction.lock"
        ),
        new(
            AutomationDefinitionIds.PredictionEndedSource,
            AutomationEventSubRequirement.Predictions,
            [PredictionsReadScope],
            "channel.prediction.end"
        ),
    ];

    public static ImmutableArray<string> ChatNotificationNoticeTypes { get; } =
    [
        "any",
        "announcement",
        "sub",
        "resub",
        "sub_gift",
        "community_sub_gift",
        "gift_paid_upgrade",
        "prime_paid_upgrade",
        "raid",
        "unraid",
        "pay_it_forward",
        "bits_badge_tier",
        "charity_donation",
    ];

    public static ImmutableArray<string> RedemptionCompletionPolicies { get; } =
    ["manual", "fulfil-on-success", "cancel-on-failure"];

    internal static RedemptionCompletionPolicy? ParseCompletionPolicy(string token) =>
        token switch
        {
            "manual" => RedemptionCompletionPolicy.Manual,
            "fulfil-on-success" => RedemptionCompletionPolicy.FulfilOnSuccess,
            "cancel-on-failure" => RedemptionCompletionPolicy.CancelOnFailure,
            _ => null,
        };
}

internal sealed class TwitchEventAutomationCatalogModule : IAutomationCatalogModule
{
    private static readonly AutomationSchemaCompatibility _schema = new(new(1), new(1));

    public AutomationModuleId Id { get; } = new("blokebot.twitch-events");

    public IEnumerable<IAutomationDefinition> Definitions { get; } =
    [
        StreamOnlineSource(),
        StreamOfflineSource(),
        FollowSource(),
        SubscriptionSource(),
        SubscriptionGiftSource(),
        CheerSource(),
        IncomingRaidSource(),
        HypeTrainSource(
            AutomationDefinitionIds.HypeTrainBeginSource,
            "Hype Train started",
            "Starts an automation when a Hype Train begins in the channel."
        ),
        HypeTrainSource(
            AutomationDefinitionIds.HypeTrainProgressSource,
            "Hype Train progressed",
            "Starts an automation when a Hype Train gains progress or levels up."
        ),
        HypeTrainSource(
            AutomationDefinitionIds.HypeTrainEndSource,
            "Hype Train ended",
            "Starts an automation when a Hype Train finishes."
        ),
        ChatNotificationSource(),
        RewardRedemptionSource(),
        FulfilRedemptionAction(),
        CancelRedemptionAction(),
    ];

    private static AutomationPortMetadata FlowPort() =>
        new(new("flow"), "Flow", "Starts the connected automation.", AutomationPortValueType.Flow);

    private static AutomationPortMetadata ChannelPort() =>
        new(
            new("channel"),
            "Channel",
            "The channel that received the Twitch event.",
            AutomationPortValueType.Channel
        );

    private static AutomationPortMetadata EventTimePort() =>
        new(
            new("event-time"),
            "Event time",
            "When Twitch reported the event.",
            AutomationPortValueType.Timestamp
        );

    private static AutomationPortMetadata ActorPort(string name, string description) =>
        new(
            new("actor"),
            name,
            description,
            AutomationPortValueType.Actor,
            AutomationDataSensitivity.Sensitive
        );

    private static AutomationDefinition<StreamOnlineSourceConfiguration> StreamOnlineSource() =>
        new(
            new(
                AutomationDefinitionIds.StreamOnlineSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Stream went live",
                    "Starts an automation when the channel's stream goes live.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ChannelPort(),
                    EventTimePort(),
                    new(
                        new("stream"),
                        "Stream",
                        "The stream that just went live.",
                        AutomationPortValueType.Stream
                    ),
                ],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new StreamOnlineSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<StreamOfflineSourceConfiguration> StreamOfflineSource() =>
        new(
            new(
                AutomationDefinitionIds.StreamOfflineSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Stream went offline",
                    "Starts an automation when the channel's stream ends.",
                    "Twitch events"
                ),
                [],
                [FlowPort(), ChannelPort(), EventTimePort()],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new StreamOfflineSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<FollowSourceConfiguration> FollowSource() =>
        new(
            new(
                AutomationDefinitionIds.FollowSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "New follower",
                    "Starts an automation when a viewer follows the channel.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Follower", "The viewer who followed the channel."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new FollowSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<SubscriptionSourceConfiguration> SubscriptionSource() =>
        new(
            new(
                AutomationDefinitionIds.SubscriptionSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "New subscription",
                    "Starts an automation when a viewer subscribes to the channel.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Subscriber", "The viewer who subscribed."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new SubscriptionSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<SubscriptionGiftSourceConfiguration> SubscriptionGiftSource() =>
        new(
            new(
                AutomationDefinitionIds.SubscriptionGiftSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Gifted subscriptions",
                    "Starts an automation when a viewer gifts subscriptions to the channel.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Gifter", "The viewer who gifted, when the gift is not anonymous."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [
                    new(
                        new("minimum-gift-count"),
                        "Minimum gifts",
                        "The smallest number of gifted subscriptions that starts this automation.",
                        new AutomationConfigurationFieldType.Number(1, 100000),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static json =>
                TryReadInt32(json, "minimum-gift-count", out var minimum)
                    ? Parsed(new SubscriptionGiftSourceConfiguration(minimum))
                    : Invalid("minimum-gift-count", "Enter a whole-number minimum gift count."),
            static configuration =>
                configuration.MinimumGiftCount is >= 1 and <= 100000
                    ? AutomationValidationResult.Valid
                    : AutomationValidationResult.Invalid(
                        new AutomationValidationTarget.Field(new("minimum-gift-count")),
                        "Choose a minimum gift count from 1 to 100,000."
                    )
        );

    private static AutomationDefinition<CheerSourceConfiguration> CheerSource() =>
        new(
            new(
                AutomationDefinitionIds.CheerSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Cheer",
                    "Starts an automation when a viewer cheers Bits in the channel.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort(
                        "Cheerer",
                        "The viewer who cheered, when the cheer is not anonymous."
                    ),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [
                    new(
                        new("minimum-bits"),
                        "Minimum Bits",
                        "The smallest cheer that starts this automation.",
                        new AutomationConfigurationFieldType.Number(1, 1000000),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static json =>
                TryReadInt32(json, "minimum-bits", out var minimum)
                    ? Parsed(new CheerSourceConfiguration(minimum))
                    : Invalid("minimum-bits", "Enter a whole-number minimum Bits amount."),
            static configuration =>
                configuration.MinimumBits is >= 1 and <= 1000000
                    ? AutomationValidationResult.Valid
                    : AutomationValidationResult.Invalid(
                        new AutomationValidationTarget.Field(new("minimum-bits")),
                        "Choose a minimum Bits amount from 1 to 1,000,000."
                    )
        );

    private static AutomationDefinition<IncomingRaidSourceConfiguration> IncomingRaidSource() =>
        new(
            new(
                AutomationDefinitionIds.IncomingRaidSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Incoming raid",
                    "Starts an automation when another channel raids this channel.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Raider", "The broadcaster who raided the channel."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [
                    new(
                        new("minimum-viewers"),
                        "Minimum viewers",
                        "The smallest raid that starts this automation.",
                        new AutomationConfigurationFieldType.Number(0, 10000000),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static json =>
                TryReadInt32(json, "minimum-viewers", out var minimum)
                    ? Parsed(new IncomingRaidSourceConfiguration(minimum))
                    : Invalid("minimum-viewers", "Enter a whole-number minimum viewer count."),
            static configuration =>
                configuration.MinimumViewerCount is >= 0 and <= 10000000
                    ? AutomationValidationResult.Valid
                    : AutomationValidationResult.Invalid(
                        new AutomationValidationTarget.Field(new("minimum-viewers")),
                        "Choose a minimum viewer count from 0 to 10,000,000."
                    )
        );

    private static AutomationDefinition<HypeTrainSourceConfiguration> HypeTrainSource(
        AutomationDefinitionId id,
        string name,
        string description
    ) =>
        new(
            new(
                id,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(name, description, "Twitch events"),
                [],
                [FlowPort(), ChannelPort(), EventTimePort()],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new HypeTrainSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<ChatNotificationSourceConfiguration> ChatNotificationSource() =>
        new(
            new(
                AutomationDefinitionIds.ChatNotificationSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Chat notification",
                    "Starts an automation from a typed Twitch chat notification such as an announcement or resub message. Ordinary chat messages never start automations.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort(
                        "Chatter",
                        "The viewer the notification is about, when not anonymous."
                    ),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [
                    new(
                        new("notice-type"),
                        "Notification type",
                        "The Twitch notification type that starts this automation.",
                        new AutomationConfigurationFieldType.Choice(
                            TwitchEventAutomationSources.ChatNotificationNoticeTypes
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static json =>
                TryReadString(json, "notice-type", out var noticeType)
                    ? Parsed(new ChatNotificationSourceConfiguration(noticeType))
                    : Invalid("notice-type", "Choose a notification type."),
            static configuration =>
                TwitchEventAutomationSources.ChatNotificationNoticeTypes.Contains(
                    configuration.NoticeType,
                    StringComparer.Ordinal
                )
                    ? AutomationValidationResult.Valid
                    : AutomationValidationResult.Invalid(
                        new AutomationValidationTarget.Field(new("notice-type")),
                        "Choose a supported notification type."
                    )
        );

    private static AutomationPortMetadata FlowInput() =>
        new(new("flow"), "Flow", "Runs this node.", AutomationPortValueType.Flow);

    private static AutomationPortMetadata CompleteOutput() =>
        new(
            new("complete"),
            "Complete",
            "Continues after this node.",
            AutomationPortValueType.Flow
        );

    private static AutomationDefinition<RewardRedemptionSourceConfiguration> RewardRedemptionSource() =>
        new(
            new(
                AutomationDefinitionIds.RewardRedemptionSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Channel Points redemption",
                    "Starts an automation when a viewer redeems a Custom Reward. Only BlokeBot can change rewards that it manages.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Viewer", "The viewer who redeemed the reward."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [
                    new(
                        new("reward-id"),
                        "Reward filter",
                        "Select a Custom Reward to filter the redemptions. Leave this field empty to accept all redemptions.",
                        new AutomationConfigurationFieldType.Reference(
                            AutomationReferenceKind.CustomReward
                        ),
                        false
                    ),
                    new(
                        new("completion-policy"),
                        "Completion policy",
                        "Choose how BlokeBot updates the redemption status. You can keep the status manual, fulfil it after success, or cancel it after failure. BlokeBot changes only the rewards that it manages.",
                        new AutomationConfigurationFieldType.Choice(
                            TwitchEventAutomationSources.RedemptionCompletionPolicies
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            ParseRewardRedemption,
            ValidateRewardRedemption
        );

    private static AutomationDefinition<FulfilRedemptionActionConfiguration> FulfilRedemptionAction() =>
        new(
            new(
                AutomationDefinitionIds.FulfilRedemptionAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Fulfil redemption",
                    "Marks the Channel Points redemption from the trigger as fulfilled. Use this action only for rewards that BlokeBot manages.",
                    "Channel Points"
                ),
                [FlowInput()],
                [CompleteOutput()],
                [],
                AutomationActionCapabilities.ChangesPoints,
                AutomationActionRetrySafety.Unsafe,
                RedemptionTriggerContext()
            ),
            static _ => Parsed(new FulfilRedemptionActionConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<CancelRedemptionActionConfiguration> CancelRedemptionAction() =>
        new(
            new(
                AutomationDefinitionIds.CancelRedemptionAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Cancel redemption",
                    "Cancels the Channel Points redemption from the trigger. Twitch refunds the points. Use this action only for rewards that BlokeBot manages.",
                    "Channel Points"
                ),
                [FlowInput()],
                [CompleteOutput()],
                [],
                AutomationActionCapabilities.ChangesPoints,
                AutomationActionRetrySafety.Unsafe,
                RedemptionTriggerContext()
            ),
            static _ => Parsed(new CancelRedemptionActionConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationTriggerContextRequirement RedemptionTriggerContext() =>
        new(
            [AutomationDefinitionIds.RewardRedemptionSource],
            "Add a Channel Points redemption trigger to use this action.",
            "Connect this action to a Channel Points redemption trigger."
        );

    private static AutomationConfigurationParseResult ParseRewardRedemption(JsonElement json)
    {
        if (
            !TryReadString(json, "completion-policy", out var policyToken)
            || TwitchEventAutomationSources.ParseCompletionPolicy(policyToken) is not { } policy
        )
        {
            return Invalid("completion-policy", "Choose the result for the redemption status.");
        }

        string? rewardId = null;
        if (
            json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("reward-id", out var rewardProperty)
            && rewardProperty.ValueKind != JsonValueKind.Null
        )
        {
            rewardId =
                rewardProperty.ValueKind == JsonValueKind.String
                    ? rewardProperty.GetString()
                    : null;
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                return Invalid("reward-id", "Choose a Custom Reward for the filter.");
            }
        }

        return Parsed(new RewardRedemptionSourceConfiguration(rewardId, policy));
    }

    private static AutomationValidationResult ValidateRewardRedemption(
        RewardRedemptionSourceConfiguration configuration
    ) =>
        configuration.RewardId switch
        {
            null => AutomationValidationResult.Valid,
            { Length: >= 1 and <= 128 } rewardId when !string.IsNullOrWhiteSpace(rewardId) =>
                AutomationValidationResult.Valid,
            _ => AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("reward-id")),
                "Choose a Custom Reward for the filter."
            ),
        };
}

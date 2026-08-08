using System.Collections.Immutable;
using System.Text.Json;

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

    private static bool TryReadString(JsonElement json, string propertyName, out string value)
    {
        value = string.Empty;
        if (
            json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadInt32(JsonElement json, string propertyName, out int value)
    {
        value = 0;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
    }

    private static AutomationConfigurationParseResult Parsed(
        AutomationConfiguration configuration
    ) => new AutomationConfigurationParseResult.Parsed(configuration);

    private static AutomationConfigurationParseResult Invalid(string fieldId, string message) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(new(fieldId)), message),
        ]);
}

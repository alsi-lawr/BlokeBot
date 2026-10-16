using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    private static ImmutableArray<AutomationPortMetadata> SourceFields(
        AutomationDefinitionDescriptor definition
    ) =>
        definition.Kind == AutomationNodeKind.Source
            ?
            [
                .. definition
                    .Outputs.Where(port => port.ValueType != AutomationPortValueType.Flow)
                    .Select(port =>
                        port.Id.Value == "actor"
                        && definition.Id.Value
                            is "subscription-gift"
                                or "cheer"
                                or "chat-notification"
                            ? port with
                            {
                                Nullability = AutomationPortNullability.Nullable,
                            }
                            : port
                    ),
                .. ContextFields(definition.Id),
            ]
            : [];

    private static ImmutableArray<AutomationPortMetadata> ContextFields(
        AutomationDefinitionId source
    ) =>
        source.Value switch
        {
            "stream-online" =>
            [
                ContextField("stream_type", "Stream type", AutomationPortValueType.Text),
            ],
            "follow" =>
            [
                ContextField("followed_at", "Followed at", AutomationPortValueType.Timestamp),
            ],
            "subscription" =>
            [
                ContextField("sub_tier", "Subscription tier", AutomationPortValueType.Text),
                ContextField("is_gift", "Gift subscription", AutomationPortValueType.Boolean),
            ],
            "subscription-gift" =>
            [
                ContextField("gift_count", "Gift count", AutomationPortValueType.Number),
                ContextField("sub_tier", "Subscription tier", AutomationPortValueType.Text),
                ContextField("is_anonymous", "Anonymous", AutomationPortValueType.Boolean),
            ],
            "cheer" =>
            [
                ContextField("bits", "Bits", AutomationPortValueType.Number),
                ContextField("is_anonymous", "Anonymous", AutomationPortValueType.Boolean),
                ContextField(
                    "cheer_message",
                    "Cheer message",
                    AutomationPortValueType.Text,
                    AutomationDataSensitivity.Sensitive
                ),
            ],
            "incoming-raid" or "shoutout-sent" or "shoutout-received" =>
            [
                ContextField("viewer_count", "Viewer count", AutomationPortValueType.Number),
            ],
            "hype-train-begin" or "hype-train-progress" or "hype-train-end" =>
            [
                ContextField(
                    "hype_train_level",
                    "Hype Train level",
                    AutomationPortValueType.Number
                ),
                ContextField(
                    "hype_train_total",
                    "Hype Train total",
                    AutomationPortValueType.Number
                ),
            ],
            "chat-notification" =>
            [
                ContextField("notice_type", "Notice type", AutomationPortValueType.Text),
                ContextField(
                    "system_message",
                    "System message",
                    AutomationPortValueType.Text,
                    AutomationDataSensitivity.Sensitive
                ),
                ContextField(
                    "message_text",
                    "Message text",
                    AutomationPortValueType.Text,
                    AutomationDataSensitivity.Sensitive
                ),
            ],
            "reward-redemption" =>
            [
                ContextField("redemption_id", "Redemption ID", AutomationPortValueType.Text),
                ContextField("reward_id", "Reward ID", AutomationPortValueType.Text),
                ContextField("reward_title", "Reward title", AutomationPortValueType.Text),
                ContextField("reward_cost", "Reward cost", AutomationPortValueType.Number),
                ContextField("status", "Status", AutomationPortValueType.Text),
                ContextField("redeemed_at", "Redeemed at", AutomationPortValueType.Timestamp),
                ContextField(
                    "user_input",
                    "User input",
                    AutomationPortValueType.Text,
                    AutomationDataSensitivity.Sensitive
                ),
            ],
            "poll-started" or "poll-progressed" or "poll-ended" =>
            [
                ContextField("poll_id", "Poll ID", AutomationPortValueType.Text),
                ContextField("poll_title", "Poll title", AutomationPortValueType.Text),
                ContextField("poll_status", "Poll status", AutomationPortValueType.Text),
                ContextField("total_votes", "Total votes", AutomationPortValueType.Number),
            ],
            "prediction-started"
            or "prediction-progressed"
            or "prediction-locked"
            or "prediction-ended" =>
            [
                ContextField("prediction_id", "Prediction ID", AutomationPortValueType.Text),
                ContextField("prediction_title", "Prediction title", AutomationPortValueType.Text),
                ContextField(
                    "prediction_status",
                    "Prediction status",
                    AutomationPortValueType.Text
                ),
                ContextField(
                    "winning_outcome_id",
                    "Winning outcome ID",
                    AutomationPortValueType.Text,
                    nullable: true
                ),
                ContextField(
                    "winning_outcome_title",
                    "Winning outcome title",
                    AutomationPortValueType.Text,
                    nullable: true
                ),
            ],
            _ => [],
        };

    private static AutomationPortMetadata ContextField(
        string id,
        string name,
        AutomationPortValueType type,
        AutomationDataSensitivity sensitivity = AutomationDataSensitivity.Safe,
        bool nullable = false
    ) =>
        new(
            new(id),
            name,
            type == AutomationPortValueType.Text
                ? $"Optional event input, at most {TwitchEventAutomationContext.MaximumTextLength} characters."
                : "Optional event input.",
            type,
            sensitivity,
            nullable ? AutomationPortNullability.Nullable : AutomationPortNullability.NonNullable
        );
}

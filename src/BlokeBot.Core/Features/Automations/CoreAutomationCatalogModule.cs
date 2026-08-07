using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public static class AutomationDefinitionIds
{
    public static AutomationDefinitionId CustomCommandSource { get; } = new("custom-command");

    public static AutomationDefinitionId SendChatAction { get; } = new("send-chat");

    public static AutomationDefinitionId PlayOverlayCueAction { get; } = new("play-overlay-cue");

    public static AutomationDefinitionId ConditionControl { get; } = new("condition");

    public static AutomationDefinitionId DelayControl { get; } = new("delay");

    public static AutomationDefinitionId StreamOnlineSource { get; } = new("stream-online");

    public static AutomationDefinitionId StreamOfflineSource { get; } = new("stream-offline");

    public static AutomationDefinitionId FollowSource { get; } = new("follow");

    public static AutomationDefinitionId SubscriptionSource { get; } = new("subscription");

    public static AutomationDefinitionId SubscriptionGiftSource { get; } = new("subscription-gift");

    public static AutomationDefinitionId CheerSource { get; } = new("cheer");

    public static AutomationDefinitionId IncomingRaidSource { get; } = new("incoming-raid");

    public static AutomationDefinitionId HypeTrainBeginSource { get; } = new("hype-train-begin");

    public static AutomationDefinitionId HypeTrainProgressSource { get; } =
        new("hype-train-progress");

    public static AutomationDefinitionId HypeTrainEndSource { get; } = new("hype-train-end");

    public static AutomationDefinitionId ChatNotificationSource { get; } = new("chat-notification");

    public static AutomationDefinitionId RewardRedemptionSource { get; } = new("reward-redemption");

    public static AutomationDefinitionId FulfilRedemptionAction { get; } = new("fulfil-redemption");

    public static AutomationDefinitionId CancelRedemptionAction { get; } = new("cancel-redemption");
}

internal sealed class CoreAutomationCatalogModule : IAutomationCatalogModule
{
    private static readonly AutomationSchemaCompatibility _schema = new(new(1), new(1));
    private static readonly AutomationPortMetadata _flowInput = new(
        new("flow"),
        "Flow",
        "Runs this node.",
        AutomationPortValueType.Flow
    );
    private static readonly AutomationPortMetadata _completeOutput = new(
        new("complete"),
        "Complete",
        "Continues after this node.",
        AutomationPortValueType.Flow
    );

    public AutomationModuleId Id { get; } = new("blokebot.core");

    public IEnumerable<IAutomationDefinition> Definitions { get; } =
    [
        CustomCommandSource(),
        SendChatAction(),
        PlayOverlayCueAction(),
        ConditionControl(),
        DelayControl(),
    ];

    private static AutomationDefinition<CustomCommandSourceConfiguration> CustomCommandSource() =>
        new(
            new(
                AutomationDefinitionIds.CustomCommandSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Custom command",
                    "Starts an automation when a selected custom command is used.",
                    "Chat"
                ),
                [],
                [
                    new(
                        new("flow"),
                        "Flow",
                        "Starts the connected automation.",
                        AutomationPortValueType.Flow
                    ),
                    new(
                        new("actor"),
                        "Viewer",
                        "The viewer who used the command.",
                        AutomationPortValueType.Actor,
                        AutomationDataSensitivity.Sensitive
                    ),
                    new(
                        new("arguments"),
                        "Arguments",
                        "The words entered after the command.",
                        AutomationPortValueType.Arguments,
                        AutomationDataSensitivity.Sensitive
                    ),
                    new(
                        new("channel"),
                        "Channel",
                        "The channel that received the command.",
                        AutomationPortValueType.Channel
                    ),
                    new(
                        new("event-time"),
                        "Event time",
                        "When the command was received.",
                        AutomationPortValueType.Timestamp
                    ),
                    new(
                        new("stream"),
                        "Stream",
                        "The active stream identity, when the channel is live.",
                        AutomationPortValueType.Stream
                    ),
                ],
                [
                    new(
                        new("custom-command-id"),
                        "Custom command",
                        "The command that starts this automation.",
                        new AutomationConfigurationFieldType.Reference(
                            AutomationReferenceKind.CustomCommand
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            ParseCustomCommand,
            ValidateCustomCommand
        );

    private static AutomationDefinition<SendChatActionConfiguration> SendChatAction() =>
        new(
            new(
                AutomationDefinitionIds.SendChatAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Send chat message", "Sends a message in the host channel.", "Chat"),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("message"),
                        "Message",
                        "The chat message, including any automation variables.",
                        new AutomationConfigurationFieldType.Text(500, true),
                        true
                    ),
                ],
                AutomationActionCapabilities.SendsChat,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseSendChat,
            ValidateSendChat
        );

    private static AutomationDefinition<PlayOverlayCueActionConfiguration> PlayOverlayCueAction() =>
        new(
            new(
                AutomationDefinitionIds.PlayOverlayCueAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Play overlay cue",
                    "Plays a saved cue through the host's Cue player Browser Source.",
                    "Overlays"
                ),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("target-id"),
                        "Cue player",
                        "The Cue player Browser Source that receives the cue.",
                        new AutomationConfigurationFieldType.Reference(
                            AutomationReferenceKind.OverlayTarget
                        ),
                        true
                    ),
                    new(
                        new("cue-id"),
                        "Cue",
                        "The saved overlay cue to play.",
                        new AutomationConfigurationFieldType.Reference(
                            AutomationReferenceKind.OverlayCue
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.PlaysOverlays,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseOverlayCue,
            ValidateOverlayCue
        );

    private static AutomationDefinition<ConditionControlConfiguration> ConditionControl() =>
        new(
            new(
                AutomationDefinitionIds.ConditionControl,
                AutomationNodeKind.Control,
                AutomationDefinitionScope.Host,
                _schema,
                new("Condition", "Evaluates an expression and chooses a branch.", "Control"),
                [_flowInput],
                [
                    new(
                        new("true"),
                        "Matches",
                        "Continues when the expression is true.",
                        AutomationPortValueType.Flow
                    ),
                    new(
                        new("false"),
                        "Does not match",
                        "Continues when the expression is false.",
                        AutomationPortValueType.Flow
                    ),
                ],
                [
                    new(
                        new("expression"),
                        "Expression",
                        "The expression that decides which branch continues.",
                        new AutomationConfigurationFieldType.Text(null, true),
                        true,
                        AutomationDataSensitivity.Sensitive
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            ParseCondition,
            ValidateCondition
        );

    private static AutomationDefinition<DelayControlConfiguration> DelayControl() =>
        new(
            new(
                AutomationDefinitionIds.DelayControl,
                AutomationNodeKind.Control,
                AutomationDefinitionScope.Host,
                _schema,
                new("Delay", "Waits before continuing the automation.", "Control"),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("duration-milliseconds"),
                        "Duration",
                        "How long the automation waits.",
                        new AutomationConfigurationFieldType.Duration(
                            TimeSpan.FromMilliseconds(1),
                            null
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            ParseDelay,
            ValidateDelay
        );

    private static AutomationConfigurationParseResult ParseCustomCommand(JsonElement json) =>
        TryReadInt32(json, "custom-command-id", out var commandId)
            ? Parsed(new CustomCommandSourceConfiguration(new(commandId)))
            : Invalid("custom-command-id", "Choose a valid custom command.");

    private static AutomationConfigurationParseResult ParseSendChat(JsonElement json) =>
        TryReadString(json, "message", out var message)
            ? Parsed(new SendChatActionConfiguration(message))
            : Invalid("message", "Enter a chat message.");

    private static AutomationConfigurationParseResult ParseOverlayCue(JsonElement json) =>
        TryReadString(json, "target-id", out var targetId)
        && Guid.TryParse(targetId, out var parsedTarget)
        && TryReadString(json, "cue-id", out var cueId)
        && Guid.TryParse(cueId, out var parsedCue)
            ? Parsed(new PlayOverlayCueActionConfiguration(new(parsedTarget), new(parsedCue)))
            : Invalid("target-id", "Choose a Cue player and a saved cue.");

    private static AutomationConfigurationParseResult ParseCondition(JsonElement json) =>
        TryReadString(json, "expression", out var expression)
            ? Parsed(new ConditionControlConfiguration(expression))
            : Invalid("expression", "Enter a condition expression.");

    private static AutomationConfigurationParseResult ParseDelay(JsonElement json) =>
        TryReadInt64(json, "duration-milliseconds", out var milliseconds)
        && milliseconds >= TimeSpan.MinValue.Ticks / TimeSpan.TicksPerMillisecond
        && milliseconds <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond
            ? Parsed(
                new DelayControlConfiguration(
                    TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond)
                )
            )
            : Invalid("duration-milliseconds", "Enter a whole-number delay in milliseconds.");

    private static AutomationValidationResult ValidateCustomCommand(
        CustomCommandSourceConfiguration configuration
    ) =>
        configuration.CommandId.Value > 0
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("custom-command-id")),
                "Choose a valid custom command."
            );

    private static AutomationValidationResult ValidateSendChat(
        SendChatActionConfiguration configuration
    ) =>
        configuration.Message.Trim() switch
        {
            [] => AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("message")),
                "Enter a chat message."
            ),
            { Length: > 500 } => AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("message")),
                "Chat messages cannot exceed 500 characters."
            ),
            _ => AutomationValidationResult.Valid,
        };

    private static AutomationValidationResult ValidateOverlayCue(
        PlayOverlayCueActionConfiguration configuration
    ) =>
        configuration.TargetId.Value != Guid.Empty && configuration.CueId.Value != Guid.Empty
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("target-id")),
                "Choose a Cue player and a saved cue."
            );

    private static AutomationValidationResult ValidateCondition(
        ConditionControlConfiguration configuration
    ) =>
        configuration.Expression.Trim() switch
        {
            [] => AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("expression")),
                "Enter a condition expression."
            ),
            _ => AutomationValidationResult.Valid,
        };

    private static AutomationValidationResult ValidateDelay(
        DelayControlConfiguration configuration
    ) =>
        configuration.Duration > TimeSpan.Zero
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("duration-milliseconds")),
                "Choose a delay longer than zero."
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

    private static bool TryReadInt64(JsonElement json, string propertyName, out long value)
    {
        value = 0;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var property)
            && property.TryGetInt64(out value);
    }

    private static AutomationConfigurationParseResult Parsed(
        AutomationConfiguration configuration
    ) => new AutomationConfigurationParseResult.Parsed(configuration);

    private static AutomationConfigurationParseResult Invalid(string fieldId, string message) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(new(fieldId)), message),
        ]);
}

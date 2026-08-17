using System.Text.Json;
using static BlokeBot.Core.Features.Automations.AutomationConfigurationJson;

namespace BlokeBot.Core.Features.Automations;

public static class AutomationDefinitionIds
{
    public static AutomationDefinitionId CustomCommandSource { get; } = new("custom-command");

    public static AutomationDefinitionId SendChatAction { get; } = new("send-chat");

    public static AutomationDefinitionId PlayOverlayCueAction { get; } = new("play-overlay-cue");

    internal static AutomationDefinitionId RandomNumber { get; } = new("random-number");

    internal static AutomationDefinitionId CelTransform { get; } = new("cel-transform");

    public static AutomationDefinitionId ConditionControl { get; } = new("condition");

    public static AutomationDefinitionId DelayControl { get; } = new("delay");

    internal static AutomationDefinitionId MergeBranchesControl { get; } = new("merge-branches");

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

    public static AutomationDefinitionId ShoutoutSentSource { get; } = new("shoutout-sent");

    public static AutomationDefinitionId ShoutoutReceivedSource { get; } = new("shoutout-received");

    public static AutomationDefinitionId PollStartedSource { get; } = new("poll-started");

    public static AutomationDefinitionId PollProgressedSource { get; } = new("poll-progressed");

    public static AutomationDefinitionId PollEndedSource { get; } = new("poll-ended");

    public static AutomationDefinitionId PredictionStartedSource { get; } =
        new("prediction-started");

    public static AutomationDefinitionId PredictionProgressedSource { get; } =
        new("prediction-progressed");

    public static AutomationDefinitionId PredictionLockedSource { get; } = new("prediction-locked");

    public static AutomationDefinitionId PredictionEndedSource { get; } = new("prediction-ended");

    public static AutomationDefinitionId SendShoutoutAction { get; } = new("send-shoutout");

    public static AutomationDefinitionId StartPollAction { get; } = new("start-poll");

    public static AutomationDefinitionId EndPollAction { get; } = new("end-poll");

    public static AutomationDefinitionId CreateClipAction { get; } = new("create-clip");

    public static AutomationDefinitionId CreateMarkerAction { get; } = new("create-marker");

    public static AutomationDefinitionId StartPredictionAction { get; } = new("start-prediction");

    public static AutomationDefinitionId LockPredictionAction { get; } = new("lock-prediction");

    public static AutomationDefinitionId CancelPredictionAction { get; } = new("cancel-prediction");

    public static AutomationDefinitionId ResolvePredictionAction { get; } =
        new("resolve-prediction");
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
        RandomNumber(),
        AutomationCelTransform.Definition(
            AutomationDefinitionIds.CelTransform,
            new("CEL Transform", "Calculates new values from data in this flow.", "Data")
        ),
        SendChatAction(),
        PlayOverlayCueAction(),
        ConditionControl(),
        DelayControl(),
        MergeBranchesControl(),
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
                    "Starts this flow when a selected custom command is used.",
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
                        AutomationPortValueType.Actor
                    ),
                    new(
                        new("arguments"),
                        "Arguments",
                        "The words entered after the command.",
                        AutomationPortValueType.Arguments
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
                        AutomationPortValueType.Timestamp,
                        AutomationDataSensitivity.Sensitive
                    ),
                    new(
                        new("stream"),
                        "Stream",
                        "The active stream identity, when the channel is live.",
                        AutomationPortValueType.Stream,
                        AutomationDataSensitivity.Sensitive,
                        Nullability: AutomationPortNullability.Nullable
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
                new("Send chat message", "Sends a message to the channel chat.", "Chat"),
                [
                    _flowInput,
                    new(
                        new("message"),
                        "Message",
                        "Receives the exact Text message to send.",
                        AutomationPortValueType.Text,
                        BindingFieldId: new("message")
                    ),
                ],
                [_completeOutput],
                [
                    new(
                        new("message"),
                        "Message",
                        "The chat message can contain automation variables.",
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

    private static AutomationDefinition<AutomationRandomNumberConfiguration> RandomNumber() =>
        new(
            new(
                AutomationDefinitionIds.RandomNumber,
                AutomationNodeKind.Value,
                AutomationDefinitionScope.Host,
                _schema,
                new("Random Number", "Picks a random whole number.", "Data"),
                [],
                [
                    new(
                        new("number"),
                        "Number",
                        "Supplies the generated whole number.",
                        AutomationPortValueType.Number
                    ),
                ],
                [
                    new(
                        new("minimum"),
                        "Minimum",
                        "The smallest number that can be generated, inclusive.",
                        new AutomationConfigurationFieldType.Number(long.MinValue, long.MaxValue),
                        true
                    ),
                    new(
                        new("maximum"),
                        "Maximum",
                        "The largest number that can be generated, inclusive.",
                        new AutomationConfigurationFieldType.Number(long.MinValue, long.MaxValue),
                        true
                    ),
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            ParseRandomNumber,
            ValidateRandomNumber
        );

    private static AutomationDefinition<PlayOverlayCueActionConfiguration> PlayOverlayCueAction() =>
        new(
            new(
                AutomationDefinitionIds.PlayOverlayCueAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Play overlay cue", "Shows a selected cue on the stream overlay.", "Overlays"),
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
                new("Condition", "Chooses a path from a yes or no value.", "Control"),
                [
                    _flowInput,
                    new(
                        new("predicate"),
                        "Predicate",
                        "Receives the exact Boolean value that chooses the branch.",
                        AutomationPortValueType.Boolean,
                        BindingFieldId: new("predicate")
                    ),
                ],
                [
                    new(
                        new("yes"),
                        "Yes",
                        "Continues when the predicate is true.",
                        AutomationPortValueType.Flow
                    ),
                    new(
                        new("no"),
                        "No",
                        "Continues when the predicate is false.",
                        AutomationPortValueType.Flow
                    ),
                ],
                [
                    new(
                        new("predicate"),
                        "Predicate",
                        "The retained Fixed Boolean value.",
                        new AutomationConfigurationFieldType.Data(AutomationPortValueType.Boolean),
                        true
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
                new("Delay", "Pauses this flow for a set time.", "Control"),
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

    private static AutomationDefinition<MergeBranchesControlConfiguration> MergeBranchesControl() =>
        new(
            new(
                AutomationDefinitionIds.MergeBranchesControl,
                AutomationNodeKind.Control,
                AutomationDefinitionScope.Host,
                _schema,
                new("Merge branches", "Continues after a connected path runs.", "Control"),
                [_flowInput],
                [_completeOutput],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new MergeBranchesControlConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationConfigurationParseResult ParseCustomCommand(JsonElement json) =>
        TryReadInt32(json, "custom-command-id", out var commandId)
            ? Parsed(new CustomCommandSourceConfiguration(new(commandId)))
            : Invalid("custom-command-id", "Choose a valid custom command.");

    private static AutomationConfigurationParseResult ParseSendChat(JsonElement json) =>
        TryReadString(json, "message", out var message)
            ? Parsed(new SendChatActionConfiguration(message))
            : Invalid("message", "Enter a chat message.");

    private static AutomationConfigurationParseResult ParseRandomNumber(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return Invalid("minimum", "Enter an exact whole-number minimum and maximum.");
        }

        var minimum = 0L;
        if (
            json.TryGetProperty("minimum", out var minimumJson)
            && !TryReadRandomBound(minimumJson, "minimum", out minimum, out var minimumError)
        )
        {
            return minimumError!;
        }

        var maximum = 100L;
        return
            json.TryGetProperty("maximum", out var maximumJson)
            && !TryReadRandomBound(maximumJson, "maximum", out maximum, out var maximumError)
            ? maximumError!
            : Parsed(new AutomationRandomNumberConfiguration(minimum, maximum));
    }

    private static AutomationConfigurationParseResult ParseOverlayCue(JsonElement json) =>
        TryReadString(json, "target-id", out var targetId)
        && Guid.TryParse(targetId, out var parsedTarget)
        && TryReadString(json, "cue-id", out var cueId)
        && Guid.TryParse(cueId, out var parsedCue)
            ? Parsed(new PlayOverlayCueActionConfiguration(new(parsedTarget), new(parsedCue)))
            : Invalid("target-id", "Choose a Cue player and a saved cue.");

    private static AutomationConfigurationParseResult ParseCondition(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object
        && json.TryGetProperty("predicate", out var predicate)
        && predicate.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? Parsed(new ConditionControlConfiguration(predicate.GetBoolean()))
            : Invalid("predicate", "Choose a Boolean predicate.");

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
                "Use 500 characters or fewer in the chat message."
            ),
            _ => AutomationValidationResult.Valid,
        };

    private static AutomationValidationResult ValidateRandomNumber(
        AutomationRandomNumberConfiguration configuration
    ) =>
        configuration.Minimum <= configuration.Maximum
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("maximum")),
                "Maximum must be greater than or equal to minimum."
            );

    private static AutomationValidationResult ValidateOverlayCue(
        PlayOverlayCueActionConfiguration configuration
    ) =>
        configuration.TargetId.Value != Guid.Empty && configuration.CueId.Value != Guid.Empty
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("target-id")),
                "Choose a Cue player and a saved cue."
            );

    private static AutomationValidationResult ValidateCondition(ConditionControlConfiguration _) =>
        AutomationValidationResult.Valid;

    private static bool TryReadRandomBound(
        JsonElement json,
        string fieldId,
        out long value,
        out AutomationConfigurationParseResult.Invalid? error
    )
    {
        value = 0;
        error = null;
        if (json.ValueKind != JsonValueKind.Number || !json.TryGetDecimal(out var number))
        {
            error = (AutomationConfigurationParseResult.Invalid)Invalid(
                fieldId,
                "Enter a whole number from -9223372036854775808 through 9223372036854775807."
            );
            return false;
        }

        if (decimal.Truncate(number) != number)
        {
            error = (AutomationConfigurationParseResult.Invalid)Invalid(
                fieldId,
                "Enter an exact whole number without a fractional part."
            );
            return false;
        }

        if (number is < long.MinValue or > long.MaxValue)
        {
            error = (AutomationConfigurationParseResult.Invalid)Invalid(
                fieldId,
                "Enter a whole number from -9223372036854775808 through 9223372036854775807."
            );
            return false;
        }

        value = decimal.ToInt64(number);
        return true;
    }

    private static AutomationValidationResult ValidateDelay(
        DelayControlConfiguration configuration
    ) =>
        configuration.Duration > TimeSpan.Zero
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("duration-milliseconds")),
                "Choose a delay longer than zero."
            );
}

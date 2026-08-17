using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Persistence.Models;
using static BlokeBot.Core.Features.Automations.AutomationConfigurationJson;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Maps each native Twitch operation definition to the single backing Native Twitch feature gate
/// it enforces in addition to Automations, and declares the text input bounds Twitch publishes
/// for those operations.
/// </summary>
public static class NativeOperationAutomations
{
    // Helix Create Poll published limits.
    internal const int PollTitleMaximumLength = 60;
    internal const int PollChoiceMinimumCount = 2;
    internal const int PollChoiceMaximumCount = 5;
    internal const int PollChoiceMaximumLength = 25;
    internal const int PollDurationMinimumSeconds = 15;
    internal const int PollDurationMaximumSeconds = 1800;
    internal const int PollChannelPointsPerVoteMinimum = 1;
    internal const int PollChannelPointsPerVoteMaximum = 1_000_000;

    // Helix Create Prediction published limits.
    internal const int PredictionTitleMaximumLength = 45;
    internal const int PredictionOutcomeMinimumCount = 2;
    internal const int PredictionOutcomeMaximumCount = 10;
    internal const int PredictionOutcomeMaximumLength = 25;
    internal const int PredictionWindowMinimumSeconds = 30;
    internal const int PredictionWindowMaximumSeconds = 1800;

    // Helix Create Stream Marker published limit.
    internal const int MarkerDescriptionMaximumLength = 140;

    // Twitch publishes no length limit for outcome identifiers; 128 mirrors the existing Custom
    // Reward reference bound.
    internal const int OutcomeIdentifierMaximumLength = 128;

    public static ImmutableArray<string> ClipDelayModes { get; } = ["immediate", "stream-delay"];

    private static readonly ImmutableDictionary<string, HostFeatureFlags> _backingFeatures =
        new Dictionary<string, HostFeatureFlags>(StringComparer.Ordinal)
        {
            ["shoutout-sent"] = HostFeatureFlags.RaidCollaboration,
            ["shoutout-received"] = HostFeatureFlags.RaidCollaboration,
            ["send-shoutout"] = HostFeatureFlags.RaidCollaboration,
            ["poll-started"] = HostFeatureFlags.Polls,
            ["poll-progressed"] = HostFeatureFlags.Polls,
            ["poll-ended"] = HostFeatureFlags.Polls,
            ["start-poll"] = HostFeatureFlags.Polls,
            ["end-poll"] = HostFeatureFlags.Polls,
            ["create-clip"] = HostFeatureFlags.ClipsAndMarkers,
            ["create-marker"] = HostFeatureFlags.ClipsAndMarkers,
            ["prediction-started"] = HostFeatureFlags.Predictions,
            ["prediction-progressed"] = HostFeatureFlags.Predictions,
            ["prediction-locked"] = HostFeatureFlags.Predictions,
            ["prediction-ended"] = HostFeatureFlags.Predictions,
            ["competition-lifecycle"] = HostFeatureFlags.Competitions,
            ["start-prediction"] = HostFeatureFlags.Predictions,
            ["lock-prediction"] = HostFeatureFlags.Predictions,
            ["cancel-prediction"] = HostFeatureFlags.Predictions,
            ["resolve-prediction"] = HostFeatureFlags.Predictions,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    internal static HostFeatureFlags BackingFeature(string definitionId) =>
        _backingFeatures.GetValueOrDefault(definitionId, HostFeatureFlags.None);

    internal static ImmutableArray<string> SplitEntries(string multiline) =>
        [
            .. multiline
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static entry => entry.Trim())
                .Where(static entry => entry.Length > 0),
        ];
}

internal sealed class NativeOperationAutomationCatalogModule : IAutomationCatalogModule
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

    public AutomationModuleId Id { get; } = new("blokebot.native-operations");

    public IEnumerable<IAutomationDefinition> Definitions { get; } =
    [
        ShoutoutSentSource(),
        ShoutoutReceivedSource(),
        PollSource(
            AutomationDefinitionIds.PollStartedSource,
            "Poll started",
            "Starts this flow when a poll begins.",
            static () => new PollStartedSourceConfiguration()
        ),
        PollSource(
            AutomationDefinitionIds.PollProgressedSource,
            "Poll progressed",
            "Starts this flow when viewers vote in a poll.",
            static () => new PollProgressedSourceConfiguration()
        ),
        PollSource(
            AutomationDefinitionIds.PollEndedSource,
            "Poll ended",
            "Starts this flow when a poll ends.",
            static () => new PollEndedSourceConfiguration()
        ),
        PredictionSource(
            AutomationDefinitionIds.PredictionStartedSource,
            "Prediction started",
            "Starts this flow when a prediction begins.",
            static () => new PredictionStartedSourceConfiguration()
        ),
        PredictionSource(
            AutomationDefinitionIds.PredictionProgressedSource,
            "Prediction progressed",
            "Starts this flow when viewers enter a prediction.",
            static () => new PredictionProgressedSourceConfiguration()
        ),
        PredictionSource(
            AutomationDefinitionIds.PredictionLockedSource,
            "Prediction locked",
            "Starts this flow when a prediction locks.",
            static () => new PredictionLockedSourceConfiguration()
        ),
        PredictionSource(
            AutomationDefinitionIds.PredictionEndedSource,
            "Prediction ended",
            "Starts this flow when a prediction ends.",
            static () => new PredictionEndedSourceConfiguration()
        ),
        SendShoutoutAction(),
        StartPollAction(),
        EndPollAction(),
        CreateClipAction(),
        CreateMarkerAction(),
        StartPredictionAction(),
        LockPredictionAction(),
        CancelPredictionAction(),
        ResolvePredictionAction(),
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
            AutomationPortValueType.Timestamp,
            AutomationDataSensitivity.Sensitive
        );

    private static AutomationPortMetadata ActorPort(string name, string description) =>
        new(new("actor"), name, description, AutomationPortValueType.Actor);

    private static AutomationDefinition<ShoutoutSentSourceConfiguration> ShoutoutSentSource() =>
        new(
            new(
                AutomationDefinitionIds.ShoutoutSentSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Shoutout sent",
                    "Starts this flow when this channel sends a shoutout.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Shouted-out channel", "The broadcaster who received the shoutout."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new ShoutoutSentSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<ShoutoutReceivedSourceConfiguration> ShoutoutReceivedSource() =>
        new(
            new(
                AutomationDefinitionIds.ShoutoutReceivedSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Shoutout received",
                    "Starts this flow when another channel sends a shoutout.",
                    "Twitch events"
                ),
                [],
                [
                    FlowPort(),
                    ActorPort("Source channel", "The broadcaster who sent the shoutout."),
                    ChannelPort(),
                    EventTimePort(),
                ],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => Parsed(new ShoutoutReceivedSourceConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<TConfiguration> PollSource<TConfiguration>(
        AutomationDefinitionId id,
        string name,
        string description,
        Func<TConfiguration> create
    )
        where TConfiguration : AutomationConfiguration =>
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
            _ => Parsed(create()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<TConfiguration> PredictionSource<TConfiguration>(
        AutomationDefinitionId id,
        string name,
        string description,
        Func<TConfiguration> create
    )
        where TConfiguration : AutomationConfiguration =>
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
            _ => Parsed(create()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<SendShoutoutActionConfiguration> SendShoutoutAction() =>
        new(
            new(
                AutomationDefinitionIds.SendShoutoutAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Send shoutout", "Sends a shoutout for a selected channel.", "Shoutouts"),
                [_flowInput],
                [_completeOutput],
                [],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe,
                KnownActorTriggerContext()
            ),
            static _ => Parsed(new SendShoutoutActionConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationTriggerContextRequirement KnownActorTriggerContext() =>
        new(
            [
                AutomationDefinitionIds.CustomCommandSource,
                AutomationDefinitionIds.FollowSource,
                AutomationDefinitionIds.SubscriptionSource,
                AutomationDefinitionIds.IncomingRaidSource,
                AutomationDefinitionIds.RewardRedemptionSource,
                AutomationDefinitionIds.ShoutoutSentSource,
                AutomationDefinitionIds.ShoutoutReceivedSource,
            ],
            "Add a trigger with a known viewer or broadcaster to use this action.",
            "Connect this action to a trigger with a known viewer or broadcaster."
        );

    private static AutomationDefinition<StartPollActionConfiguration> StartPollAction() =>
        new(
            new(
                AutomationDefinitionIds.StartPollAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Start poll", "Starts a poll in the channel.", "Polls"),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("title"),
                        "Question",
                        "The poll question can contain automation variables.",
                        new AutomationConfigurationFieldType.Text(
                            NativeOperationAutomations.PollTitleMaximumLength
                        ),
                        true
                    ),
                    new(
                        new("choices"),
                        "Choices",
                        "One poll choice per line. Twitch polls take 2–5 choices of at most 25 characters each.",
                        new AutomationConfigurationFieldType.Text(null, Multiline: true),
                        true
                    ),
                    new(
                        new("duration-seconds"),
                        "Duration",
                        "How long the poll runs. Twitch polls last 15 seconds to 30 minutes.",
                        new AutomationConfigurationFieldType.Duration(
                            TimeSpan.FromSeconds(
                                NativeOperationAutomations.PollDurationMinimumSeconds
                            ),
                            TimeSpan.FromSeconds(
                                NativeOperationAutomations.PollDurationMaximumSeconds
                            )
                        ),
                        true
                    ),
                    new(
                        new("channel-points-per-vote"),
                        "Channel Points per extra vote",
                        "Viewers can buy more votes at this Channel Points cost. Leave this field empty to turn off the option.",
                        new AutomationConfigurationFieldType.Number(
                            NativeOperationAutomations.PollChannelPointsPerVoteMinimum,
                            NativeOperationAutomations.PollChannelPointsPerVoteMaximum
                        ),
                        false
                    ),
                ],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseStartPoll,
            ValidateStartPoll
        );

    private static AutomationDefinition<EndPollActionConfiguration> EndPollAction() =>
        new(
            new(
                AutomationDefinitionIds.EndPollAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("End poll", "Ends the active poll in the channel.", "Polls"),
                [_flowInput],
                [_completeOutput],
                [],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            static _ => Parsed(new EndPollActionConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<CreateClipActionConfiguration> CreateClipAction() =>
        new(
            new(
                AutomationDefinitionIds.CreateClipAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Create clip", "Creates a clip of the live stream.", "Clips & markers"),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("delay-mode"),
                        "Capture time",
                        "Choose Immediate to capture now. Choose Stream delay to include the Twitch broadcast delay.",
                        new AutomationConfigurationFieldType.Choice(
                            NativeOperationAutomations.ClipDelayModes
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseCreateClip,
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<CreateMarkerActionConfiguration> CreateMarkerAction() =>
        new(
            new(
                AutomationDefinitionIds.CreateMarkerAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Create stream marker", "Adds a marker to the live stream.", "Clips & markers"),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("description"),
                        "Description",
                        "What this marker is about.",
                        new AutomationConfigurationFieldType.Text(
                            NativeOperationAutomations.MarkerDescriptionMaximumLength
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseCreateMarker,
            ValidateCreateMarker
        );

    private static AutomationDefinition<StartPredictionActionConfiguration> StartPredictionAction() =>
        new(
            new(
                AutomationDefinitionIds.StartPredictionAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new("Start prediction", "Starts a prediction in the channel.", "Predictions"),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("title"),
                        "Question",
                        "The prediction question can contain automation variables.",
                        new AutomationConfigurationFieldType.Text(
                            NativeOperationAutomations.PredictionTitleMaximumLength
                        ),
                        true
                    ),
                    new(
                        new("outcomes"),
                        "Outcomes",
                        "One outcome per line. Twitch predictions take 2–10 outcomes of at most 25 characters each.",
                        new AutomationConfigurationFieldType.Text(null, Multiline: true),
                        true
                    ),
                    new(
                        new("window-seconds"),
                        "Prediction window",
                        "How long viewers can participate. Twitch prediction windows last 30 seconds to 30 minutes.",
                        new AutomationConfigurationFieldType.Duration(
                            TimeSpan.FromSeconds(
                                NativeOperationAutomations.PredictionWindowMinimumSeconds
                            ),
                            TimeSpan.FromSeconds(
                                NativeOperationAutomations.PredictionWindowMaximumSeconds
                            )
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseStartPrediction,
            ValidateStartPrediction
        );

    private static AutomationDefinition<LockPredictionActionConfiguration> LockPredictionAction() =>
        new(
            new(
                AutomationDefinitionIds.LockPredictionAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Lock prediction",
                    "Stops new entries to the active prediction.",
                    "Predictions"
                ),
                [_flowInput],
                [_completeOutput],
                [],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            static _ => Parsed(new LockPredictionActionConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<CancelPredictionActionConfiguration> CancelPredictionAction() =>
        new(
            new(
                AutomationDefinitionIds.CancelPredictionAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Cancel prediction",
                    "Cancels the active prediction and refunds all points.",
                    "Predictions"
                ),
                [_flowInput],
                [_completeOutput],
                [],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            static _ => Parsed(new CancelPredictionActionConfiguration()),
            static _ => AutomationValidationResult.Valid
        );

    private static AutomationDefinition<ResolvePredictionActionConfiguration> ResolvePredictionAction() =>
        new(
            new(
                AutomationDefinitionIds.ResolvePredictionAction,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Resolve prediction",
                    "Selects the winning outcome for the active prediction.",
                    "Predictions"
                ),
                [_flowInput],
                [_completeOutput],
                [
                    new(
                        new("winning-outcome-id"),
                        "Outcome",
                        "The Twitch ID of the selected outcome can contain automation variables.",
                        new AutomationConfigurationFieldType.Text(
                            NativeOperationAutomations.OutcomeIdentifierMaximumLength
                        ),
                        true
                    ),
                ],
                AutomationActionCapabilities.CallsTwitchApi,
                AutomationActionRetrySafety.Unsafe
            ),
            ParseResolvePrediction,
            ValidateResolvePrediction
        );

    private static AutomationConfigurationParseResult ParseStartPoll(JsonElement json)
    {
        if (!TryReadString(json, "title", out var title))
        {
            return Invalid("title", "Enter a poll question.");
        }

        if (!TryReadString(json, "choices", out var choices))
        {
            return Invalid("choices", "Enter the poll choices, one per line.");
        }

        if (!TryReadInt32(json, "duration-seconds", out var durationSeconds))
        {
            return Invalid("duration-seconds", "Enter a whole-number poll duration in seconds.");
        }

        int? channelPointsPerVote = null;
        if (
            json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("channel-points-per-vote", out var perVote)
            && perVote.ValueKind != JsonValueKind.Null
        )
        {
            if (!perVote.TryGetInt32(out var cost))
            {
                return Invalid(
                    "channel-points-per-vote",
                    "Enter a whole-number Channel Points cost per extra vote."
                );
            }

            channelPointsPerVote = cost;
        }

        return Parsed(
            new StartPollActionConfiguration(title, choices, durationSeconds, channelPointsPerVote)
        );
    }

    private static AutomationValidationResult ValidateStartPoll(
        StartPollActionConfiguration configuration
    )
    {
        var choices = NativeOperationAutomations.SplitEntries(configuration.Choices);
        return configuration.Title.Trim().Length
                is < 1
                    or > NativeOperationAutomations.PollTitleMaximumLength
                ? AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("title")),
                    "Use 1–60 characters in the poll question."
                )
            : choices.Length
                is < NativeOperationAutomations.PollChoiceMinimumCount
                    or > NativeOperationAutomations.PollChoiceMaximumCount
            || choices.Any(static choice =>
                choice.Length > NativeOperationAutomations.PollChoiceMaximumLength
            )
                ? AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("choices")),
                    "Polls need 2–5 choices, each no longer than 25 characters."
                )
            : configuration.DurationSeconds
                is < NativeOperationAutomations.PollDurationMinimumSeconds
                    or > NativeOperationAutomations.PollDurationMaximumSeconds
                ? AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("duration-seconds")),
                    "Choose a poll duration from 15 to 1800 seconds."
                )
            : configuration.ChannelPointsPerVote
                is null
                    or (
                        >= NativeOperationAutomations.PollChannelPointsPerVoteMinimum
                        and <= NativeOperationAutomations.PollChannelPointsPerVoteMaximum
                    )
                ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("channel-points-per-vote")),
                "Enter a cost from 1 to 1,000,000 Channel Points for each extra vote."
            );
    }

    private static AutomationConfigurationParseResult ParseCreateClip(JsonElement json) =>
        TryReadString(json, "delay-mode", out var delayMode)
        && NativeOperationAutomations.ClipDelayModes.Contains(delayMode, StringComparer.Ordinal)
            ? Parsed(new CreateClipActionConfiguration(delayMode == "stream-delay"))
            : Invalid("delay-mode", "Choose when the clip is captured.");

    private static AutomationConfigurationParseResult ParseCreateMarker(JsonElement json) =>
        TryReadString(json, "description", out var description)
            ? Parsed(new CreateMarkerActionConfiguration(description))
            : Invalid("description", "Enter a marker description.");

    private static AutomationValidationResult ValidateCreateMarker(
        CreateMarkerActionConfiguration configuration
    ) =>
        configuration.Description.Trim() switch
        {
            [] => AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("description")),
                "Enter a marker description."
            ),
            { Length: > NativeOperationAutomations.MarkerDescriptionMaximumLength } =>
                AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("description")),
                    "Use 140 characters or fewer in the marker description."
                ),
            _ => AutomationValidationResult.Valid,
        };

    private static AutomationConfigurationParseResult ParseStartPrediction(JsonElement json) =>
        !TryReadString(json, "title", out var title)
            ? Invalid("title", "Enter a prediction question.")
        : !TryReadString(json, "outcomes", out var outcomes)
            ? Invalid("outcomes", "Enter the prediction outcomes, one per line.")
        : TryReadInt32(json, "window-seconds", out var windowSeconds)
            ? Parsed(new StartPredictionActionConfiguration(title, outcomes, windowSeconds))
        : Invalid("window-seconds", "Enter a whole-number prediction window in seconds.");

    private static AutomationValidationResult ValidateStartPrediction(
        StartPredictionActionConfiguration configuration
    )
    {
        var outcomes = NativeOperationAutomations.SplitEntries(configuration.Outcomes);
        return configuration.Title.Trim().Length
                is < 1
                    or > NativeOperationAutomations.PredictionTitleMaximumLength
                ? AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("title")),
                    "Use 1–45 characters in the prediction question."
                )
            : outcomes.Length
                is < NativeOperationAutomations.PredictionOutcomeMinimumCount
                    or > NativeOperationAutomations.PredictionOutcomeMaximumCount
            || outcomes.Any(static outcome =>
                outcome.Length > NativeOperationAutomations.PredictionOutcomeMaximumLength
            )
                ? AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("outcomes")),
                    "Predictions need 2–10 outcomes, each no longer than 25 characters."
                )
            : configuration.WindowSeconds
                is >= NativeOperationAutomations.PredictionWindowMinimumSeconds
                    and <= NativeOperationAutomations.PredictionWindowMaximumSeconds
                ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("window-seconds")),
                "Choose a prediction window from 30 to 1800 seconds."
            );
    }

    private static AutomationConfigurationParseResult ParseResolvePrediction(JsonElement json) =>
        TryReadString(json, "winning-outcome-id", out var winningOutcomeId)
            ? Parsed(new ResolvePredictionActionConfiguration(winningOutcomeId))
            : Invalid("winning-outcome-id", "Enter the outcome ID.");

    private static AutomationValidationResult ValidateResolvePrediction(
        ResolvePredictionActionConfiguration configuration
    ) =>
        configuration.WinningOutcomeId.Trim() switch
        {
            [] => AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Field(new("winning-outcome-id")),
                "Enter the outcome ID."
            ),
            { Length: > NativeOperationAutomations.OutcomeIdentifierMaximumLength } =>
                AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new("winning-outcome-id")),
                    "Use 128 characters or fewer in the outcome ID."
                ),
            _ => AutomationValidationResult.Valid,
        };
}

using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public readonly record struct AutomationModuleId(string Value);

public readonly record struct AutomationDefinitionId(string Value);

public readonly record struct AutomationPortId(string Value);

public readonly record struct AutomationConfigurationFieldId(string Value);

public readonly record struct AutomationSchemaVersion(int Value);

public readonly record struct AutomationHostId(int Value);

public readonly record struct AutomationVariableName(string Value);

internal readonly record struct AutomationCelIdentifier(string Value);

public readonly record struct AutomationSafeTriggerFieldId(string Value);

public readonly record struct AutomationCustomCommandId(int Value);

public readonly record struct AutomationOverlayCueId(Guid Value);

public readonly record struct AutomationOverlayTargetId(Guid Value);

public enum AutomationNodeKind
{
    Source,
    Value,
    Transform,
    Action,
    Control,
}

public enum AutomationDefinitionScope
{
    Host,
}

[Flags]
public enum AutomationActionCapabilities
{
    None = 0,
    SendsChat = 1 << 0,
    PlaysOverlays = 1 << 1,
    ChangesPoints = 1 << 2,
    CallsTwitchApi = 1 << 3,
    RunsScripts = 1 << 4,
}

public enum AutomationPortValueType
{
    Flow,
    Text,
    Number,
    Boolean,
    Timestamp,
    Actor,
    Channel,
    Stream,
    Arguments,
}

public enum AutomationDataSensitivity
{
    Safe,
    Sensitive,
}

public enum AutomationPortNullability
{
    NonNullable,
    Nullable,
}

public enum AutomationActionRetrySafety
{
    NotApplicable,
    Unsafe,
    Safe,
}

public enum AutomationReferenceKind
{
    CustomCommand,
    OverlayCue,
    OverlayTarget,
    CustomReward,
}

public enum AutomationSchemaCompatibilityStatus
{
    Current,
    UpgradeRequired,
    OlderThanSupported,
    NewerThanSupported,
}

public sealed record AutomationDisplayMetadata(string Name, string Description, string Category);

public sealed record AutomationTriggerContextRequirement(
    ImmutableArray<AutomationDefinitionId> CompatibleSources,
    string UnavailableReason,
    string ValidationMessage
);

public sealed record AutomationPortMetadata(
    AutomationPortId Id,
    string Name,
    string Description,
    AutomationPortValueType ValueType,
    AutomationDataSensitivity Sensitivity = AutomationDataSensitivity.Safe,
    AutomationPortNullability Nullability = AutomationPortNullability.NonNullable,
    AutomationConfigurationFieldId? BindingFieldId = null
);

public abstract record AutomationConfigurationFieldType
{
    private AutomationConfigurationFieldType() { }

    public sealed record Text(int? MaximumLength, bool Multiline = false)
        : AutomationConfigurationFieldType;

    public sealed record Duration(TimeSpan Minimum, TimeSpan? Maximum)
        : AutomationConfigurationFieldType;

    public sealed record Number(long Minimum, long? Maximum) : AutomationConfigurationFieldType;

    public sealed record Data(AutomationPortValueType ValueType) : AutomationConfigurationFieldType;

    public sealed record Reference(AutomationReferenceKind ReferenceKind)
        : AutomationConfigurationFieldType;

    public sealed record Choice(ImmutableArray<string> Values) : AutomationConfigurationFieldType;
}

public sealed record AutomationConfigurationFieldMetadata(
    AutomationConfigurationFieldId Id,
    string Name,
    string Description,
    AutomationConfigurationFieldType FieldType,
    bool Required,
    AutomationDataSensitivity Sensitivity = AutomationDataSensitivity.Safe
);

public sealed record AutomationSchemaCompatibility(
    AutomationSchemaVersion Current,
    AutomationSchemaVersion OldestReadable
)
{
    public AutomationSchemaCompatibilityStatus Classify(AutomationSchemaVersion persisted) =>
        persisted.Value switch
        {
            var value when value > Current.Value =>
                AutomationSchemaCompatibilityStatus.NewerThanSupported,
            var value when value < OldestReadable.Value =>
                AutomationSchemaCompatibilityStatus.OlderThanSupported,
            var value when value < Current.Value =>
                AutomationSchemaCompatibilityStatus.UpgradeRequired,
            _ => AutomationSchemaCompatibilityStatus.Current,
        };
}

public sealed record AutomationDefinitionDescriptor(
    AutomationDefinitionId Id,
    AutomationNodeKind Kind,
    AutomationDefinitionScope Scope,
    AutomationSchemaCompatibility Schema,
    AutomationDisplayMetadata Display,
    ImmutableArray<AutomationPortMetadata> Inputs,
    ImmutableArray<AutomationPortMetadata> Outputs,
    ImmutableArray<AutomationConfigurationFieldMetadata> Configuration,
    AutomationActionCapabilities Capabilities,
    AutomationActionRetrySafety RetrySafety,
    AutomationTriggerContextRequirement? TriggerContextRequirement = null
);

public abstract record AutomationValidationTarget
{
    private AutomationValidationTarget() { }

    public sealed record Definition : AutomationValidationTarget;

    public sealed record Field(AutomationConfigurationFieldId Id) : AutomationValidationTarget;

    public sealed record Port(AutomationPortId Id) : AutomationValidationTarget;
}

public sealed record AutomationValidationError(AutomationValidationTarget Target, string Message);

public sealed record AutomationValidationResult(ImmutableArray<AutomationValidationError> Errors)
{
    public bool IsValid => Errors.IsEmpty;

    public static AutomationValidationResult Valid { get; } = new([]);

    public static AutomationValidationResult Invalid(
        AutomationValidationTarget target,
        string message
    ) => new([new(target, message)]);
}

public abstract record AutomationConfiguration;

public sealed record CustomCommandSourceConfiguration(AutomationCustomCommandId CommandId)
    : AutomationConfiguration;

public sealed record StreamOnlineSourceConfiguration : AutomationConfiguration;

public sealed record StreamOfflineSourceConfiguration : AutomationConfiguration;

public sealed record FollowSourceConfiguration : AutomationConfiguration;

public sealed record SubscriptionSourceConfiguration : AutomationConfiguration;

public sealed record SubscriptionGiftSourceConfiguration(int MinimumGiftCount)
    : AutomationConfiguration;

public sealed record CheerSourceConfiguration(int MinimumBits) : AutomationConfiguration;

public sealed record IncomingRaidSourceConfiguration(int MinimumViewerCount)
    : AutomationConfiguration;

public sealed record HypeTrainSourceConfiguration : AutomationConfiguration;

public sealed record ChatNotificationSourceConfiguration(string NoticeType)
    : AutomationConfiguration;

public enum RedemptionCompletionPolicy
{
    Manual,
    FulfilOnSuccess,
    CancelOnFailure,
}

public sealed record RewardRedemptionSourceConfiguration(
    string? RewardId,
    RedemptionCompletionPolicy CompletionPolicy
) : AutomationConfiguration;

public sealed record FulfilRedemptionActionConfiguration : AutomationConfiguration;

public sealed record CancelRedemptionActionConfiguration : AutomationConfiguration;

public sealed record ShoutoutSentSourceConfiguration : AutomationConfiguration;

public sealed record ShoutoutReceivedSourceConfiguration : AutomationConfiguration;

public sealed record PollStartedSourceConfiguration : AutomationConfiguration;

public sealed record PollProgressedSourceConfiguration : AutomationConfiguration;

public sealed record PollEndedSourceConfiguration : AutomationConfiguration;

public sealed record PredictionStartedSourceConfiguration : AutomationConfiguration;

public sealed record PredictionProgressedSourceConfiguration : AutomationConfiguration;

public sealed record PredictionLockedSourceConfiguration : AutomationConfiguration;

public sealed record PredictionEndedSourceConfiguration : AutomationConfiguration;

public sealed record SendShoutoutActionConfiguration : AutomationConfiguration;

public sealed record StartPollActionConfiguration(
    string Title,
    string Choices,
    int DurationSeconds,
    int? ChannelPointsPerVote
) : AutomationConfiguration;

public sealed record EndPollActionConfiguration : AutomationConfiguration;

public sealed record CreateClipActionConfiguration(bool HasDelay) : AutomationConfiguration;

public sealed record CreateMarkerActionConfiguration(string Description) : AutomationConfiguration;

public sealed record StartPredictionActionConfiguration(
    string Title,
    string Outcomes,
    int WindowSeconds
) : AutomationConfiguration;

public sealed record LockPredictionActionConfiguration : AutomationConfiguration;

public sealed record CancelPredictionActionConfiguration : AutomationConfiguration;

public sealed record ResolvePredictionActionConfiguration(string WinningOutcomeId)
    : AutomationConfiguration;

public sealed record SendChatActionConfiguration(string Message) : AutomationConfiguration;

public sealed record PlayOverlayCueActionConfiguration(
    AutomationOverlayTargetId TargetId,
    AutomationOverlayCueId CueId
) : AutomationConfiguration;

public sealed record ConditionControlConfiguration(string Expression) : AutomationConfiguration;

public sealed record DelayControlConfiguration(TimeSpan Duration) : AutomationConfiguration;

public sealed record PersistedAutomationNodeDefinition(
    string TypeId,
    int SchemaVersion,
    JsonElement Configuration
);

public abstract record AutomationConfigurationParseResult
{
    private AutomationConfigurationParseResult() { }

    public sealed record Parsed(AutomationConfiguration Configuration)
        : AutomationConfigurationParseResult;

    public sealed record Invalid(ImmutableArray<AutomationValidationError> Errors)
        : AutomationConfigurationParseResult;
}

public sealed record AutomationEventIdentity(
    Guid OccurrenceId,
    AutomationDefinitionId SourceDefinitionId
);

public sealed record AutomationActor(string TwitchUserId, string Login, string DisplayName);

public sealed record AutomationChannel(
    AutomationHostId HostId,
    string TwitchChannelId,
    string Login,
    string DisplayName
);

public sealed record AutomationStream(
    string TwitchStreamId,
    string? Title,
    string? GameName,
    DateTimeOffset? StartedAtUtc
);

public sealed record AutomationTimestamps(
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc
);

public sealed record AutomationArgument(int Position, string Value);

public sealed record AutomationPublicActor(string Login, string DisplayName);

public sealed record AutomationPublicChannel(string Login, string DisplayName);

public sealed record AutomationPublicStream(
    string? Title,
    string? GameName,
    DateTimeOffset? StartedAtUtc
);

public sealed record AutomationValueArgument(
    int Position,
    string Value,
    ImmutableArray<AutomationValueProvenance> Provenance
);

public abstract record AutomationValue
{
    private AutomationValue() { }

    public sealed record Text(string Value) : AutomationValue;

    public sealed record Number(decimal Value) : AutomationValue;

    public sealed record Boolean(bool Value) : AutomationValue;

    public sealed record Timestamp(DateTimeOffset Value) : AutomationValue;

    public sealed record Actor(AutomationPublicActor Value) : AutomationValue;

    public sealed record Channel(AutomationPublicChannel Value) : AutomationValue;

    public sealed record Stream(AutomationPublicStream Value) : AutomationValue;

    public sealed record Arguments(ImmutableArray<AutomationValueArgument> Values)
        : AutomationValue;

    public sealed record Null(AutomationPortValueType ValueType) : AutomationValue;
}

public sealed record AutomationVariable(
    AutomationValue Value,
    AutomationDataSensitivity Sensitivity
);

public sealed class AutomationVariableSet
{
    private readonly ImmutableDictionary<AutomationVariableName, AutomationVariable> _values;

    public AutomationVariableSet(
        IEnumerable<KeyValuePair<AutomationVariableName, AutomationVariable>> values
    ) => _values = values.ToImmutableDictionary();

    public IReadOnlyDictionary<AutomationVariableName, AutomationValue> SafeForExternalUse() =>
        _values
            .Where(static pair => pair.Value.Sensitivity == AutomationDataSensitivity.Safe)
            .ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.Value);

    internal IReadOnlyDictionary<AutomationVariableName, AutomationVariable> ForExecution() =>
        _values;
}

public sealed record AutomationContext(
    AutomationEventIdentity Event,
    AutomationActor? Actor,
    AutomationChannel Channel,
    AutomationStream? Stream,
    AutomationTimestamps Timestamps,
    ImmutableArray<AutomationArgument> Arguments,
    AutomationVariableSet Variables
)
{
    public AutomationHostId HostId => Channel.HostId;
}

public enum AutomationCatalogAvailability
{
    Enabled,
    Disabled,
    HostNotFound,
}

public sealed record AutomationCatalogSnapshot(
    AutomationCatalogAvailability Availability,
    ImmutableArray<AutomationDefinitionDescriptor> Definitions
);

public abstract record AutomationConfigurationCheck
{
    private AutomationConfigurationCheck() { }

    public sealed record Valid(
        AutomationDefinitionDescriptor Definition,
        AutomationConfiguration Configuration
    ) : AutomationConfigurationCheck;

    public sealed record Invalid(ImmutableArray<AutomationValidationError> Errors)
        : AutomationConfigurationCheck;

    public sealed record FeatureDisabled : AutomationConfigurationCheck;

    public sealed record HostNotFound : AutomationConfigurationCheck;

    public sealed record HostMismatch(AutomationHostId Requested, AutomationHostId Context)
        : AutomationConfigurationCheck;

    public sealed record DefinitionMissing(AutomationDefinitionId Id)
        : AutomationConfigurationCheck;

    public sealed record SchemaUnsupported(
        AutomationDefinitionId Id,
        AutomationSchemaVersion Persisted,
        AutomationSchemaCompatibilityStatus Status
    ) : AutomationConfigurationCheck;
}

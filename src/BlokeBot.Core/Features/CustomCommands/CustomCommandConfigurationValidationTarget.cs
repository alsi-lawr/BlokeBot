namespace BlokeBot.Core.Features.CustomCommands;

public enum CustomCommandSettingsTab
{
    Commands,
    MessageLibrary,
}

public enum CustomCommandValidationEntityKind
{
    Configuration,
    Reply,
    Variant,
    Command,
    Counter,
    ScheduledMessage,
}

public enum CustomCommandValidationFieldKind
{
    Name,
    SelectionMode,
    VariantText,
    Aliases,
    Reply,
    Action,
    Counter,
    Cooldown,
    CooldownScope,
    Delivery,
    Color,
    RetryDelay,
    OccurrenceLifetime,
    Schedule,
    Interval,
    ChatMessages,
    Day,
    TimeZone,
    Identity,
}

public sealed record CustomCommandConfigurationValidationTarget(
    CustomCommandSettingsTab Tab,
    CustomCommandValidationEntityKind EntityKind,
    int EntityId,
    CustomCommandValidationFieldKind FieldKind,
    int VariantId = 0
);

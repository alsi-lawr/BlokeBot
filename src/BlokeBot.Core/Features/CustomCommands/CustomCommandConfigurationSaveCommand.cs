using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed record CustomCommandTimeZone
{
    internal CustomCommandTimeZone(string id) => Id = id;

    public string Id { get; }
}

public sealed record CustomMessageVariantValue(int Id, string Text);

public sealed record CustomMessageLibraryEntryValue
{
    internal CustomMessageLibraryEntryValue(
        int id,
        string name,
        CustomMessageSelectionMode selectionMode,
        int currentVariantIndex,
        IEnumerable<CustomMessageVariantValue> variants
    )
    {
        Id = id;
        Name = name;
        SelectionMode = selectionMode;
        CurrentVariantIndex = currentVariantIndex;
        Variants = Array.AsReadOnly(variants.ToArray());
    }

    public int Id { get; }

    public string Name { get; }

    public CustomMessageSelectionMode SelectionMode { get; }

    public int CurrentVariantIndex { get; }

    public IReadOnlyList<CustomMessageVariantValue> Variants { get; }
}

public sealed record CustomCommandReplyRoutes(
    int? ZeroArgumentMessageLibraryEntryId,
    int? OneArgumentMessageLibraryEntryId,
    int? TwoArgumentMessageLibraryEntryId
);

public sealed record CustomCommandAllowedUserValue(
    string TwitchUserId,
    string Login,
    string DisplayName
);

public abstract record CustomCommandActionValue
{
    private CustomCommandActionValue() { }

    public abstract CustomCommandReplyRoutes ReplyRoutes { get; }

    public TResult Match<TResult>(
        Func<Message, TResult> message,
        Func<Counter, TResult> counter,
        Func<OverlayCue, TResult> overlayCue,
        Func<Automation, TResult> automation
    ) =>
        this switch
        {
            Message value => message(value),
            Counter value => counter(value),
            OverlayCue value => overlayCue(value),
            Automation value => automation(value),
            _ => throw new UnreachableException("Unknown custom command action value."),
        };

    public sealed record Message(CustomCommandReplyRoutes Routes) : CustomCommandActionValue
    {
        public override CustomCommandReplyRoutes ReplyRoutes => Routes;
    }

    public sealed record Counter(CustomCommandReplyRoutes Routes, int CounterId)
        : CustomCommandActionValue
    {
        public override CustomCommandReplyRoutes ReplyRoutes => Routes;
    }

    public sealed record OverlayCue(
        CustomCommandReplyRoutes Routes,
        Guid TargetOverlayPublicId,
        Guid CuePublicId,
        OverlayCueQueuePolicy QueuePolicy,
        OverlayCueReplyOrder ReplyOrder
    ) : CustomCommandActionValue
    {
        public override CustomCommandReplyRoutes ReplyRoutes => Routes;
    }

    public sealed record Automation(CustomCommandReplyRoutes Routes) : CustomCommandActionValue
    {
        public override CustomCommandReplyRoutes ReplyRoutes => Routes;
    }
}

public sealed record CustomCommandValue
{
    internal CustomCommandValue(
        int id,
        string name,
        IEnumerable<string> aliases,
        bool enabled,
        bool allowEveryone,
        bool allowModerators,
        IEnumerable<CustomCommandAllowedUserValue> allowedUsers,
        int cooldownSeconds,
        CustomCommandCooldownScope cooldownScope,
        CustomCommandInvocationLimit invocationLimit,
        CustomCommandActionValue action
    )
    {
        Id = id;
        Name = name;
        Aliases = Array.AsReadOnly(aliases.ToArray());
        Enabled = enabled;
        AllowEveryone = allowEveryone;
        AllowModerators = allowModerators;
        AllowedUsers = Array.AsReadOnly(allowedUsers.ToArray());
        CooldownSeconds = cooldownSeconds;
        CooldownScope = cooldownScope;
        InvocationLimit = invocationLimit;
        Action = action;
    }

    public int Id { get; }

    public string Name { get; }

    public IReadOnlyList<string> Aliases { get; }

    public bool Enabled { get; }

    public bool AllowEveryone { get; }

    public bool AllowModerators { get; }

    public IReadOnlyList<CustomCommandAllowedUserValue> AllowedUsers { get; }

    public int CooldownSeconds { get; }

    public CustomCommandCooldownScope CooldownScope { get; }

    public CustomCommandInvocationLimit InvocationLimit { get; }

    public CustomCommandActionValue Action { get; }
}

public sealed record CustomCounterValue(int Id, string Name, long Value);

public abstract record CustomAnnouncementScheduleValue
{
    private CustomAnnouncementScheduleValue() { }

    public TResult Match<TResult>(
        Func<Interval, TResult> interval,
        Func<IntervalAfterChat, TResult> intervalAfterChat,
        Func<Weekly, TResult> weekly
    ) =>
        this switch
        {
            Interval value => interval(value),
            IntervalAfterChat value => intervalAfterChat(value),
            Weekly value => weekly(value),
            _ => throw new UnreachableException("Unknown custom announcement schedule value."),
        };

    public sealed record Interval(int IntervalMinutes) : CustomAnnouncementScheduleValue;

    public sealed record IntervalAfterChat(int IntervalMinutes, int RequiredChatMessages)
        : CustomAnnouncementScheduleValue;

    public sealed record Weekly(DayOfWeek Day, TimeOnly Time) : CustomAnnouncementScheduleValue;
}

public sealed record CustomAnnouncementValue(
    int Id,
    string Name,
    bool Enabled,
    int MessageLibraryEntryId,
    CustomAnnouncementDeliveryType DeliveryType,
    BlokeBot.Persistence.Models.TwitchAnnouncementColor AnnouncementColor,
    AnnouncementRetryDelay RetryDelay,
    AnnouncementOccurrenceLifetime OccurrenceLifetime,
    CustomAnnouncementScheduleValue Schedule
);

public sealed record CustomCommandConfigurationSaveCommand
{
    internal CustomCommandConfigurationSaveCommand(
        CustomCommandTimeZone timeZone,
        IEnumerable<CustomMessageLibraryEntryValue> messageEntries,
        IEnumerable<CustomCommandValue> commands,
        IEnumerable<CustomCounterValue> counters,
        IEnumerable<CustomAnnouncementValue> announcements
    )
    {
        TimeZone = timeZone;
        MessageEntries = Array.AsReadOnly(messageEntries.ToArray());
        Commands = Array.AsReadOnly(commands.ToArray());
        Counters = Array.AsReadOnly(counters.ToArray());
        Announcements = Array.AsReadOnly(announcements.ToArray());
    }

    public CustomCommandTimeZone TimeZone { get; }

    public IReadOnlyList<CustomMessageLibraryEntryValue> MessageEntries { get; }

    public IReadOnlyList<CustomCommandValue> Commands { get; }

    public IReadOnlyList<CustomCounterValue> Counters { get; }

    public IReadOnlyList<CustomAnnouncementValue> Announcements { get; }
}

public readonly record struct CustomCommandConfigurationSaved;

public abstract record CustomCommandConfigurationSaveFailure
{
    private CustomCommandConfigurationSaveFailure() { }

    public abstract string Message { get; }

    public TResult Match<TResult>(
        Func<CustomAliasCollision, TResult> customAliasCollision,
        Func<StaleEntity, TResult> staleEntity,
        Func<OverlayCueReference, TResult> overlayCueReference
    ) =>
        this switch
        {
            CustomAliasCollision value => customAliasCollision(value),
            StaleEntity value => staleEntity(value),
            OverlayCueReference value => overlayCueReference(value),
            _ => throw new UnreachableException("Unknown custom command save failure."),
        };

    public sealed record CustomAliasCollision(string Alias) : CustomCommandConfigurationSaveFailure
    {
        public override string Message => $"!{Alias} is already used by another custom command.";
    }

    public sealed record StaleEntity(string EntityName) : CustomCommandConfigurationSaveFailure
    {
        public override string Message =>
            $"A {EntityName} you edited is no longer available. Reload the page and try again.";
    }

    public sealed record OverlayCueReference(
        int CommandId,
        CustomCommandValidationFieldKind Field,
        string Detail
    ) : CustomCommandConfigurationSaveFailure
    {
        public override string Message => Detail;
    }
}

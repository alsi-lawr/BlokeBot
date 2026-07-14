using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

public sealed record CustomCommandTimeZone
{
    internal CustomCommandTimeZone(string id)
    {
        Id = id;
    }

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

public abstract record CustomCommandActionValue
{
    private CustomCommandActionValue() { }

    public abstract int MessageLibraryEntryId { get; }

    public TResult Match<TResult>(Func<Message, TResult> message, Func<Counter, TResult> counter)
    {
        return this switch
        {
            Message value => message(value),
            Counter value => counter(value),
            _ => throw new UnreachableException("Unknown custom command action value."),
        };
    }

    public sealed record Message(int MessageEntryId) : CustomCommandActionValue
    {
        public override int MessageLibraryEntryId => MessageEntryId;
    }

    public sealed record Counter(int MessageEntryId, int CounterId) : CustomCommandActionValue
    {
        public override int MessageLibraryEntryId => MessageEntryId;
    }
}

public sealed record CustomCommandValue
{
    internal CustomCommandValue(
        int id,
        string name,
        IEnumerable<string> aliases,
        bool enabled,
        bool moderatorOnly,
        int cooldownSeconds,
        CustomCommandCooldownScope cooldownScope,
        CustomCommandActionValue action
    )
    {
        Id = id;
        Name = name;
        Aliases = Array.AsReadOnly(aliases.ToArray());
        Enabled = enabled;
        ModeratorOnly = moderatorOnly;
        CooldownSeconds = cooldownSeconds;
        CooldownScope = cooldownScope;
        Action = action;
    }

    public int Id { get; }

    public string Name { get; }

    public IReadOnlyList<string> Aliases { get; }

    public bool Enabled { get; }

    public bool ModeratorOnly { get; }

    public int CooldownSeconds { get; }

    public CustomCommandCooldownScope CooldownScope { get; }

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
    )
    {
        return this switch
        {
            Interval value => interval(value),
            IntervalAfterChat value => intervalAfterChat(value),
            Weekly value => weekly(value),
            _ => throw new UnreachableException("Unknown custom announcement schedule value."),
        };
    }

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
        Func<BuiltInAliasCollision, TResult> builtInAliasCollision,
        Func<CustomAliasCollision, TResult> customAliasCollision,
        Func<StaleEntity, TResult> staleEntity
    )
    {
        return this switch
        {
            BuiltInAliasCollision value => builtInAliasCollision(value),
            CustomAliasCollision value => customAliasCollision(value),
            StaleEntity value => staleEntity(value),
            _ => throw new UnreachableException("Unknown custom command save failure."),
        };
    }

    public sealed record BuiltInAliasCollision(string Alias) : CustomCommandConfigurationSaveFailure
    {
        public override string Message => $"!{Alias} is already used by another bot command.";
    }

    public sealed record CustomAliasCollision(string Alias) : CustomCommandConfigurationSaveFailure
    {
        public override string Message => $"!{Alias} is already used by another custom command.";
    }

    public sealed record StaleEntity(string EntityName) : CustomCommandConfigurationSaveFailure
    {
        public override string Message =>
            $"A {EntityName} you edited is no longer available. Reload the page and try again.";
    }
}

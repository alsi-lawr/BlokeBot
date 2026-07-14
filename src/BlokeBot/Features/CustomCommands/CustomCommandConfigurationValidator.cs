using BlokeBot.Announcements;
using BlokeBot.Commands;
using BlokeBot.Functional;

namespace BlokeBot.Features.CustomCommands;

public static class CustomCommandConfigurationValidator
{
    private const int _aliasMaxLength = 64;
    private const int _messageVariantMaxLength = 500;
    private const int _nameMaxLength = 128;

    public static Validation<
        CustomCommandConfigurationSaveCommand,
        CustomCommandConfigurationValidationError
    > Validate(CustomCommandConfiguration draft)
    {
        var errors = new List<CustomCommandConfigurationValidationError>();
        EnsureUniqueEditorIds(draft.MessageEntries.Select(entry => entry.Id), "replies", errors);
        EnsureUniqueEditorIds(draft.Counters.Select(counter => counter.Id), "counters", errors);
        EnsureUniqueEditorIds(draft.Commands.Select(command => command.Id), "commands", errors);
        EnsureUniqueEditorIds(
            draft.Announcements.Select(announcement => announcement.Id),
            "announcements",
            errors
        );

        var messageNames = draft
            .MessageEntries.Select(entry => RequiredName(entry.Name, "Reply", errors))
            .ToArray();
        var counterNames = draft
            .Counters.Select(counter => RequiredName(counter.Name, "Counter", errors))
            .ToArray();
        var commandNames = draft
            .Commands.Select(command => RequiredName(command.Name, "Command", errors))
            .ToArray();
        var announcementNames = draft
            .Announcements.Select(announcement =>
                RequiredName(announcement.Name, "Announcement", errors)
            )
            .ToArray();
        EnsureUniqueNames(messageNames, "reply", errors);
        EnsureUniqueNames(counterNames, "counter", errors);
        EnsureUniqueNames(commandNames, "command", errors);
        EnsureUniqueNames(announcementNames, "announcement", errors);

        var messageIds = draft.MessageEntries.Select(entry => entry.Id).ToHashSet();
        var counterIds = draft.Counters.Select(counter => counter.Id).ToHashSet();
        var messageEntries = SnapshotMessageEntries(draft.MessageEntries, messageNames, errors);
        var counters = SnapshotCounters(draft.Counters, counterNames);
        var commands = SnapshotCommands(
            draft.Commands,
            commandNames,
            messageIds,
            counterIds,
            errors
        );
        var announcements = SnapshotAnnouncements(
            draft.Announcements,
            announcementNames,
            messageIds,
            errors
        );
        EnsureUniqueAliases(commands, errors);
        var timeZone = NormalizeTimeZone(draft.TimeZoneId, errors);

        if (errors.Count > 0)
        {
            return Validation<
                CustomCommandConfigurationSaveCommand,
                CustomCommandConfigurationValidationError
            >.Invalid(errors[0], errors.Skip(1).ToArray());
        }

        return Validation<
            CustomCommandConfigurationSaveCommand,
            CustomCommandConfigurationValidationError
        >.Valid(
            new CustomCommandConfigurationSaveCommand(
                timeZone,
                messageEntries,
                commands,
                counters,
                announcements
            )
        );
    }

    private static IReadOnlyList<CustomMessageLibraryEntryValue> SnapshotMessageEntries(
        IReadOnlyList<CustomMessageLibraryEntryEditor> editors,
        IReadOnlyList<string> names,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = new List<CustomMessageLibraryEntryValue>(editors.Count);
        for (var entryIndex = 0; entryIndex < editors.Count; entryIndex++)
        {
            var editor = editors[entryIndex];
            if (!Enum.IsDefined(editor.SelectionMode))
            {
                AddError(errors, $"Choose how reply '{names[entryIndex]}' selects messages.");
            }

            if (editor.Variants.Count == 0)
            {
                AddError(errors, $"Reply '{names[entryIndex]}' needs at least one message.");
            }

            var variants = new List<CustomMessageVariantValue>(editor.Variants.Count);
            foreach (var variant in editor.Variants)
            {
                var text = variant.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    AddError(errors, $"Reply '{names[entryIndex]}' has a blank message.");
                }
                else if (text.Length > _messageVariantMaxLength)
                {
                    AddError(
                        errors,
                        $"Reply messages cannot exceed {_messageVariantMaxLength} characters."
                    );
                }

                variants.Add(new CustomMessageVariantValue(variant.Id, text));
            }

            values.Add(
                new CustomMessageLibraryEntryValue(
                    editor.Id,
                    names[entryIndex],
                    editor.SelectionMode,
                    ClampVariantIndex(editor.CurrentVariantIndex, variants.Count),
                    variants
                )
            );
        }

        return values;
    }

    private static IReadOnlyList<CustomCounterValue> SnapshotCounters(
        IReadOnlyList<CustomCounterEditor> editors,
        IReadOnlyList<string> names
    )
    {
        return editors
            .Select(
                (editor, index) => new CustomCounterValue(editor.Id, names[index], editor.Value)
            )
            .ToArray();
    }

    private static IReadOnlyList<CustomCommandValue> SnapshotCommands(
        IReadOnlyList<CustomCommandEditor> editors,
        IReadOnlyList<string> names,
        IReadOnlySet<int> messageIds,
        IReadOnlySet<int> counterIds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = new List<CustomCommandValue>(editors.Count);
        for (var index = 0; index < editors.Count; index++)
        {
            var editor = editors[index];
            if (!messageIds.Contains(editor.Action.MessageLibraryEntryId))
            {
                AddError(errors, $"Choose a saved reply for command '{names[index]}'.");
            }

            if (editor.CooldownSeconds < 0)
            {
                AddError(errors, "The wait between command uses cannot be negative.");
            }

            if (!Enum.IsDefined(editor.CooldownScope))
            {
                AddError(errors, $"Choose who waits for command '{names[index]}'.");
            }

            var action = editor.Action switch
            {
                MessageCustomCommandActionEditor message => new CustomCommandActionValue.Message(
                    message.MessageLibraryEntryId
                ),
                CounterCustomCommandActionEditor counter
                    when counterIds.Contains(counter.CounterId) =>
                    new CustomCommandActionValue.Counter(
                        counter.MessageLibraryEntryId,
                        counter.CounterId
                    ),
                CounterCustomCommandActionEditor counter => MissingCounterAction(
                    names[index],
                    counter,
                    errors
                ),
                _ => InvalidAction(names[index], editor.Action, errors),
            };
            var aliases = CommandAliasNormalizer.Split(editor.Aliases).ToArray();
            if (aliases.Length == 0)
            {
                AddError(errors, "Enter at least one command word.");
            }

            if (aliases.Any(alias => alias.Length > _aliasMaxLength))
            {
                AddError(errors, $"Command words cannot exceed {_aliasMaxLength} characters.");
            }

            values.Add(
                new CustomCommandValue(
                    editor.Id,
                    names[index],
                    aliases,
                    editor.Enabled,
                    editor.ModeratorOnly,
                    editor.CooldownSeconds,
                    editor.CooldownScope,
                    action
                )
            );
        }

        return values;
    }

    private static CustomCommandActionValue MissingCounterAction(
        string commandName,
        CounterCustomCommandActionEditor editor,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        AddError(errors, $"Choose a counter for command '{commandName}'.");
        return new CustomCommandActionValue.Counter(editor.MessageLibraryEntryId, editor.CounterId);
    }

    private static CustomCommandActionValue InvalidAction(
        string commandName,
        ICustomCommandActionEditor editor,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        AddError(errors, $"Choose what command '{commandName}' should do.");
        return new CustomCommandActionValue.Message(editor.MessageLibraryEntryId);
    }

    private static IReadOnlyList<CustomAnnouncementValue> SnapshotAnnouncements(
        IReadOnlyList<CustomAnnouncementEditor> editors,
        IReadOnlyList<string> names,
        IReadOnlySet<int> messageIds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = new List<CustomAnnouncementValue>(editors.Count);
        for (var index = 0; index < editors.Count; index++)
        {
            var editor = editors[index];
            if (!messageIds.Contains(editor.MessageLibraryEntryId))
            {
                AddError(errors, $"Choose a saved reply for announcement '{names[index]}'.");
            }

            var retryDelay = RetryDelay(editor.RetryDelaySeconds, errors);
            var occurrenceLifetime = OccurrenceLifetime(editor.OccurrenceLifetimeSeconds, errors);
            if (
                retryDelay is not null
                && occurrenceLifetime is not null
                && retryDelay.Value >= occurrenceLifetime.Value
            )
            {
                AddError(
                    errors,
                    "Announcement retry delay must be less than its occurrence lifetime."
                );
            }

            var schedule = SnapshotSchedule(names[index], editor.Schedule, errors);
            if (retryDelay is null || occurrenceLifetime is null)
            {
                continue;
            }

            values.Add(
                new CustomAnnouncementValue(
                    editor.Id,
                    names[index],
                    editor.Enabled,
                    editor.MessageLibraryEntryId,
                    retryDelay,
                    occurrenceLifetime,
                    schedule
                )
            );
        }

        return values;
    }

    private static AnnouncementRetryDelay? RetryDelay(
        int seconds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (seconds <= 0)
        {
            AddError(errors, "Announcement retry delay must be positive.");
            return null;
        }

        return new(TimeSpan.FromSeconds(seconds));
    }

    private static AnnouncementOccurrenceLifetime? OccurrenceLifetime(
        int seconds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (seconds <= 0 || TimeSpan.FromSeconds(seconds) > AnnouncementOccurrenceLifetime.Maximum)
        {
            AddError(
                errors,
                "Announcement occurrence lifetime must be positive and no greater than 60 seconds."
            );
            return null;
        }

        return new(TimeSpan.FromSeconds(seconds));
    }

    private static CustomAnnouncementScheduleValue SnapshotSchedule(
        string announcementName,
        ICustomAnnouncementScheduleEditor editor,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        switch (editor)
        {
            case IntervalCustomAnnouncementScheduleEditor interval:
                if (interval.IntervalMinutes < 1)
                {
                    AddError(errors, "Announcements must wait at least 1 minute.");
                }

                return new CustomAnnouncementScheduleValue.Interval(interval.IntervalMinutes);
            case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat:
                if (intervalAfterChat.IntervalMinutes < 1)
                {
                    AddError(errors, "Announcements must wait at least 1 minute.");
                }

                if (intervalAfterChat.RequiredChatMessages < 1)
                {
                    AddError(errors, "Chat-based announcements need at least 1 chat message.");
                }

                return new CustomAnnouncementScheduleValue.IntervalAfterChat(
                    intervalAfterChat.IntervalMinutes,
                    intervalAfterChat.RequiredChatMessages
                );
            case WeeklyCustomAnnouncementScheduleEditor weekly:
                if (!Enum.IsDefined(weekly.Day))
                {
                    AddError(errors, "Choose a valid day for weekly announcements.");
                }

                return new CustomAnnouncementScheduleValue.Weekly(weekly.Day, weekly.Time);
            default:
                AddError(errors, $"Choose when announcement '{announcementName}' should be sent.");
                return new CustomAnnouncementScheduleValue.Interval(1);
        }
    }

    private static void EnsureUniqueAliases(
        IReadOnlyList<CustomCommandValue> commands,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var duplicate = commands
            .SelectMany(
                (command, index) =>
                    command.Aliases.Select(alias => new { Alias = alias, CommandIndex = index })
            )
            .GroupBy(value => value.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group =>
                group.Select(value => value.CommandIndex).Distinct().Count() > 1
            )
            ?.Key;
        if (duplicate is not null)
        {
            AddError(errors, $"!{duplicate} is already used by another custom command.");
        }
    }

    private static CustomCommandTimeZone NormalizeTimeZone(
        string timeZoneId,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(normalized, out _))
        {
            AddError(errors, $"Time zone '{normalized}' was not found.");
        }

        return new(normalized);
    }

    private static string RequiredName(
        string value,
        string entityName,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddError(errors, $"{entityName} name is required.");
        }
        else if (trimmed.Length > _nameMaxLength)
        {
            AddError(errors, $"{entityName} names cannot exceed {_nameMaxLength} characters.");
        }

        return trimmed;
    }

    private static void EnsureUniqueEditorIds(
        IEnumerable<int> ids,
        string entityName,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (ids.GroupBy(id => id).Any(group => group.Count() > 1))
        {
            AddError(
                errors,
                $"Some {entityName} were duplicated while you were editing. Reload the page and try again."
            );
        }
    }

    private static void EnsureUniqueNames(
        IEnumerable<string> names,
        string entityName,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var duplicate = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            AddError(errors, $"Another {entityName} named '{duplicate}' already exists.");
        }
    }

    private static int ClampVariantIndex(int index, int variantCount)
    {
        return variantCount <= 0 ? 0 : Math.Clamp(index, 0, variantCount - 1);
    }

    private static void AddError(
        ICollection<CustomCommandConfigurationValidationError> errors,
        string message
    )
    {
        errors.Add(new(message));
    }
}

public sealed record CustomCommandConfigurationValidationError
{
    internal CustomCommandConfigurationValidationError(string message)
    {
        Message = message;
    }

    public string Message { get; }
}

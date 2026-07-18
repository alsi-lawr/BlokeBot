using BlokeBot.Announcements;
using BlokeBot.Commands;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public static class CustomCommandConfigurationValidator
{
    private const int _aliasMaxLength = 64;
    private const int _messageVariantMaxLength = 500;
    private const int _nameMaxLength = 128;
    private const int _twitchAnnouncementMaxLength = 500;

    public static Validation<
        CustomCommandConfigurationSaveCommand,
        CustomCommandConfigurationValidationError
    > Validate(CustomCommandConfiguration draft)
    {
        var errors = new List<CustomCommandConfigurationValidationError>();
        EnsureUniqueEditorIds(draft.MessageEntries.Select(entry => entry.Id), errors);
        EnsureUniqueEditorIds(draft.Counters.Select(counter => counter.Id), errors);
        EnsureUniqueEditorIds(draft.Commands.Select(command => command.Id), errors);
        EnsureUniqueEditorIds(draft.Announcements.Select(announcement => announcement.Id), errors);

        var messageNames = draft
            .MessageEntries.Select(entry =>
                RequiredName(
                    entry.Name,
                    "Reply",
                    ReplyTarget(entry.Id, CustomCommandValidationFieldKind.Name),
                    errors
                )
            )
            .ToArray();
        var counterNames = draft
            .Counters.Select(counter =>
                RequiredName(
                    counter.Name,
                    "Counter",
                    CounterTarget(counter.Id, CustomCommandValidationFieldKind.Name),
                    errors
                )
            )
            .ToArray();
        var commandNames = draft
            .Commands.Select(command =>
                RequiredName(
                    command.Name,
                    "Command",
                    CommandTarget(command.Id, CustomCommandValidationFieldKind.Name),
                    errors
                )
            )
            .ToArray();
        var announcementNames = draft
            .Announcements.Select(announcement =>
                RequiredName(
                    announcement.Name,
                    "Announcement",
                    AnnouncementTarget(announcement.Id, CustomCommandValidationFieldKind.Name),
                    errors
                )
            )
            .ToArray();
        EnsureUniqueNames(
            draft.MessageEntries.Select(
                (entry, index) =>
                    (
                        messageNames[index],
                        ReplyTarget(entry.Id, CustomCommandValidationFieldKind.Name)
                    )
            ),
            "reply",
            errors
        );
        EnsureUniqueNames(
            draft.Counters.Select(
                (counter, index) =>
                    (
                        counterNames[index],
                        CounterTarget(counter.Id, CustomCommandValidationFieldKind.Name)
                    )
            ),
            "counter",
            errors
        );
        EnsureUniqueNames(
            draft.Commands.Select(
                (command, index) =>
                    (
                        commandNames[index],
                        CommandTarget(command.Id, CustomCommandValidationFieldKind.Name)
                    )
            ),
            "command",
            errors
        );
        EnsureUniqueNames(
            draft.Announcements.Select(
                (announcement, index) =>
                    (
                        announcementNames[index],
                        AnnouncementTarget(announcement.Id, CustomCommandValidationFieldKind.Name)
                    )
            ),
            "announcement",
            errors
        );

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
            messageEntries
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => group.First()),
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
                AddError(
                    errors,
                    $"Choose how reply '{names[entryIndex]}' selects messages.",
                    ReplyTarget(editor.Id, CustomCommandValidationFieldKind.SelectionMode)
                );
            }

            if (editor.Variants.Count == 0)
            {
                AddError(
                    errors,
                    $"Reply '{names[entryIndex]}' needs at least one message.",
                    ReplyTarget(editor.Id, CustomCommandValidationFieldKind.VariantText)
                );
            }

            var variants = new List<CustomMessageVariantValue>(editor.Variants.Count);
            foreach (var variant in editor.Variants)
            {
                var text = variant.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    AddError(
                        errors,
                        $"Reply '{names[entryIndex]}' has a blank message.",
                        new(
                            CustomCommandSettingsTab.MessageLibrary,
                            CustomCommandValidationEntityKind.Variant,
                            editor.Id,
                            CustomCommandValidationFieldKind.VariantText,
                            variant.Id
                        )
                    );
                }
                else if (text.Length > _messageVariantMaxLength)
                {
                    AddError(
                        errors,
                        $"Reply messages cannot exceed {_messageVariantMaxLength} characters.",
                        new(
                            CustomCommandSettingsTab.MessageLibrary,
                            CustomCommandValidationEntityKind.Variant,
                            editor.Id,
                            CustomCommandValidationFieldKind.VariantText,
                            variant.Id
                        )
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
                AddError(
                    errors,
                    $"Choose a saved reply for command '{names[index]}'.",
                    new(
                        CustomCommandSettingsTab.Commands,
                        CustomCommandValidationEntityKind.Command,
                        editor.Id,
                        CustomCommandValidationFieldKind.Reply
                    )
                );
            }

            if (editor.CooldownSeconds < 0)
            {
                AddError(
                    errors,
                    "The wait between command uses cannot be negative.",
                    CommandTarget(editor.Id, CustomCommandValidationFieldKind.Cooldown)
                );
            }

            if (!Enum.IsDefined(editor.CooldownScope))
            {
                AddError(
                    errors,
                    $"Choose who waits for command '{names[index]}'.",
                    CommandTarget(editor.Id, CustomCommandValidationFieldKind.CooldownScope)
                );
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
                    editor.Id,
                    names[index],
                    counter,
                    errors
                ),
                _ => InvalidAction(editor.Id, names[index], editor.Action, errors),
            };
            var aliases = CommandAliasNormalizer.Split(editor.Aliases).ToArray();
            if (aliases.Length == 0)
            {
                AddError(
                    errors,
                    "Enter at least one command word.",
                    new(
                        CustomCommandSettingsTab.Commands,
                        CustomCommandValidationEntityKind.Command,
                        editor.Id,
                        CustomCommandValidationFieldKind.Aliases
                    )
                );
            }

            if (aliases.Any(alias => alias.Length > _aliasMaxLength))
            {
                AddError(
                    errors,
                    $"Command words cannot exceed {_aliasMaxLength} characters.",
                    new(
                        CustomCommandSettingsTab.Commands,
                        CustomCommandValidationEntityKind.Command,
                        editor.Id,
                        CustomCommandValidationFieldKind.Aliases
                    )
                );
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
        int commandId,
        string commandName,
        CounterCustomCommandActionEditor editor,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        AddError(
            errors,
            $"Choose a counter for command '{commandName}'.",
            CommandTarget(commandId, CustomCommandValidationFieldKind.Counter)
        );
        return new CustomCommandActionValue.Counter(editor.MessageLibraryEntryId, editor.CounterId);
    }

    private static CustomCommandActionValue InvalidAction(
        int commandId,
        string commandName,
        ICustomCommandActionEditor editor,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        AddError(
            errors,
            $"Choose what command '{commandName}' should do.",
            CommandTarget(commandId, CustomCommandValidationFieldKind.Action)
        );
        return new CustomCommandActionValue.Message(editor.MessageLibraryEntryId);
    }

    private static IReadOnlyList<CustomAnnouncementValue> SnapshotAnnouncements(
        IReadOnlyList<CustomAnnouncementEditor> editors,
        IReadOnlyList<string> names,
        IReadOnlySet<int> messageIds,
        IReadOnlyDictionary<int, CustomMessageLibraryEntryValue> messageEntries,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = new List<CustomAnnouncementValue>(editors.Count);
        for (var index = 0; index < editors.Count; index++)
        {
            var editor = editors[index];
            if (!messageIds.Contains(editor.MessageLibraryEntryId))
            {
                AddError(
                    errors,
                    $"Choose a saved reply for announcement '{names[index]}'.",
                    AnnouncementTarget(editor.Id, CustomCommandValidationFieldKind.Reply)
                );
            }

            if (!Enum.IsDefined(editor.DeliveryType))
            {
                AddError(
                    errors,
                    $"Choose how scheduled message '{names[index]}' is delivered.",
                    AnnouncementTarget(editor.Id, CustomCommandValidationFieldKind.Delivery)
                );
            }

            if (!Enum.IsDefined(editor.AnnouncementColor))
            {
                AddError(
                    errors,
                    $"Choose a supported Twitch announcement color for '{names[index]}'.",
                    editor.DeliveryType == CustomAnnouncementDeliveryType.TwitchAnnouncement
                        ? AnnouncementTarget(editor.Id, CustomCommandValidationFieldKind.Color)
                        : AnnouncementTarget(editor.Id, CustomCommandValidationFieldKind.Delivery)
                );
            }

            if (
                editor.DeliveryType == CustomAnnouncementDeliveryType.TwitchAnnouncement
                && messageEntries.TryGetValue(editor.MessageLibraryEntryId, out var messageEntry)
                && messageEntry.Variants.Any(variant =>
                    variant.Text.Length > _twitchAnnouncementMaxLength
                )
            )
            {
                AddError(
                    errors,
                    $"Every reply message for Twitch announcement '{names[index]}' must be at most {_twitchAnnouncementMaxLength} characters.",
                    AnnouncementTarget(editor.Id, CustomCommandValidationFieldKind.Reply)
                );
            }

            var retryDelay = RetryDelay(editor.Id, editor.RetryDelaySeconds, errors);
            var occurrenceLifetime = OccurrenceLifetime(
                editor.Id,
                editor.OccurrenceLifetimeSeconds,
                errors
            );
            if (
                retryDelay is not null
                && occurrenceLifetime is not null
                && retryDelay.Value >= occurrenceLifetime.Value
            )
            {
                AddError(
                    errors,
                    "Announcement retry delay must be less than its occurrence lifetime.",
                    AnnouncementTarget(editor.Id, CustomCommandValidationFieldKind.RetryDelay)
                );
            }

            var schedule = SnapshotSchedule(editor.Id, names[index], editor.Schedule, errors);
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
                    editor.DeliveryType,
                    editor.AnnouncementColor,
                    retryDelay,
                    occurrenceLifetime,
                    schedule
                )
            );
        }

        return values;
    }

    private static AnnouncementRetryDelay? RetryDelay(
        int announcementId,
        int seconds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (seconds <= 0)
        {
            AddError(
                errors,
                "Announcement retry delay must be positive.",
                AnnouncementTarget(announcementId, CustomCommandValidationFieldKind.RetryDelay)
            );
            return null;
        }

        return new(TimeSpan.FromSeconds(seconds));
    }

    private static AnnouncementOccurrenceLifetime? OccurrenceLifetime(
        int announcementId,
        int seconds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (seconds <= 0 || TimeSpan.FromSeconds(seconds) > AnnouncementOccurrenceLifetime.Maximum)
        {
            AddError(
                errors,
                "Announcement occurrence lifetime must be positive and no greater than 60 seconds.",
                AnnouncementTarget(
                    announcementId,
                    CustomCommandValidationFieldKind.OccurrenceLifetime
                )
            );
            return null;
        }

        return new(TimeSpan.FromSeconds(seconds));
    }

    private static CustomAnnouncementScheduleValue SnapshotSchedule(
        int announcementId,
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
                    AddError(
                        errors,
                        "Announcements must wait at least 1 minute.",
                        AnnouncementTarget(
                            announcementId,
                            CustomCommandValidationFieldKind.Interval
                        )
                    );
                }

                return new CustomAnnouncementScheduleValue.Interval(interval.IntervalMinutes);
            case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat:
                if (intervalAfterChat.IntervalMinutes < 1)
                {
                    AddError(
                        errors,
                        "Announcements must wait at least 1 minute.",
                        AnnouncementTarget(
                            announcementId,
                            CustomCommandValidationFieldKind.Interval
                        )
                    );
                }

                if (intervalAfterChat.RequiredChatMessages < 1)
                {
                    AddError(
                        errors,
                        "Chat-based announcements need at least 1 chat message.",
                        AnnouncementTarget(
                            announcementId,
                            CustomCommandValidationFieldKind.ChatMessages
                        )
                    );
                }

                return new CustomAnnouncementScheduleValue.IntervalAfterChat(
                    intervalAfterChat.IntervalMinutes,
                    intervalAfterChat.RequiredChatMessages
                );
            case WeeklyCustomAnnouncementScheduleEditor weekly:
                if (!Enum.IsDefined(weekly.Day))
                {
                    AddError(
                        errors,
                        "Choose a valid day for weekly announcements.",
                        AnnouncementTarget(announcementId, CustomCommandValidationFieldKind.Day)
                    );
                }

                return new CustomAnnouncementScheduleValue.Weekly(weekly.Day, weekly.Time);
            default:
                AddError(
                    errors,
                    $"Choose when announcement '{announcementName}' should be sent.",
                    AnnouncementTarget(announcementId, CustomCommandValidationFieldKind.Schedule)
                );
                return new CustomAnnouncementScheduleValue.Interval(1);
        }
    }

    private static void EnsureUniqueAliases(
        IReadOnlyList<CustomCommandValue> commands,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var duplicate = commands
            .SelectMany(command =>
                command.Aliases.Select(alias => new { Alias = alias, command.Id })
            )
            .GroupBy(value => value.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(value => value.Id).Distinct().Count() > 1);
        if (duplicate is not null)
        {
            AddError(
                errors,
                $"!{duplicate.Key} is already used by another custom command.",
                new(
                    CustomCommandSettingsTab.Commands,
                    CustomCommandValidationEntityKind.Command,
                    duplicate.First().Id,
                    CustomCommandValidationFieldKind.Aliases
                )
            );
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
            AddError(
                errors,
                $"Time zone '{normalized}' was not found.",
                new(
                    CustomCommandSettingsTab.Commands,
                    CustomCommandValidationEntityKind.Configuration,
                    0,
                    CustomCommandValidationFieldKind.TimeZone
                )
            );
        }

        return new(normalized);
    }

    private static string RequiredName(
        string value,
        string entityName,
        CustomCommandConfigurationValidationTarget target,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddError(errors, $"{entityName} name is required.", target);
        }
        else if (trimmed.Length > _nameMaxLength)
        {
            AddError(
                errors,
                $"{entityName} names cannot exceed {_nameMaxLength} characters.",
                target
            );
        }

        return trimmed;
    }

    private static void EnsureUniqueEditorIds(
        IEnumerable<int> ids,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (ids.GroupBy(id => id).FirstOrDefault(group => group.Count() > 1) is not null)
        {
            AddError(
                errors,
                "Some items were duplicated while you were editing. Reload the page and try again.",
                ConfigurationTarget(CustomCommandValidationFieldKind.Identity)
            );
        }
    }

    private static void EnsureUniqueNames(
        IEnumerable<(string Name, CustomCommandConfigurationValidationTarget Target)> names,
        string entityName,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var duplicate = names
            .Where(value => !string.IsNullOrWhiteSpace(value.Name))
            .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            AddError(
                errors,
                $"Another {entityName} named '{duplicate.Key}' already exists.",
                duplicate.First().Target
            );
        }
    }

    private static int ClampVariantIndex(int index, int variantCount)
    {
        return variantCount <= 0 ? 0 : Math.Clamp(index, 0, variantCount - 1);
    }

    private static CustomCommandConfigurationValidationTarget ReplyTarget(
        int replyId,
        CustomCommandValidationFieldKind field
    )
    {
        return new(
            CustomCommandSettingsTab.MessageLibrary,
            CustomCommandValidationEntityKind.Reply,
            replyId,
            field
        );
    }

    private static CustomCommandConfigurationValidationTarget CounterTarget(
        int counterId,
        CustomCommandValidationFieldKind field
    )
    {
        return new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Counter,
            counterId,
            field
        );
    }

    private static CustomCommandConfigurationValidationTarget CommandTarget(
        int commandId,
        CustomCommandValidationFieldKind field
    )
    {
        return new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Command,
            commandId,
            field
        );
    }

    private static CustomCommandConfigurationValidationTarget AnnouncementTarget(
        int announcementId,
        CustomCommandValidationFieldKind field
    )
    {
        return new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.ScheduledMessage,
            announcementId,
            field
        );
    }

    private static CustomCommandConfigurationValidationTarget ConfigurationTarget(
        CustomCommandValidationFieldKind field
    )
    {
        return new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Configuration,
            0,
            field
        );
    }

    private static void AddError(
        ICollection<CustomCommandConfigurationValidationError> errors,
        string message,
        CustomCommandConfigurationValidationTarget target
    )
    {
        errors.Add(new(message, target));
    }
}

public sealed record CustomCommandConfigurationValidationError(
    string Message,
    CustomCommandConfigurationValidationTarget Target
);

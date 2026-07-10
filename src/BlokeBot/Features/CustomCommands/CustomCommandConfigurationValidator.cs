using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

internal static class CustomCommandConfigurationValidator
{
    private const int MessageVariantMaxLength = 500;
    private const int NameMaxLength = 128;

    public static void Validate(CustomCommandConfiguration config)
    {
        EnsureUniqueEditorIds(config.MessageEntries.Select(x => x.Id), "message library entries");
        EnsureUniqueEditorIds(config.Counters.Select(x => x.Id), "counters");
        EnsureUniqueEditorIds(config.Commands.Select(x => x.Id), "custom commands");
        EnsureUniqueEditorIds(config.Announcements.Select(x => x.Id), "announcements");
        EnsureUniqueNames(config.MessageEntries, x => x.Name, "message library entry");
        EnsureUniqueNames(config.Counters, x => x.Name, "counter");
        EnsureUniqueNames(config.Commands, x => x.Name, "custom command");
        EnsureUniqueNames(config.Announcements, x => x.Name, "announcement");

        var messageEntryIds = config.MessageEntries.Select(x => x.Id).ToHashSet();
        var counterIds = config.Counters.Select(x => x.Id).ToHashSet();
        foreach (var entry in config.MessageEntries)
        {
            RequiredName(entry.Name, "Message library entry");
            if (entry.Variants.Count == 0)
                throw new InvalidOperationException(
                    $"Message library entry '{entry.Name.Trim()}' needs at least one variant."
                );

            foreach (var variant in entry.Variants)
            {
                var text = variant.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(
                        $"Message library entry '{entry.Name.Trim()}' has an empty variant."
                    );

                if (text.Length > MessageVariantMaxLength)
                    throw new InvalidOperationException(
                        $"Message variants cannot exceed {MessageVariantMaxLength} characters."
                    );
            }
        }

        foreach (var counter in config.Counters)
            RequiredName(counter.Name, "Counter");

        foreach (var command in config.Commands)
        {
            RequiredName(command.Name, "Custom command");
            if (!messageEntryIds.Contains(command.Action.MessageLibraryEntryId))
                throw new InvalidOperationException(
                    $"Custom command '{command.Name.Trim()}' needs a message library entry."
                );

            if (command.CooldownSeconds < 0)
                throw new InvalidOperationException("Command cooldown seconds cannot be negative.");

            switch (command.Action)
            {
                case MessageCustomCommandActionEditor:
                    break;
                case CounterCustomCommandActionEditor counter
                    when !counterIds.Contains(counter.CounterId):
                    throw new InvalidOperationException(
                        $"Custom command '{command.Name.Trim()}' references a missing counter."
                    );
                case CounterCustomCommandActionEditor:
                    break;
                default:
                    throw new InvalidOperationException("Unsupported custom command action.");
            }
        }

        foreach (var announcement in config.Announcements)
        {
            RequiredName(announcement.Name, "Announcement");
            if (!messageEntryIds.Contains(announcement.MessageLibraryEntryId))
                throw new InvalidOperationException(
                    $"Announcement '{announcement.Name.Trim()}' needs a message library entry."
                );

            switch (announcement.Schedule)
            {
                case IntervalCustomAnnouncementScheduleEditor interval
                    when interval.IntervalMinutes < 1:
                    throw new InvalidOperationException(
                        "Announcement interval minutes must be at least 1."
                    );
                case IntervalCustomAnnouncementScheduleEditor:
                    break;
                case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat
                    when intervalAfterChat.IntervalMinutes < 1:
                    throw new InvalidOperationException(
                        "Announcement interval minutes must be at least 1."
                    );
                case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat
                    when intervalAfterChat.RequiredChatMessages < 1:
                    throw new InvalidOperationException(
                        "Interval-after-chat announcements need at least one required chat message."
                    );
                case IntervalAfterChatCustomAnnouncementScheduleEditor:
                    break;
                case WeeklyCustomAnnouncementScheduleEditor weekly:
                    if (!Enum.IsDefined(weekly.Day))
                        throw new InvalidOperationException(
                            "Weekly announcement day is invalid."
                        );
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported custom announcement schedule."
                    );
            }
        }
    }

    public static void ValidateExistingIds(
        CustomCommandConfiguration config,
        IReadOnlyList<CustomMessageLibraryEntry> messageEntries,
        IReadOnlyList<CustomCounter> counters,
        IReadOnlyList<CustomCommand> commands,
        IReadOnlyList<CustomAnnouncement> announcements
    )
    {
        EnsurePositiveIdsExist(
            config.MessageEntries.Select(x => x.Id),
            messageEntries.Select(x => x.Id).ToHashSet(),
            "Message library entry"
        );
        EnsurePositiveIdsExist(
            config.Counters.Select(x => x.Id),
            counters.Select(x => x.Id).ToHashSet(),
            "Counter"
        );
        EnsurePositiveIdsExist(
            config.Commands.Select(x => x.Id),
            commands.Select(x => x.Id).ToHashSet(),
            "Custom command"
        );
        EnsurePositiveIdsExist(
            config.Announcements.Select(x => x.Id),
            announcements.Select(x => x.Id).ToHashSet(),
            "Announcement"
        );
    }

    public static string RequiredName(string value, string entityName)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{entityName} name is required.");

        if (trimmed.Length > NameMaxLength)
            throw new InvalidOperationException(
                $"{entityName} names cannot exceed {NameMaxLength} characters."
            );

        return trimmed;
    }

    private static void EnsureUniqueEditorIds(IEnumerable<int> ids, string entityName)
    {
        var duplicate = ids.GroupBy(x => x).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Editor IDs for {entityName} must be unique."
            );
    }

    private static void EnsureUniqueNames<T>(
        IEnumerable<T> editors,
        Func<T, string> name,
        string entityName
    )
    {
        var duplicate = editors
            .Select(x => RequiredName(name(x), entityName))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1)
            ?.Key;
        if (!string.IsNullOrWhiteSpace(duplicate))
            throw new InvalidOperationException(
                $"A {entityName} named '{duplicate}' already exists."
            );
    }

    private static void EnsurePositiveIdsExist(
        IEnumerable<int> editorIds,
        IReadOnlySet<int> existingIds,
        string entityName
    )
    {
        var missingId = editorIds.Where(x => x > 0).FirstOrDefault(x => !existingIds.Contains(x));
        if (missingId != 0)
            throw new InvalidOperationException($"{entityName} {missingId} was not found.");
    }
}

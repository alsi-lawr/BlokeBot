using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

internal static class CustomCommandConfigurationValidator
{
    private const int MessageVariantMaxLength = 500;
    private const int NameMaxLength = 128;

    public static void Validate(CustomCommandConfiguration config)
    {
        EnsureUniqueEditorIds(config.MessageEntries.Select(x => x.Id), "replies");
        EnsureUniqueEditorIds(config.Counters.Select(x => x.Id), "counters");
        EnsureUniqueEditorIds(config.Commands.Select(x => x.Id), "commands");
        EnsureUniqueEditorIds(config.Announcements.Select(x => x.Id), "announcements");
        EnsureUniqueNames(config.MessageEntries, x => x.Name, "reply");
        EnsureUniqueNames(config.Counters, x => x.Name, "counter");
        EnsureUniqueNames(config.Commands, x => x.Name, "command");
        EnsureUniqueNames(config.Announcements, x => x.Name, "announcement");

        var messageEntryIds = config.MessageEntries.Select(x => x.Id).ToHashSet();
        var counterIds = config.Counters.Select(x => x.Id).ToHashSet();
        foreach (var entry in config.MessageEntries)
        {
            RequiredName(entry.Name, "Reply");
            if (entry.Variants.Count == 0)
                throw new InvalidOperationException(
                    $"Reply '{entry.Name.Trim()}' needs at least one message."
                );

            foreach (var variant in entry.Variants)
            {
                var text = variant.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(
                        $"Reply '{entry.Name.Trim()}' has a blank message."
                    );

                if (text.Length > MessageVariantMaxLength)
                    throw new InvalidOperationException(
                        $"Reply messages cannot exceed {MessageVariantMaxLength} characters."
                    );
            }
        }

        foreach (var counter in config.Counters)
            RequiredName(counter.Name, "Counter");

        foreach (var command in config.Commands)
        {
            RequiredName(command.Name, "Command");
            if (!messageEntryIds.Contains(command.Action.MessageLibraryEntryId))
                throw new InvalidOperationException(
                    $"Choose a saved reply for command '{command.Name.Trim()}'."
                );

            if (command.CooldownSeconds < 0)
                throw new InvalidOperationException(
                    "The wait between command uses cannot be negative."
                );

            switch (command.Action)
            {
                case MessageCustomCommandActionEditor:
                    break;
                case CounterCustomCommandActionEditor counter
                    when !counterIds.Contains(counter.CounterId):
                    throw new InvalidOperationException(
                        $"Choose a counter for command '{command.Name.Trim()}'."
                    );
                case CounterCustomCommandActionEditor:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Choose what command '{command.Name.Trim()}' should do."
                    );
            }
        }

        foreach (var announcement in config.Announcements)
        {
            RequiredName(announcement.Name, "Announcement");
            if (!messageEntryIds.Contains(announcement.MessageLibraryEntryId))
                throw new InvalidOperationException(
                    $"Choose a saved reply for announcement '{announcement.Name.Trim()}'."
                );

            switch (announcement.Schedule)
            {
                case IntervalCustomAnnouncementScheduleEditor interval
                    when interval.IntervalMinutes < 1:
                    throw new InvalidOperationException(
                        "Announcements must wait at least 1 minute."
                    );
                case IntervalCustomAnnouncementScheduleEditor:
                    break;
                case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat
                    when intervalAfterChat.IntervalMinutes < 1:
                    throw new InvalidOperationException(
                        "Announcements must wait at least 1 minute."
                    );
                case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat
                    when intervalAfterChat.RequiredChatMessages < 1:
                    throw new InvalidOperationException(
                        "Chat-based announcements need at least 1 chat message."
                    );
                case IntervalAfterChatCustomAnnouncementScheduleEditor:
                    break;
                case WeeklyCustomAnnouncementScheduleEditor weekly:
                    if (!Enum.IsDefined(weekly.Day))
                        throw new InvalidOperationException(
                            "Choose a valid day for weekly announcements."
                        );
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Choose when announcement '{announcement.Name.Trim()}' should be sent."
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
            "saved reply"
        );
        EnsurePositiveIdsExist(
            config.Counters.Select(x => x.Id),
            counters.Select(x => x.Id).ToHashSet(),
            "counter"
        );
        EnsurePositiveIdsExist(
            config.Commands.Select(x => x.Id),
            commands.Select(x => x.Id).ToHashSet(),
            "command"
        );
        EnsurePositiveIdsExist(
            config.Announcements.Select(x => x.Id),
            announcements.Select(x => x.Id).ToHashSet(),
            "announcement"
        );
    }

    public static string RequiredName(string value, string entityName)
    {
        var trimmed = value.Trim();
        var displayName = char.ToUpperInvariant(entityName[0]) + entityName[1..];
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{displayName} name is required.");

        if (trimmed.Length > NameMaxLength)
            throw new InvalidOperationException(
                $"{displayName} names cannot exceed {NameMaxLength} characters."
            );

        return trimmed;
    }

    private static void EnsureUniqueEditorIds(IEnumerable<int> ids, string entityName)
    {
        var duplicate = ids.GroupBy(x => x).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Some {entityName} were duplicated while you were editing. Reload the page and try again."
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
                $"Another {entityName} named '{duplicate}' already exists."
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
            throw new InvalidOperationException(
                $"A {entityName} you edited is no longer available. Reload the page and try again."
            );
    }
}

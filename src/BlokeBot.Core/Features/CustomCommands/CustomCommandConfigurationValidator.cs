using BlokeBot.Functional;

namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
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
        var timeZone = NormalizeTimeZone(draft.TimeZoneId, errors, out var projectionTimeZone);
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
            projectionTimeZone,
            draft.ProjectionReferenceUtc,
            errors
        );
        EnsureUniqueAliases(commands, errors);

        return errors.Count > 0
            ? Validation<
                CustomCommandConfigurationSaveCommand,
                CustomCommandConfigurationValidationError
            >.Invalid(errors[0], errors.Skip(1).ToArray())
            : Validation<
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
}

public sealed record CustomCommandConfigurationValidationError(
    string Message,
    CustomCommandConfigurationValidationTarget Target
);

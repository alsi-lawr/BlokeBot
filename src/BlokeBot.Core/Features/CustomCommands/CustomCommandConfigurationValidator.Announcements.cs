using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
{
    private static IReadOnlyList<CustomAnnouncementValue> SnapshotAnnouncements(
        IReadOnlyList<CustomAnnouncementEditor> editors,
        IReadOnlyList<string> names,
        IReadOnlySet<int> messageIds,
        IReadOnlyDictionary<int, CustomMessageLibraryEntryValue> messageEntries,
        TimeZoneInfo timeZone,
        DateTimeOffset projectionReference,
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
                && messageEntry.Variants.Any(static variant =>
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

            var schedule = SnapshotSchedule(
                editor.Id,
                names[index],
                editor.Schedule,
                timeZone,
                projectionReference,
                errors
            );
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
}

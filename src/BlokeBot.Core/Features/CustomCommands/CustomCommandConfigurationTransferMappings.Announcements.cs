using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter
{
    private static ImportedAnnouncements MapAnnouncements(
        AnnouncementsSectionV1 section,
        TimeZoneInfo destinationTimeZone,
        DateTimeOffset projectionReference,
        ref int nextId
    )
    {
        var firstReplyId = nextId;
        var replyIds = section
            .Replies.Select((reply, index) => (reply.Id, Value: firstReplyId - index))
            .ToDictionary(x => x.Id, x => x.Value, StringComparer.Ordinal);
        nextId -= section.Replies.Count;
        var firstAnnouncementId = nextId;
        nextId -= section.Items.Count;
        return new(
            section.Replies.Select(x => MapReply(x, replyIds[x.Id])).ToList(),
            section
                .Items.Select(
                    (item, index) =>
                        MapAnnouncement(
                            item,
                            replyIds[item.MessageReplyId],
                            firstAnnouncementId - index,
                            destinationTimeZone,
                            projectionReference
                        )
                )
                .ToList()
        );
    }

    private static void RemapAnnouncementReferences(
        IEnumerable<CustomAnnouncementEditor> announcements,
        IReadOnlyDictionary<CustomMessageLibraryEntryEditor, int> originalReplyIds
    )
    {
        var replyIds = originalReplyIds.ToDictionary(x => x.Value, x => x.Key.Id);
        foreach (var announcement in announcements)
        {
            announcement.MessageLibraryEntryId = replyIds.GetValueOrDefault(
                announcement.MessageLibraryEntryId,
                announcement.MessageLibraryEntryId
            );
        }
    }

    private static CustomAnnouncementEditor MapAnnouncement(
        AnnouncementV1 value,
        int replyId,
        int id,
        TimeZoneInfo destinationTimeZone,
        DateTimeOffset projectionReference
    ) =>
        new()
        {
            Id = id,
            Name = value.Name,
            Enabled = value.Enabled,
            MessageLibraryEntryId = replyId,
            DeliveryType = value.DeliveryType,
            AnnouncementColor = value.AnnouncementColor,
            RetryDelaySeconds = value.RetryDelaySeconds,
            OccurrenceLifetimeSeconds = value.OccurrenceLifetimeSeconds,
            Schedule = value.Schedule.Type switch
            {
                AnnouncementScheduleTypeV1.Interval => new IntervalCustomAnnouncementScheduleEditor
                {
                    IntervalMinutes = value.Schedule.IntervalMinutes ?? 0,
                },
                AnnouncementScheduleTypeV1.IntervalAfterChat =>
                    new IntervalAfterChatCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = value.Schedule.IntervalMinutes ?? 0,
                        RequiredChatMessages = value.Schedule.RequiredChatMessages ?? 0,
                    },
                AnnouncementScheduleTypeV1.Weekly =>
                    WeeklyAnnouncementScheduleEditorProjection.FromUtc(
                        value.Schedule.Day ?? DayOfWeek.Monday,
                        value.Schedule.Time ?? default,
                        destinationTimeZone,
                        projectionReference
                    ),
                _ => throw new InvalidOperationException("Unsupported announcement schedule."),
            },
        };
}

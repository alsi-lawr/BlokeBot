using BlokeBot.Announcements;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationExportMappers
{
    internal sealed record CommandGraph(
        IReadOnlyList<CustomMessageLibraryEntry> Replies,
        IReadOnlyList<CustomCounter> Counters,
        IReadOnlyList<CustomCommand> Commands,
        IReadOnlyList<CustomAnnouncement> Announcements
    );

    internal static async Task<CommandGraph> LoadCommandGraphAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        new(
            await db
                .CustomMessageLibraryEntries.AsNoTracking()
                .Include(x => x.Variants)
                .Where(x => x.HostId == hostId)
                .OrderBy(x => x.Name)
                .ToArrayAsync(cancellationToken),
            await db
                .CustomCounters.AsNoTracking()
                .Where(x => x.HostId == hostId)
                .OrderBy(x => x.Name)
                .ToArrayAsync(cancellationToken),
            await db
                .CustomCommands.AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Action)
                .Include(x => x.Aliases)
                .Include(x => x.AllowedUsers)
                .Where(x => x.HostId == hostId)
                .OrderBy(x => x.Name)
                .ToArrayAsync(cancellationToken),
            await db
                .CustomAnnouncements.AsNoTracking()
                .Include(x => x.Schedule)
                .Include(x => x.DeliveryPolicy)
                .Where(x => x.HostId == hostId)
                .OrderBy(x => x.Name)
                .ToArrayAsync(cancellationToken)
        );

    internal static CustomCommandsSectionV1 CustomCommands(CommandGraph graph, string timeZoneId)
    {
        var replyIds = LocalIds("reply", graph.Replies.Select(x => x.Id));
        var counterIds = LocalIds("counter", graph.Counters.Select(x => x.Id));
        var commandIds = LocalIds("command", graph.Commands.Select(x => x.Id));
        return new(
            timeZoneId,
            graph.Replies.Select(x => Reply(x, replyIds[x.Id])).ToArray(),
            graph.Counters.Select(x => new CounterV1(counterIds[x.Id], x.Name, x.Value)).ToArray(),
            graph.Commands.Select(x => Command(x, commandIds[x.Id], replyIds, counterIds)).ToArray()
        );
    }

    internal static AnnouncementsSectionV1 Announcements(CommandGraph graph)
    {
        var selectedReplyIds = graph.Announcements.Select(x => x.MessageLibraryEntryId).ToHashSet();
        var replies = graph.Replies.Where(x => selectedReplyIds.Contains(x.Id)).ToArray();
        var replyIds = LocalIds("reply", replies.Select(x => x.Id));
        var announcementIds = LocalIds("announcement", graph.Announcements.Select(x => x.Id));
        return new(
            replies.Select(x => Reply(x, replyIds[x.Id])).ToArray(),
            graph
                .Announcements.Select(x => Announcement(x, announcementIds[x.Id], replyIds))
                .ToArray()
        );
    }

    private static MessageEntryV1 Reply(CustomMessageLibraryEntry value, string id) =>
        new(
            id,
            value.Name,
            value.SelectionMode,
            value.Variants.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => x.Text).ToArray()
        );

    private static CustomCommandV1 Command(
        CustomCommand value,
        string id,
        IReadOnlyDictionary<int, string> replyIds,
        IReadOnlyDictionary<int, string> counterIds
    ) =>
        new(
            id,
            value.Name,
            value.Enabled,
            value
                .Aliases.OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => x.Alias)
                .ToArray(),
            value.AllowEveryone,
            value.AllowModerators,
            value
                .AllowedUsers.OrderBy(x => x.Login)
                .Select(x => new AllowedUserV1(x.TwitchUserId, x.Login, x.DisplayName))
                .ToArray(),
            value.CooldownSeconds,
            value.CooldownScope,
            value.InvocationLimit,
            Action(value.Action, replyIds, counterIds)
        );

    private static CustomCommandActionV1 Action(
        CustomCommandAction value,
        IReadOnlyDictionary<int, string> replyIds,
        IReadOnlyDictionary<int, string> counterIds
    ) =>
        new(
            value switch
            {
                MessageCustomCommandAction => CustomCommandActionTypeV1.Message,
                CounterCustomCommandAction => CustomCommandActionTypeV1.Counter,
                AutomationCustomCommandAction => CustomCommandActionTypeV1.Automation,
                OverlayCueCustomCommandAction => CustomCommandActionTypeV1.OverlayCue,
                _ => throw new InvalidOperationException("Unsupported custom command action."),
            },
            Reference(value.ZeroArgumentMessageLibraryEntryId, replyIds),
            Reference(value.OneArgumentMessageLibraryEntryId, replyIds),
            Reference(value.TwoArgumentMessageLibraryEntryId, replyIds),
            value is CounterCustomCommandAction counter ? counterIds[counter.CounterId] : null
        );

    private static AnnouncementV1 Announcement(
        CustomAnnouncement value,
        string id,
        IReadOnlyDictionary<int, string> replyIds
    )
    {
        var policy = (RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy)
            value.DeliveryPolicy;
        return new(
            id,
            value.Name,
            value.Enabled,
            replyIds[value.MessageLibraryEntryId],
            value.DeliveryType,
            value.AnnouncementColor,
            WholeSeconds(policy.RetryDelay),
            WholeSeconds(policy.OccurrenceLifetime),
            Schedule(value.Schedule)
        );
    }

    private static AnnouncementScheduleV1 Schedule(CustomAnnouncementSchedule value) =>
        value switch
        {
            IntervalCustomAnnouncementSchedule x => new(
                AnnouncementScheduleTypeV1.Interval,
                x.IntervalMinutes
            ),
            IntervalAfterChatCustomAnnouncementSchedule x => new(
                AnnouncementScheduleTypeV1.IntervalAfterChat,
                x.IntervalMinutes,
                x.RequiredChatMessages
            ),
            WeeklyCustomAnnouncementSchedule x => new(
                AnnouncementScheduleTypeV1.Weekly,
                Day: x.Day,
                Time: x.Time
            ),
            _ => throw new InvalidOperationException("Unsupported announcement schedule."),
        };

    private static int WholeSeconds(AnnouncementRetryDelay value) =>
        checked((int)value.Value.TotalSeconds);

    private static int WholeSeconds(AnnouncementOccurrenceLifetime value) =>
        checked((int)value.Value.TotalSeconds);

    private static string? Reference(int? id, IReadOnlyDictionary<int, string> ids) =>
        id is { } value ? ids[value] : null;

    private static Dictionary<int, string> LocalIds(string prefix, IEnumerable<int> sourceIds) =>
        sourceIds
            .Select((id, index) => (id, value: $"{prefix}-{index + 1:D4}"))
            .ToDictionary(x => x.id, x => x.value);
}

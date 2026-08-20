using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationGraphWriter
{
    private static async Task<Dictionary<int, CustomMessageLibraryEntry>> StageMessageEntriesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomMessageLibraryEntryValue> editors,
        IReadOnlyList<CustomMessageLibraryEntry> existing,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(static x => x.Id);
        var result = new Dictionary<int, CustomMessageLibraryEntry>();
        foreach (var editor in editors)
        {
            var entry =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomMessageLibraryEntry { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
            {
                _ = db.CustomMessageLibraryEntries.Add(entry);
            }

            entry.Name = TemporaryName("message", editor.Id);
            entry.SelectionMode = editor.SelectionMode;
            entry.CurrentVariantIndex = 0;
            entry.UpdatedAtUtc = now;
            result[editor.Id] = entry;
        }

        _ = await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<int, CustomCounter>> StageCountersAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCounterValue> editors,
        IReadOnlyList<CustomCounter> existing,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(static x => x.Id);
        var result = new Dictionary<int, CustomCounter>();
        foreach (var editor in editors)
        {
            var counter =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCounter { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
            {
                _ = db.CustomCounters.Add(counter);
            }

            counter.Name = TemporaryName("counter", editor.Id);
            counter.Value = editor.Value;
            counter.UpdatedAtUtc = now;
            result[editor.Id] = counter;
        }

        _ = await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<int, CustomCommand>> StageCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCommandValue> editors,
        IReadOnlyList<CustomCommand> existing,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(static x => x.Id);
        var result = new Dictionary<int, CustomCommand>();
        foreach (var editor in editors)
        {
            var command =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCommand { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
            {
                _ = db.CustomCommands.Add(command);
            }

            command.Name = TemporaryName("command", editor.Id);
            command.Enabled = editor.Enabled;
            command.AllowEveryone = editor.AllowEveryone;
            command.AllowModerators = editor.AllowModerators;
            command.CooldownSeconds = editor.CooldownSeconds;
            command.CooldownScope = editor.CooldownScope;
            command.InvocationLimit = editor.InvocationLimit;
            command.Action = CustomCommandConfigurationMapper.ActionMatches(
                command.Action,
                editor.Action
            )
                ? command.Action
                : CustomCommandConfigurationMapper.CreateAction(
                    hostId,
                    editor.Action,
                    messageEntries,
                    counters
                );
            CustomCommandConfigurationMapper.ApplyAction(
                command.Action,
                editor.Action,
                messageEntries,
                counters
            );
            command.UpdatedAtUtc = now;
            result[editor.Id] = command;
        }

        _ = await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<int, CustomAnnouncement>> StageAnnouncementsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomAnnouncementValue> editors,
        IReadOnlyList<CustomAnnouncement> existing,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(static x => x.Id);
        var result = new Dictionary<int, CustomAnnouncement>();
        foreach (var editor in editors)
        {
            var announcement =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomAnnouncement
                    {
                        HostId = hostId,
                        CreatedAtUtc = now,
                        DeliveryPolicy = CustomCommandConfigurationMapper.CreateDeliveryPolicy(
                            hostId,
                            editor
                        ),
                    };
            if (editor.Id <= 0)
            {
                _ = db.CustomAnnouncements.Add(announcement);
            }

            announcement.Name = TemporaryName("announcement", editor.Id);
            announcement.Enabled = editor.Enabled;
            announcement.MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id;
            announcement.DeliveryType = editor.DeliveryType;
            announcement.AnnouncementColor = editor.AnnouncementColor;
            announcement.Schedule = CustomCommandConfigurationMapper.ScheduleMatches(
                announcement.Schedule,
                editor.Schedule
            )
                ? announcement.Schedule
                : CustomCommandConfigurationMapper.CreateSchedule(hostId, editor.Schedule);
            CustomCommandConfigurationMapper.ApplySchedule(announcement.Schedule, editor.Schedule);
            CustomCommandConfigurationMapper.ApplyDeliveryPolicy(
                announcement.DeliveryPolicy,
                editor
            );
            announcement.UpdatedAtUtc = now;
            result[editor.Id] = announcement;
        }

        _ = await db.SaveChangesAsync(ct);
        return result;
    }
}

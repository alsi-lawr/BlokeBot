using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandConfigurationGraphWriter(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    public async Task WriteAsync(
        int hostId,
        CustomCommandConfiguration config,
        IReadOnlyDictionary<CustomCommandEditor, string[]> normalizedAliases,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var messageEntries = await db
            .CustomMessageLibraryEntries.Include(x => x.Variants)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var counters = await db.CustomCounters.Where(x => x.HostId == hostId).ToListAsync(ct);
        var commands = await db
            .CustomCommands.Include(x => x.Action)
            .Include(x => x.Aliases)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var announcements = await db
            .CustomAnnouncements.Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);

        CustomCommandConfigurationValidator.ValidateExistingIds(
            config,
            messageEntries,
            counters,
            commands,
            announcements
        );
        await RemoveChangedVariantsAsync(db, config, commands, announcements, ct);

        var messageEntityByEditorId = await StageMessageEntriesAsync(
            db,
            hostId,
            config.MessageEntries,
            messageEntries,
            now,
            ct
        );
        var counterEntityByEditorId = await StageCountersAsync(
            db,
            hostId,
            config.Counters,
            counters,
            now,
            ct
        );
        var commandEntityByEditor = await StageCommandsAsync(
            db,
            hostId,
            config.Commands,
            commands,
            messageEntityByEditorId,
            counterEntityByEditorId,
            now,
            ct
        );
        var announcementEntityByEditor = await StageAnnouncementsAsync(
            db,
            hostId,
            config.Announcements,
            announcements,
            messageEntityByEditorId,
            now,
            ct
        );

        await DeleteRemovedDependentsAsync(db, config, commands, announcements, ct);
        await DeleteRemovedPrincipalsAsync(db, config, messageEntries, counters, ct);
        await ReplaceVariantsAsync(db, config.MessageEntries, messageEntityByEditorId, ct);
        await ReplaceAliasesAsync(db, hostId, commandEntityByEditor, normalizedAliases, ct);
        ApplyFinalFields(
            config,
            messageEntityByEditorId,
            counterEntityByEditorId,
            commandEntityByEditor,
            announcementEntityByEditor,
            now
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task<Dictionary<int, CustomMessageLibraryEntry>> StageMessageEntriesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomMessageLibraryEntryEditor> editors,
        IReadOnlyList<CustomMessageLibraryEntry> existing,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<int, CustomMessageLibraryEntry>();
        foreach (var editor in editors)
        {
            var entry =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomMessageLibraryEntry
                    {
                        HostId = hostId,
                        CreatedAtUtc = now,
                    };
            if (editor.Id <= 0)
                db.CustomMessageLibraryEntries.Add(entry);

            entry.Name = TemporaryName("message", editor.Id);
            entry.SelectionMode = editor.SelectionMode;
            entry.CurrentVariantIndex = 0;
            entry.UpdatedAtUtc = now;
            result[editor.Id] = entry;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<int, CustomCounter>> StageCountersAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCounterEditor> editors,
        IReadOnlyList<CustomCounter> existing,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<int, CustomCounter>();
        foreach (var editor in editors)
        {
            var counter =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCounter { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
                db.CustomCounters.Add(counter);

            counter.Name = TemporaryName("counter", editor.Id);
            counter.Value = editor.Value;
            counter.UpdatedAtUtc = now;
            result[editor.Id] = counter;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<CustomCommandEditor, CustomCommand>> StageCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCommandEditor> editors,
        IReadOnlyList<CustomCommand> existing,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<CustomCommandEditor, CustomCommand>();
        foreach (var editor in editors)
        {
            var command =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCommand { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
                db.CustomCommands.Add(command);

            command.Name = TemporaryName("command", editor.Id);
            command.Enabled = editor.Enabled;
            command.ModeratorOnly = editor.ModeratorOnly;
            command.CooldownSeconds = editor.CooldownSeconds;
            command.CooldownScope = editor.CooldownScope;
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
            result[editor] = command;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<
        Dictionary<CustomAnnouncementEditor, CustomAnnouncement>
    > StageAnnouncementsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomAnnouncementEditor> editors,
        IReadOnlyList<CustomAnnouncement> existing,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<CustomAnnouncementEditor, CustomAnnouncement>();
        foreach (var editor in editors)
        {
            var announcement =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomAnnouncement
                    {
                        HostId = hostId,
                        CreatedAtUtc = now,
                        DeliveryPolicy =
                            CustomCommandConfigurationMapper.CreateDeliveryPolicy(
                                hostId,
                                editor
                            ),
                    };
            if (editor.Id <= 0)
                db.CustomAnnouncements.Add(announcement);

            announcement.Name = TemporaryName("announcement", editor.Id);
            announcement.Enabled = editor.Enabled;
            announcement.MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id;
            announcement.Schedule = CustomCommandConfigurationMapper.ScheduleMatches(
                announcement.Schedule,
                editor.Schedule
            )
                ? announcement.Schedule
                : CustomCommandConfigurationMapper.CreateSchedule(hostId, editor.Schedule);
            CustomCommandConfigurationMapper.ApplySchedule(
                announcement.Schedule,
                editor.Schedule
            );
            CustomCommandConfigurationMapper.ApplyDeliveryPolicy(
                announcement.DeliveryPolicy,
                editor
            );
            announcement.UpdatedAtUtc = now;
            result[editor] = announcement;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task RemoveChangedVariantsAsync(
        BlokeBotDbContext db,
        CustomCommandConfiguration config,
        IReadOnlyList<CustomCommand> existingCommands,
        IReadOnlyList<CustomAnnouncement> existingAnnouncements,
        CancellationToken ct
    )
    {
        var commandEditors = config.Commands.Where(x => x.Id > 0).ToDictionary(x => x.Id);
        foreach (var command in existingCommands)
        {
            if (
                commandEditors.TryGetValue(command.Id, out var editor)
                && !CustomCommandConfigurationMapper.ActionMatches(
                    command.Action,
                    editor.Action
                )
            )
            {
                db.CustomCommandActions.Remove(command.Action);
            }
        }

        var announcementEditors = config
            .Announcements.Where(x => x.Id > 0)
            .ToDictionary(x => x.Id);
        foreach (var announcement in existingAnnouncements)
        {
            if (
                announcementEditors.TryGetValue(announcement.Id, out var editor)
                && !CustomCommandConfigurationMapper.ScheduleMatches(
                    announcement.Schedule,
                    editor.Schedule
                )
            )
            {
                db.CustomAnnouncementSchedules.Remove(announcement.Schedule);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task DeleteRemovedDependentsAsync(
        BlokeBotDbContext db,
        CustomCommandConfiguration config,
        IReadOnlyList<CustomCommand> existingCommands,
        IReadOnlyList<CustomAnnouncement> existingAnnouncements,
        CancellationToken ct
    )
    {
        var retainedCommandIds = config.Commands.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
        var retainedAnnouncementIds = config
            .Announcements.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        db.CustomCommands.RemoveRange(existingCommands.Where(x => !retainedCommandIds.Contains(x.Id)));
        var removedAnnouncements = existingAnnouncements
            .Where(x => !retainedAnnouncementIds.Contains(x.Id))
            .ToArray();
        var removedDeliveryPolicies = removedAnnouncements
            .Select(x => x.DeliveryPolicy)
            .ToArray();
        db.CustomAnnouncements.RemoveRange(removedAnnouncements);
        await db.SaveChangesAsync(ct);
        db.CustomAnnouncementDeliveryPolicies.RemoveRange(removedDeliveryPolicies);
        await db.SaveChangesAsync(ct);
    }

    private static async Task DeleteRemovedPrincipalsAsync(
        BlokeBotDbContext db,
        CustomCommandConfiguration config,
        IReadOnlyList<CustomMessageLibraryEntry> existingMessageEntries,
        IReadOnlyList<CustomCounter> existingCounters,
        CancellationToken ct
    )
    {
        var retainedMessageEntryIds = config
            .MessageEntries.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();
        var retainedCounterIds = config.Counters.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();

        db.CustomCounters.RemoveRange(existingCounters.Where(x => !retainedCounterIds.Contains(x.Id)));
        db.CustomMessageLibraryEntries.RemoveRange(
            existingMessageEntries.Where(x => !retainedMessageEntryIds.Contains(x.Id))
        );
        await db.SaveChangesAsync(ct);
    }

    private static async Task ReplaceVariantsAsync(
        BlokeBotDbContext db,
        IReadOnlyList<CustomMessageLibraryEntryEditor> editors,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> entries,
        CancellationToken ct
    )
    {
        var entryIds = entries.Values.Select(x => x.Id).ToArray();
        var existingVariants = await db
            .CustomMessageVariants.Where(x => entryIds.Contains(x.CustomMessageLibraryEntryId))
            .ToListAsync(ct);
        db.CustomMessageVariants.RemoveRange(existingVariants);
        await db.SaveChangesAsync(ct);

        foreach (var editor in editors)
        {
            var entry = entries[editor.Id];
            for (var i = 0; i < editor.Variants.Count; i++)
            {
                db.CustomMessageVariants.Add(
                    new CustomMessageVariant
                    {
                        CustomMessageLibraryEntryId = entry.Id,
                        SortOrder = i,
                        Text = editor.Variants[i].Text.Trim(),
                    }
                );
            }
        }
    }

    private static async Task ReplaceAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyDictionary<CustomCommandEditor, CustomCommand> commands,
        IReadOnlyDictionary<CustomCommandEditor, string[]> normalizedAliases,
        CancellationToken ct
    )
    {
        var existingAliases = await db
            .CustomCommandAliases.Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        db.CustomCommandAliases.RemoveRange(existingAliases);
        await db.SaveChangesAsync(ct);

        foreach (var pair in commands)
        {
            foreach (var alias in normalizedAliases[pair.Key])
            {
                db.CustomCommandAliases.Add(
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        CustomCommandId = pair.Value.Id,
                        Alias = alias,
                    }
                );
            }
        }
    }

    private static void ApplyFinalFields(
        CustomCommandConfiguration config,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        IReadOnlyDictionary<CustomCommandEditor, CustomCommand> commands,
        IReadOnlyDictionary<CustomAnnouncementEditor, CustomAnnouncement> announcements,
        DateTime now
    )
    {
        foreach (var editor in config.MessageEntries)
        {
            var entry = messageEntries[editor.Id];
            entry.Name = CustomCommandConfigurationValidator.RequiredName(
                editor.Name,
                "Reply"
            );
            entry.SelectionMode = editor.SelectionMode;
            entry.CurrentVariantIndex = ClampVariantIndex(
                editor.CurrentVariantIndex,
                editor.Variants.Count
            );
            entry.UpdatedAtUtc = now;
        }

        foreach (var editor in config.Counters)
        {
            var counter = counters[editor.Id];
            counter.Name = CustomCommandConfigurationValidator.RequiredName(
                editor.Name,
                "Counter"
            );
            counter.Value = editor.Value;
            counter.UpdatedAtUtc = now;
        }

        foreach (var pair in commands)
        {
            pair.Value.Name = CustomCommandConfigurationValidator.RequiredName(
                pair.Key.Name,
                "Custom command"
            );
            pair.Value.UpdatedAtUtc = now;
        }

        foreach (var pair in announcements)
        {
            pair.Value.Name = CustomCommandConfigurationValidator.RequiredName(
                pair.Key.Name,
                "Announcement"
            );
            pair.Value.UpdatedAtUtc = now;
        }
    }

    private static int ClampVariantIndex(int index, int variantCount) =>
        variantCount <= 0 ? 0 : Math.Clamp(index, 0, variantCount - 1);

    private static string TemporaryName(string entityName, int editorId) =>
        $"__editing_{entityName}_{editorId}_{Guid.NewGuid():N}";
}

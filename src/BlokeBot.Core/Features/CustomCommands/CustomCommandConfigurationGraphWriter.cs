using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandConfigurationGraphWriter(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    public async Task<CustomCommandConfigurationSaveFailure?> WriteAsync(
        int hostId,
        CustomCommandConfigurationSaveCommand command,
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

        var staleEntity = FindStaleEntity(
            command,
            messageEntries,
            counters,
            commands,
            announcements
        );
        if (staleEntity is not null)
        {
            return staleEntity;
        }

        await RemoveChangedVariantsAsync(db, command, commands, announcements, ct);

        var messageEntityByEditorId = await StageMessageEntriesAsync(
            db,
            hostId,
            command.MessageEntries,
            messageEntries,
            now,
            ct
        );
        var counterEntityByEditorId = await StageCountersAsync(
            db,
            hostId,
            command.Counters,
            counters,
            now,
            ct
        );
        var commandEntityByEditor = await StageCommandsAsync(
            db,
            hostId,
            command.Commands,
            commands,
            messageEntityByEditorId,
            counterEntityByEditorId,
            now,
            ct
        );
        var announcementEntityByEditor = await StageAnnouncementsAsync(
            db,
            hostId,
            command.Announcements,
            announcements,
            messageEntityByEditorId,
            now,
            ct
        );

        await DeleteRemovedDependentsAsync(db, command, commands, announcements, ct);
        await DeleteRemovedPrincipalsAsync(db, command, messageEntries, counters, ct);
        await ReplaceVariantsAsync(db, command.MessageEntries, messageEntityByEditorId, ct);
        await ReplaceAliasesAsync(db, hostId, command.Commands, commandEntityByEditor, ct);
        ApplyFinalFields(
            command,
            messageEntityByEditorId,
            counterEntityByEditorId,
            commandEntityByEditor,
            announcementEntityByEditor,
            now
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    private static async Task<Dictionary<int, CustomMessageLibraryEntry>> StageMessageEntriesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomMessageLibraryEntryValue> editors,
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
                    : new CustomMessageLibraryEntry { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
            {
                db.CustomMessageLibraryEntries.Add(entry);
            }

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
        IReadOnlyList<CustomCounterValue> editors,
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
            {
                db.CustomCounters.Add(counter);
            }

            counter.Name = TemporaryName("counter", editor.Id);
            counter.Value = editor.Value;
            counter.UpdatedAtUtc = now;
            result[editor.Id] = counter;
        }

        await db.SaveChangesAsync(ct);
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
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<int, CustomCommand>();
        foreach (var editor in editors)
        {
            var command =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCommand { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
            {
                db.CustomCommands.Add(command);
            }

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
            result[editor.Id] = command;
        }

        await db.SaveChangesAsync(ct);
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
        var existingById = existing.ToDictionary(x => x.Id);
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
                db.CustomAnnouncements.Add(announcement);
            }

            announcement.Name = TemporaryName("announcement", editor.Id);
            announcement.Enabled = editor.Enabled;
            announcement.MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id;
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

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task RemoveChangedVariantsAsync(
        BlokeBotDbContext db,
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyList<CustomCommand> existingCommands,
        IReadOnlyList<CustomAnnouncement> existingAnnouncements,
        CancellationToken ct
    )
    {
        var configuredCommands = command.Commands.Where(x => x.Id > 0).ToDictionary(x => x.Id);
        foreach (var storedCommand in existingCommands)
        {
            if (
                configuredCommands.TryGetValue(storedCommand.Id, out var configured)
                && !CustomCommandConfigurationMapper.ActionMatches(
                    storedCommand.Action,
                    configured.Action
                )
            )
            {
                db.CustomCommandActions.Remove(storedCommand.Action);
            }
        }

        var configuredAnnouncements = command
            .Announcements.Where(x => x.Id > 0)
            .ToDictionary(x => x.Id);
        foreach (var announcement in existingAnnouncements)
        {
            if (
                configuredAnnouncements.TryGetValue(announcement.Id, out var configured)
                && !CustomCommandConfigurationMapper.ScheduleMatches(
                    announcement.Schedule,
                    configured.Schedule
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
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyList<CustomCommand> existingCommands,
        IReadOnlyList<CustomAnnouncement> existingAnnouncements,
        CancellationToken ct
    )
    {
        var retainedCommandIds = command
            .Commands.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();
        var retainedAnnouncementIds = command
            .Announcements.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        db.CustomCommands.RemoveRange(
            existingCommands.Where(x => !retainedCommandIds.Contains(x.Id))
        );
        var removedAnnouncements = existingAnnouncements
            .Where(x => !retainedAnnouncementIds.Contains(x.Id))
            .ToArray();
        var removedDeliveryPolicies = removedAnnouncements.Select(x => x.DeliveryPolicy).ToArray();
        db.CustomAnnouncements.RemoveRange(removedAnnouncements);
        await db.SaveChangesAsync(ct);
        db.CustomAnnouncementDeliveryPolicies.RemoveRange(removedDeliveryPolicies);
        await db.SaveChangesAsync(ct);
    }

    private static async Task DeleteRemovedPrincipalsAsync(
        BlokeBotDbContext db,
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyList<CustomMessageLibraryEntry> existingMessageEntries,
        IReadOnlyList<CustomCounter> existingCounters,
        CancellationToken ct
    )
    {
        var retainedMessageEntryIds = command
            .MessageEntries.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();
        var retainedCounterIds = command
            .Counters.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        db.CustomCounters.RemoveRange(
            existingCounters.Where(x => !retainedCounterIds.Contains(x.Id))
        );
        db.CustomMessageLibraryEntries.RemoveRange(
            existingMessageEntries.Where(x => !retainedMessageEntryIds.Contains(x.Id))
        );
        await db.SaveChangesAsync(ct);
    }

    private static async Task ReplaceVariantsAsync(
        BlokeBotDbContext db,
        IReadOnlyList<CustomMessageLibraryEntryValue> editors,
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
                        Text = editor.Variants[i].Text,
                    }
                );
            }
        }
    }

    private static async Task ReplaceAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCommandValue> configuredCommands,
        IReadOnlyDictionary<int, CustomCommand> commands,
        CancellationToken ct
    )
    {
        var existingAliases = await db
            .CustomCommandAliases.Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        db.CustomCommandAliases.RemoveRange(existingAliases);
        await db.SaveChangesAsync(ct);

        foreach (var configured in configuredCommands)
        {
            foreach (var alias in configured.Aliases)
            {
                db.CustomCommandAliases.Add(
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        CustomCommandId = commands[configured.Id].Id,
                        Alias = alias,
                    }
                );
            }
        }
    }

    private static void ApplyFinalFields(
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        IReadOnlyDictionary<int, CustomCommand> commands,
        IReadOnlyDictionary<int, CustomAnnouncement> announcements,
        DateTime now
    )
    {
        foreach (var configured in command.MessageEntries)
        {
            var entry = messageEntries[configured.Id];
            entry.Name = configured.Name;
            entry.SelectionMode = configured.SelectionMode;
            entry.CurrentVariantIndex = configured.CurrentVariantIndex;
            entry.UpdatedAtUtc = now;
        }

        foreach (var configured in command.Counters)
        {
            var counter = counters[configured.Id];
            counter.Name = configured.Name;
            counter.Value = configured.Value;
            counter.UpdatedAtUtc = now;
        }

        foreach (var configured in command.Commands)
        {
            var stored = commands[configured.Id];
            stored.Name = configured.Name;
            stored.UpdatedAtUtc = now;
        }

        foreach (var configured in command.Announcements)
        {
            var stored = announcements[configured.Id];
            stored.Name = configured.Name;
            stored.UpdatedAtUtc = now;
        }
    }

    private static CustomCommandConfigurationSaveFailure? FindStaleEntity(
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyList<CustomMessageLibraryEntry> messageEntries,
        IReadOnlyList<CustomCounter> counters,
        IReadOnlyList<CustomCommand> commands,
        IReadOnlyList<CustomAnnouncement> announcements
    )
    {
        if (
            HasMissingPositiveId(
                command.MessageEntries,
                messageEntries,
                configured => configured.Id,
                stored => stored.Id
            )
        )
        {
            return new CustomCommandConfigurationSaveFailure.StaleEntity("saved reply");
        }

        if (
            HasMissingPositiveId(
                command.Counters,
                counters,
                configured => configured.Id,
                stored => stored.Id
            )
        )
        {
            return new CustomCommandConfigurationSaveFailure.StaleEntity("counter");
        }

        if (
            HasMissingPositiveId(
                command.Commands,
                commands,
                configured => configured.Id,
                stored => stored.Id
            )
        )
        {
            return new CustomCommandConfigurationSaveFailure.StaleEntity("command");
        }

        return HasMissingPositiveId(
            command.Announcements,
            announcements,
            configured => configured.Id,
            stored => stored.Id
        )
            ? new CustomCommandConfigurationSaveFailure.StaleEntity("announcement")
            : null;
    }

    private static bool HasMissingPositiveId<TConfigured, TStored>(
        IEnumerable<TConfigured> configured,
        IEnumerable<TStored> stored,
        Func<TConfigured, int> configuredId,
        Func<TStored, int> storedId
    )
    {
        var storedIds = stored.Select(storedId).ToHashSet();
        return configured.Select(configuredId).Any(id => id > 0 && !storedIds.Contains(id));
    }

    private static string TemporaryName(string entityName, int editorId)
    {
        return $"__editing_{entityName}_{editorId}_{Guid.NewGuid():N}";
    }
}

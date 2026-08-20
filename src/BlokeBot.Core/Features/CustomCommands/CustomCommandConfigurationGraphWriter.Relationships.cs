using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationGraphWriter
{
    private static async Task RemoveChangedVariantsAsync(
        BlokeBotDbContext db,
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyList<CustomCommand> existingCommands,
        IReadOnlyList<CustomAnnouncement> existingAnnouncements,
        CancellationToken ct
    )
    {
        var configuredCommands = command
            .Commands.Where(static x => x.Id > 0)
            .ToDictionary(static x => x.Id);
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
                _ = db.CustomCommandActions.Remove(storedCommand.Action);
            }
        }

        var configuredAnnouncements = command
            .Announcements.Where(static x => x.Id > 0)
            .ToDictionary(static x => x.Id);
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
                _ = db.CustomAnnouncementSchedules.Remove(announcement.Schedule);
            }
        }

        _ = await db.SaveChangesAsync(ct);
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
        _ = await db.SaveChangesAsync(ct);
        db.CustomAnnouncementDeliveryPolicies.RemoveRange(removedDeliveryPolicies);
        _ = await db.SaveChangesAsync(ct);
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
        _ = await db.SaveChangesAsync(ct);
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
        _ = await db.SaveChangesAsync(ct);

        foreach (var editor in editors)
        {
            var entry = entries[editor.Id];
            for (var i = 0; i < editor.Variants.Count; i++)
            {
                _ = db.CustomMessageVariants.Add(
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
        _ = await db.SaveChangesAsync(ct);

        foreach (var configured in configuredCommands)
        {
            for (var sortOrder = 0; sortOrder < configured.Aliases.Count; sortOrder++)
            {
                _ = db.CustomCommandAliases.Add(
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        CustomCommandId = commands[configured.Id].Id,
                        Alias = configured.Aliases[sortOrder],
                        SortOrder = sortOrder,
                    }
                );
            }
        }
    }

    private static async Task ReplaceAllowedUsersAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCommandValue> configuredCommands,
        IReadOnlyDictionary<int, CustomCommand> commands,
        CancellationToken ct
    )
    {
        var existing = await db
            .CustomCommandAllowedUsers.Where(user => user.HostId == hostId)
            .ToListAsync(ct);
        db.CustomCommandAllowedUsers.RemoveRange(existing);
        _ = await db.SaveChangesAsync(ct);

        foreach (var configured in configuredCommands)
        {
            var commandId = commands[configured.Id].Id;
            db.CustomCommandAllowedUsers.AddRange(
                configured.AllowedUsers.Select(user => new CustomCommandAllowedUser
                {
                    HostId = hostId,
                    CustomCommandId = commandId,
                    TwitchUserId = user.TwitchUserId,
                    Login = user.Login,
                    DisplayName = user.DisplayName,
                })
            );
        }
    }
}

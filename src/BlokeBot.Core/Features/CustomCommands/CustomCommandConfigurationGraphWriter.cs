using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationGraphWriter(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IOverlayCueAdmissionService overlayCues,
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
        var failure = await StageAsync(db, hostId, command, ct);
        if (failure is not null)
        {
            return failure;
        }
        await transaction.CommitAsync(ct);
        return null;
    }

    internal async Task<CustomCommandConfigurationSaveFailure?> StageAsync(
        BlokeBotDbContext db,
        int hostId,
        CustomCommandConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var messageEntries = await db
            .CustomMessageLibraryEntries.Include(x => x.Variants)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var counters = await db.CustomCounters.Where(x => x.HostId == hostId).ToListAsync(ct);
        var commands = await db
            .CustomCommands.AsSplitQuery()
            .Include(x => x.Action)
            .Include(x => x.Aliases)
            .Include(x => x.AllowedUsers)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var announcements = await db
            .CustomAnnouncements.Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);

        var cueFailure = await ValidateOverlayCueReferencesAsync(hostId, command.Commands, ct);
        if (cueFailure is not null)
        {
            return cueFailure;
        }

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
        await ReplaceAllowedUsersAsync(db, hostId, command.Commands, commandEntityByEditor, ct);
        ApplyFinalFields(
            command,
            messageEntityByEditorId,
            counterEntityByEditorId,
            commandEntityByEditor,
            announcementEntityByEditor,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        return null;
    }
}

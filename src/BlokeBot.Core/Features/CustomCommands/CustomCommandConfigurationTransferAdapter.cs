using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter(
    CustomCommandConfigurationGraphWriter graphWriter,
    CustomCommandAliasRegistry aliasRegistry
)
{
    internal async Task<IReadOnlyList<ConfigurationValidationIssue>> StageAsync(
        BlokeBotDbContext db,
        int hostId,
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        var customSelection = selection.Sections.SingleOrDefault(x =>
            x.Section == ConfigurationSectionId.CustomCommands
        );
        var announcementSelection = selection.Sections.SingleOrDefault(x =>
            x.Section == ConfigurationSectionId.Announcements
        );
        if (customSelection is null && announcementSelection is null)
        {
            return [];
        }

        var draft = await LoadDraftAsync(db, hostId, cancellationToken);
        var retainedAnnouncementReplies = announcementSelection is null
            ? draft
                .MessageEntries.Where(reply =>
                    draft.Announcements.Any(announcement =>
                        announcement.MessageLibraryEntryId == reply.Id
                    )
                )
                .ToArray()
            : [];
        var nextId = -1;
        if (customSelection is not null && document.Sections.CustomCommands is { } custom)
        {
            var imported = MapCustomCommands(custom, customSelection, ref nextId);
            if (imported.Issues.Count > 0)
            {
                return imported.Issues;
            }
            var originalReplyIds = imported.Replies.ToDictionary(x => x, x => x.Id);
            var originalCounterIds = imported.Counters.ToDictionary(x => x, x => x.Id);
            draft.TimeZoneId = custom.TimeZoneId;
            draft.MessageEntries = Merge(
                draft.MessageEntries,
                imported.Replies,
                customSelection.Strategy,
                static x => x.Name,
                static x => x.Id,
                static (source, targetId) => source.Id = targetId
            );
            draft.Counters = Merge(
                draft.Counters,
                imported.Counters,
                customSelection.Strategy,
                static x => x.Name,
                static x => x.Id,
                static (source, targetId) => source.Id = targetId
            );
            RemapCommandReferences(imported.Commands, originalReplyIds, originalCounterIds);
            draft.Commands = Merge(
                draft.Commands,
                imported.Commands,
                customSelection.Strategy,
                static x => x.Name,
                static x => x.Id,
                static (source, targetId) => source.Id = targetId
            );
            foreach (var reply in retainedAnnouncementReplies)
            {
                if (draft.MessageEntries.All(x => x.Id != reply.Id))
                {
                    draft.MessageEntries.Add(reply);
                }
            }
        }
        if (
            announcementSelection is not null
            && document.Sections.Announcements is { } announcements
        )
        {
            var imported = MapAnnouncements(announcements, ref nextId);
            var originalReplyIds = imported.Replies.ToDictionary(x => x, x => x.Id);
            draft.MessageEntries = Merge(
                draft.MessageEntries,
                imported.Replies,
                ImportConflictStrategy.Merge,
                static x => x.Name,
                static x => x.Id,
                static (source, targetId) => source.Id = targetId
            );
            RemapAnnouncementReferences(imported.Announcements, originalReplyIds);
            draft.Announcements = Merge(
                draft.Announcements,
                imported.Announcements,
                announcementSelection.Strategy,
                static x => x.Name,
                static x => x.Id,
                static (source, targetId) => source.Id = targetId
            );
        }

        var aliasConflict = await aliasRegistry.FindConflictAsync(
            db,
            hostId,
            draft.Commands.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet(),
            draft
                .Commands.SelectMany(x => BlokeBot.Commands.CommandAliasNormalizer.Split(x.Aliases))
                .ToArray(),
            cancellationToken
        );
        if (aliasConflict is not null)
        {
            var alias = aliasConflict.Match(x => x.Alias, x => x.Alias);
            return
            [
                new(
                    "sections.customCommands.commands.aliases",
                    $"!{alias} is already used by another command."
                ),
            ];
        }

        return await CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                async command =>
                {
                    var failure = await graphWriter.StageAsync(
                        db,
                        hostId,
                        command,
                        cancellationToken
                    );
                    return failure is null
                        ? []
                        :
                        [
                            new ConfigurationValidationIssue(
                                "sections.customCommands",
                                failure.Message
                            ),
                        ];
                },
                errors =>
                    Task.FromResult<IReadOnlyList<ConfigurationValidationIssue>>(
                        errors
                            .Select(error => new ConfigurationValidationIssue(
                                "sections.customCommands",
                                error.Message
                            ))
                            .ToArray()
                    )
            );
    }

    private static async Task<CustomCommandConfiguration> LoadDraftAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var replies = await db
            .CustomMessageLibraryEntries.Include(x => x.Variants)
            .Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var counters = await db
            .CustomCounters.Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var commands = await db
            .CustomCommands.AsSplitQuery()
            .Include(x => x.Action)
            .Include(x => x.Aliases)
            .Include(x => x.AllowedUsers)
            .Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var announcements = await db
            .CustomAnnouncements.Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var timeZone = await db
            .Hosts.Where(x => x.Id == hostId)
            .Select(x => x.TimeZoneId)
            .SingleAsync(cancellationToken);
        return new()
        {
            TimeZoneId = timeZone,
            MessageEntries = replies.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Counters = counters.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Commands = commands.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Announcements = announcements
                .Select(CustomCommandConfigurationMapper.ToEditor)
                .ToList(),
        };
    }

    private sealed record ImportedCustomCommands(
        List<CustomMessageLibraryEntryEditor> Replies,
        List<CustomCounterEditor> Counters,
        List<CustomCommandEditor> Commands,
        IReadOnlyList<ConfigurationValidationIssue> Issues
    );

    private sealed record ImportedAnnouncements(
        List<CustomMessageLibraryEntryEditor> Replies,
        List<CustomAnnouncementEditor> Announcements
    );
}

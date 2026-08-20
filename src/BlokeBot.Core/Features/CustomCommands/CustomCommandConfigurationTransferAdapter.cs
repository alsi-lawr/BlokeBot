using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter(
    CustomCommandConfigurationGraphWriter graphWriter,
    CustomCommandAliasRegistry aliasRegistry,
    TimeProvider timeProvider
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
        var retainedAnnouncementReplies =
            announcementSelection?.Strategy != ImportConflictStrategy.ReplaceSection
                ? draft
                    .MessageEntries.Where(reply =>
                        draft.Announcements.Any(announcement =>
                            announcement.MessageLibraryEntryId == reply.Id
                        )
                    )
                    .ToArray()
                : [];
        var retainedCommands =
            customSelection?.Strategy == ImportConflictStrategy.ReplaceSection
            && document.Sections.CustomCommands is { } replacement
                ? LoadRetainedCommandsByName(
                        draft.Commands,
                        replacement
                            .Commands.Where(command =>
                                customSelection.ItemResolutions.Any(resolution =>
                                    resolution.ImportedId == command.Id
                                    && resolution.Resolution == ImportConflictResolution.Skip
                                )
                            )
                            .Select(command => command.Name)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    )
                    .ToArray()
                : [];
        var retainedCommandReplyIds = retainedCommands
            .SelectMany(command =>
                new[]
                {
                    command.Action.ReplyRoutes.ZeroArgumentMessageLibraryEntryId,
                    command.Action.ReplyRoutes.OneArgumentMessageLibraryEntryId,
                    command.Action.ReplyRoutes.TwoArgumentMessageLibraryEntryId,
                }
            )
            .OfType<int>()
            .ToHashSet();
        var retainedCommandCounterIds = retainedCommands
            .Select(command => command.Action)
            .OfType<CounterCustomCommandActionEditor>()
            .Select(action => action.CounterId)
            .ToHashSet();
        var retainedCommandReplies = draft
            .MessageEntries.Where(reply => retainedCommandReplyIds.Contains(reply.Id))
            .ToArray();
        var retainedCommandCounters = draft
            .Counters.Where(counter => retainedCommandCounterIds.Contains(counter.Id))
            .ToArray();
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
            if (customSelection.Strategy != ImportConflictStrategy.AddMissing)
            {
                var host = await db.Hosts.SingleAsync(x => x.Id == hostId, cancellationToken);
                host.TimeZoneId = custom.TimeZoneId;
            }
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
            if (customSelection.Strategy == ImportConflictStrategy.ReplaceSection)
            {
                foreach (var retained in retainedCommands)
                {
                    if (draft.Commands.All(x => x.Id != retained.Id))
                    {
                        draft.Commands.Add(retained);
                    }
                }
            }
            foreach (var reply in retainedAnnouncementReplies.Concat(retainedCommandReplies))
            {
                if (draft.MessageEntries.All(x => x.Id != reply.Id))
                {
                    draft.MessageEntries.Add(reply);
                }
            }
            foreach (var counter in retainedCommandCounters)
            {
                if (draft.Counters.All(x => x.Id != counter.Id))
                {
                    draft.Counters.Add(counter);
                }
            }
        }
        if (
            announcementSelection is not null
            && document.Sections.Announcements is { } announcements
        )
        {
            var selectedAnnouncements =
                announcementSelection.Strategy == ImportConflictStrategy.AddMissing
                    ? SelectMissingAnnouncements(draft, announcements)
                    : announcements;
            var imported = MapAnnouncements(
                selectedAnnouncements,
                TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId),
                draft.ProjectionReferenceUtc,
                ref nextId
            );
            var originalReplyIds = imported.Replies.ToDictionary(x => x, x => x.Id);
            draft.MessageEntries = Merge(
                draft.MessageEntries,
                imported.Replies,
                announcementSelection.Strategy == ImportConflictStrategy.AddMissing
                    ? ImportConflictStrategy.AddMissing
                    : ImportConflictStrategy.Merge,
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

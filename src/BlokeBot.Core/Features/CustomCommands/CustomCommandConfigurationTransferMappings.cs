using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter
{
    private static ImportedCustomCommands MapCustomCommands(
        CustomCommandsSectionV1 section,
        SectionImportSelection selection,
        ref int nextId
    )
    {
        var firstReplyId = nextId;
        var replyIds = section
            .Replies.Select((reply, index) => (reply.Id, Value: firstReplyId - index))
            .ToDictionary(x => x.Id, x => x.Value, StringComparer.Ordinal);
        nextId -= section.Replies.Count;
        var firstCounterId = nextId;
        var counterIds = section
            .Counters.Select((counter, index) => (counter.Id, Value: firstCounterId - index))
            .ToDictionary(x => x.Id, x => x.Value, StringComparer.Ordinal);
        nextId -= section.Counters.Count;
        var resolutions = selection.ItemResolutions.ToDictionary(
            x => x.ImportedId,
            StringComparer.Ordinal
        );
        var issues = new List<ConfigurationValidationIssue>();
        if (resolutions.Values.Any(x => x.Resolution == ImportConflictResolution.Abort))
        {
            issues.Add(
                new("sections.customCommands", "The import was aborted by a conflict decision.")
            );
        }
        var commands = new List<CustomCommandEditor>();
        foreach (var command in section.Commands)
        {
            if (
                command.Action.Type
                is CustomCommandActionTypeV1.Automation
                    or CustomCommandActionTypeV1.OverlayCue
            )
            {
                if (
                    resolutions.GetValueOrDefault(command.Id)?.Resolution
                    == ImportConflictResolution.Skip
                )
                {
                    continue;
                }

                issues.Add(
                    new(
                        $"sections.customCommands.commands[{command.Id}].action",
                        $"!{command.Aliases.FirstOrDefault() ?? command.Name} uses an unsupported {command.Action.Type} dependency. Skip the whole command or abort."
                    )
                );
                continue;
            }
            var mapped = MapCommand(command, replyIds, counterIds, nextId--);
            var selectedAliases = new List<string>(command.Aliases.Count);
            foreach (var alias in command.Aliases)
            {
                var resolution = resolutions.GetValueOrDefault(
                    ConfigurationConflictIds.CustomCommandAlias(command.Id, alias)
                );
                if (resolution?.Resolution == ImportConflictResolution.Skip)
                {
                    continue;
                }

                selectedAliases.Add(
                    resolution
                        is {
                            Resolution: ImportConflictResolution.Rename,
                            ReplacementName: { Length: > 0 } renamed
                        }
                        ? renamed
                        : alias
                );
            }
            mapped.Aliases = string.Join(", ", selectedAliases);
            commands.Add(mapped);
        }
        return new(
            section.Replies.Select(x => MapReply(x, replyIds[x.Id])).ToList(),
            section
                .Counters.Select(x => new CustomCounterEditor
                {
                    Id = counterIds[x.Id],
                    Name = x.Name,
                    Value = x.Value,
                })
                .ToList(),
            commands,
            issues
        );
    }

    private static ImportedAnnouncements MapAnnouncements(
        AnnouncementsSectionV1 section,
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
                            firstAnnouncementId - index
                        )
                )
                .ToList()
        );
    }

    private static CustomMessageLibraryEntryEditor MapReply(MessageEntryV1 value, int id) =>
        new()
        {
            Id = id,
            Name = value.Name,
            SelectionMode = value.SelectionMode,
            CurrentVariantIndex = 0,
            Variants = value
                .Variants.Select(text => new CustomMessageVariantEditor { Text = text })
                .ToList(),
        };

    private static CustomCommandEditor MapCommand(
        CustomCommandV1 value,
        IReadOnlyDictionary<string, int> replies,
        IReadOnlyDictionary<string, int> counters,
        int id
    )
    {
        var editor = new CustomCommandEditor
        {
            Id = id,
            Name = value.Name,
            Aliases = string.Join(", ", value.Aliases),
            Enabled = value.Enabled,
            AllowEveryone = value.AllowEveryone,
            AllowModerators = value.AllowModerators,
            AllowedUsers = value
                .AllowedUsers.Select(x => new CustomCommandAllowedUserEditor(
                    x.TwitchUserId,
                    x.Login,
                    x.DisplayName
                ))
                .ToList(),
            CooldownSeconds = value.CooldownSeconds,
            CooldownScope = value.CooldownScope,
            InvocationLimit = value.InvocationLimit,
            Action =
                value.Action.Type == CustomCommandActionTypeV1.Counter
                    ? new CounterCustomCommandActionEditor
                    {
                        CounterId = counters[value.Action.CounterId!],
                    }
                    : new MessageCustomCommandActionEditor(),
        };
        editor.Action.ReplyRoutes = new()
        {
            ZeroArgumentMessageLibraryEntryId = Resolve(value.Action.ZeroArgumentReplyId, replies),
            OneArgumentMessageLibraryEntryId = Resolve(value.Action.OneArgumentReplyId, replies),
            TwoArgumentMessageLibraryEntryId = Resolve(value.Action.TwoArgumentReplyId, replies),
        };
        return editor;
    }

    private static void RemapCommandReferences(
        IEnumerable<CustomCommandEditor> commands,
        IReadOnlyDictionary<CustomMessageLibraryEntryEditor, int> originalReplyIds,
        IReadOnlyDictionary<CustomCounterEditor, int> originalCounterIds
    )
    {
        var replyIds = originalReplyIds.ToDictionary(x => x.Value, x => x.Key.Id);
        var counterIds = originalCounterIds.ToDictionary(x => x.Value, x => x.Key.Id);
        foreach (var command in commands)
        {
            var routes = command.Action.ReplyRoutes;
            routes.ZeroArgumentMessageLibraryEntryId = Remap(
                routes.ZeroArgumentMessageLibraryEntryId,
                replyIds
            );
            routes.OneArgumentMessageLibraryEntryId = Remap(
                routes.OneArgumentMessageLibraryEntryId,
                replyIds
            );
            routes.TwoArgumentMessageLibraryEntryId = Remap(
                routes.TwoArgumentMessageLibraryEntryId,
                replyIds
            );
            if (command.Action is CounterCustomCommandActionEditor counter)
            {
                counter.CounterId = counterIds.GetValueOrDefault(
                    counter.CounterId,
                    counter.CounterId
                );
            }
        }
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

    private static int? Remap(int? id, IReadOnlyDictionary<int, int> ids) =>
        id is { } value ? ids.GetValueOrDefault(value, value) : null;

    private static CustomAnnouncementEditor MapAnnouncement(
        AnnouncementV1 value,
        int replyId,
        int id
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
                AnnouncementScheduleTypeV1.Weekly => new WeeklyCustomAnnouncementScheduleEditor
                {
                    Day = value.Schedule.Day ?? DayOfWeek.Monday,
                    Time = value.Schedule.Time ?? default,
                },
                _ => throw new InvalidOperationException("Unsupported announcement schedule."),
            },
        };

    private static List<T> Merge<T>(
        List<T> current,
        List<T> imported,
        ImportConflictStrategy strategy,
        Func<T, string> name,
        Func<T, int> id,
        Action<T, int> preserveId
    )
        where T : class
    {
        var existing = current.ToDictionary(name, StringComparer.OrdinalIgnoreCase);
        var selected = strategy == ImportConflictStrategy.ReplaceSection ? [] : current.ToList();
        foreach (var item in imported)
        {
            if (existing.TryGetValue(name(item), out var match))
            {
                if (strategy == ImportConflictStrategy.AddMissing)
                {
                    continue;
                }

                preserveId(item, id(match));
                _ = selected.Remove(match);
            }
            selected.Add(item);
        }
        return selected;
    }

    private static int? Resolve(string? id, IReadOnlyDictionary<string, int> ids) =>
        id is null ? null : ids[id];
}

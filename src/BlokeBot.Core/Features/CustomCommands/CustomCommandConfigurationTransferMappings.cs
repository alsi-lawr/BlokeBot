using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter
{
    private static ImportedCustomCommands MapCustomCommands(
        CustomCommandsSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan referencePlan,
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
            if (ConfigurationConflictIds.SkipsCustomCommand(command, selection.ItemResolutions))
            {
                continue;
            }
            if (
                command.Action.Type == CustomCommandActionTypeV1.OverlayCue
                && (
                    command.Action.OverlayTargetId is null
                    || command.Action.OverlayCueId is null
                    || !referencePlan.OverlayInstances.ContainsKey(command.Action.OverlayTargetId)
                    || !referencePlan.OverlayCues.ContainsKey(command.Action.OverlayCueId)
                )
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
                        $"!{command.Aliases.FirstOrDefault() ?? command.Name} has an unresolved Overlay dependency. Skip the whole command or abort."
                    )
                );
                continue;
            }
            var mapped = MapCommand(command, replyIds, counterIds, referencePlan, nextId--);
            var selectedAliases = new List<string>(command.Aliases.Count);
            foreach (var alias in command.Aliases)
            {
                selectedAliases.Add(
                    ConfigurationConflictIds.SelectedCustomCommandAlias(
                        command.Id,
                        alias,
                        selection.ItemResolutions
                    )
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
        ConfigurationImportReferencePlan referencePlan,
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
            AllowedUsers = [],
            CooldownSeconds = value.CooldownSeconds,
            CooldownScope = value.CooldownScope,
            InvocationLimit = value.InvocationLimit,
            Action = value.Action.Type switch
            {
                CustomCommandActionTypeV1.Counter => new CounterCustomCommandActionEditor
                {
                    CounterId = counters[value.Action.CounterId!],
                },
                CustomCommandActionTypeV1.Automation => new AutomationCustomCommandActionEditor(),
                CustomCommandActionTypeV1.OverlayCue => new OverlayCueCustomCommandActionEditor
                {
                    TargetOverlayPublicId = referencePlan.OverlayInstances[
                        value.Action.OverlayTargetId!
                    ],
                    CuePublicId = referencePlan.OverlayCues[value.Action.OverlayCueId!],
                    QueuePolicy = value.Action.OverlayQueuePolicy!.Value,
                    ReplyOrder = value.Action.OverlayReplyOrder!.Value,
                },
                _ => new MessageCustomCommandActionEditor(),
            },
        };
        editor.Action.ReplyRoutes = new()
        {
            ZeroArgumentMessageLibraryEntryId = Resolve(value.Action.ZeroArgumentReplyId, replies),
            OneArgumentMessageLibraryEntryId = Resolve(value.Action.OneArgumentReplyId, replies),
            TwoArgumentMessageLibraryEntryId = Resolve(value.Action.TwoArgumentReplyId, replies),
        };
        return editor;
    }
}

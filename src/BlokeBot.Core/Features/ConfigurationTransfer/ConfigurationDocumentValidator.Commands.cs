using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    private static ConfigurationValidationIssue? ValidateCommands(CustomCommandsSectionV1? section)
    {
        if (section is null)
        {
            return null;
        }

        var issue =
            Limit("sections.customCommands.replies", section.Replies.Count)
            ?? Limit("sections.customCommands.counters", section.Counters.Count)
            ?? Limit("sections.customCommands.commands", section.Commands.Count)
            ?? DuplicateIds("sections.customCommands.replies", section.Replies.Select(x => x.Id))
            ?? DuplicateIds("sections.customCommands.counters", section.Counters.Select(x => x.Id))
            ?? DuplicateIds("sections.customCommands.commands", section.Commands.Select(x => x.Id));
        if (issue is not null)
        {
            return issue;
        }

        var replies = section.Replies.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var counters = section.Counters.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var reply in section.Replies)
        {
            if (
                Limit(
                    $"sections.customCommands.replies[{reply.Id}].variants",
                    reply.Variants.Count
                ) is
                { } limit
            )
            {
                return limit;
            }
        }
        foreach (var command in section.Commands)
        {
            var path = $"sections.customCommands.commands[{command.Id}]";
            if (Limit($"{path}.aliases", command.Aliases.Count) is { } aliasLimit)
            {
                return aliasLimit;
            }

            foreach (var reference in ReplyReferences(command.Action))
            {
                if (reference is not null && !replies.Contains(reference))
                {
                    return new(
                        $"{path}.action",
                        $"Reply reference '{reference}' was not exported with this section."
                    );
                }
            }
            if (command.Action.Type == CustomCommandActionTypeV1.Counter)
            {
                if (
                    command.Action.CounterId is null
                    || !counters.Contains(command.Action.CounterId)
                )
                {
                    return new(
                        $"{path}.action.counterId",
                        "The counter action does not reference an exported counter."
                    );
                }
            }
            if (
                command.Action.Type == CustomCommandActionTypeV1.OverlayCue
                && (
                    string.IsNullOrWhiteSpace(command.Action.OverlayTargetId)
                    || string.IsNullOrWhiteSpace(command.Action.OverlayTargetName)
                    || string.IsNullOrWhiteSpace(command.Action.OverlayCueId)
                    || string.IsNullOrWhiteSpace(command.Action.OverlayCueName)
                    || command.Action.OverlayQueuePolicy is null
                    || command.Action.OverlayReplyOrder is null
                )
            )
            {
                return new(
                    $"{path}.action",
                    "An overlay cue action requires its target, cue, queue policy, and reply order."
                );
            }
        }
        return null;
    }

    private static IEnumerable<string?> ReplyReferences(CustomCommandActionV1 action)
    {
        yield return action.ZeroArgumentReplyId;
        yield return action.OneArgumentReplyId;
        yield return action.TwoArgumentReplyId;
    }
}

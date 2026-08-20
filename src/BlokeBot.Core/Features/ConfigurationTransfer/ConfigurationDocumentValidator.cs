using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Guessing.Profiles;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class ConfigurationDocumentValidator
{
    public static ConfigurationValidationIssue? Validate(ConfigurationDocumentV1 document) =>
        !string.Equals(document.Format, ConfigurationDocumentCodec.Format, StringComparison.Ordinal)
            ? new("format", $"Expected format '{ConfigurationDocumentCodec.Format}'.")
        : document.Version != ConfigurationDocumentCodec.CurrentVersion
            ? new("version", $"Format version {document.Version} is not supported.")
        : ValidateCommands(document.Sections.CustomCommands)
            ?? ValidateAnnouncements(document.Sections.Announcements)
            ?? ValidateGuessing(document.Sections.Guessing)
            ?? ValidatePoints(document.Sections.Points);

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

            if (Limit($"{path}.allowedUsers", command.AllowedUsers.Count) is { } userLimit)
            {
                return userLimit;
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
        }
        return null;
    }

    private static ConfigurationValidationIssue? ValidateAnnouncements(
        AnnouncementsSectionV1? section
    )
    {
        if (section is null)
        {
            return null;
        }

        var issue =
            Limit("sections.announcements.replies", section.Replies.Count)
            ?? Limit("sections.announcements.items", section.Items.Count)
            ?? DuplicateIds("sections.announcements.replies", section.Replies.Select(x => x.Id))
            ?? DuplicateIds("sections.announcements.items", section.Items.Select(x => x.Id));
        if (issue is not null)
        {
            return issue;
        }

        var replies = section.Replies.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var reply in section.Replies)
        {
            if (
                Limit(
                    $"sections.announcements.replies[{reply.Id}].variants",
                    reply.Variants.Count
                ) is
                { } limit
            )
            {
                return limit;
            }
        }
        var missing = section.Items.FirstOrDefault(x => !replies.Contains(x.MessageReplyId));
        return missing is null
            ? null
            : new(
                $"sections.announcements.items[{missing.Id}].messageReplyId",
                "The announcement reply was not exported with this section."
            );
    }

    private static ConfigurationValidationIssue? ValidateGuessing(GuessingSectionV1? section)
    {
        if (section is null)
        {
            return null;
        }

        var issue =
            Limit("sections.guessing.profiles", section.Profiles.Count)
            ?? DuplicateIds("sections.guessing.profiles", section.Profiles.Select(x => x.Id))
            ?? DuplicateIds("sections.guessing.profiles", section.Profiles.Select(x => x.Slug));
        if (issue is not null)
        {
            return issue;
        }

        foreach (var profile in section.Profiles)
        {
            var path = $"sections.guessing.profiles[{profile.Id}]";
            if (
                !string.Equals(
                    profile.Slug,
                    GuessRoundProfileSlug.FromName(profile.Name).Value,
                    StringComparison.Ordinal
                )
            )
            {
                return new(
                    $"{path}.slug",
                    "The profile slug is not the canonical slug for its name."
                );
            }
            if (Limit($"{path}.commandAliases", profile.CommandAliases.Count) is { } aliasGroups)
            {
                return aliasGroups;
            }

            if (Limit($"{path}.options", profile.Options.Count) is { } options)
            {
                return options;
            }

            foreach (var aliases in profile.CommandAliases)
            {
                if (
                    Limit($"{path}.commandAliases[{aliases.Command}]", aliases.Aliases.Count) is
                    { } aliasesLimit
                )
                {
                    return aliasesLimit;
                }
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? ValidatePoints(PointsSectionV1? section)
    {
        if (section is null)
        {
            return null;
        }

        if (Limit("sections.points.commandAliases", section.CommandAliases.Count) is { } groups)
        {
            return groups;
        }

        foreach (var aliases in section.CommandAliases)
        {
            if (
                Limit(
                    $"sections.points.commandAliases[{aliases.Command}]",
                    aliases.Aliases.Count
                ) is
                { } aliasesLimit
            )
            {
                return aliasesLimit;
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? DuplicateIds(
        string location,
        IEnumerable<string> ids
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicate = ids.FirstOrDefault(id => string.IsNullOrWhiteSpace(id) || !seen.Add(id));
        return duplicate is null
            ? null
            : new(
                location,
                string.IsNullOrWhiteSpace(duplicate)
                    ? "Export-local identifiers must not be empty."
                    : $"Export-local identifier '{duplicate}' is duplicated."
            );
    }

    private static ConfigurationValidationIssue? Limit(string location, int count) =>
        count <= ConfigurationDocumentCodec.MaximumRecordsPerCollection
            ? null
            : new(
                location,
                $"This collection exceeds the {ConfigurationDocumentCodec.MaximumRecordsPerCollection} record limit."
            );

    private static IEnumerable<string?> ReplyReferences(CustomCommandActionV1 action)
    {
        yield return action.ZeroArgumentReplyId;
        yield return action.OneArgumentReplyId;
        yield return action.TwoArgumentReplyId;
    }
}

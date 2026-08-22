using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Guessing.Profiles;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    public static ConfigurationValidationIssue? Validate(ConfigurationDocumentV1 document) =>
        !string.Equals(document.Format, ConfigurationDocumentCodec.Format, StringComparison.Ordinal)
            ? new("format", $"Expected format '{ConfigurationDocumentCodec.Format}'.")
        : document.Version != ConfigurationDocumentCodec.CurrentVersion
            ? new("version", $"Format version {document.Version} is not supported.")
        : string.IsNullOrWhiteSpace(document.Source.ChannelLogin)
            ? new("source.channelLogin", "The source channel login is required.")
        : document.ExportedAtUtc.Offset != TimeSpan.Zero
            ? new("exportedAtUtc", "The export timestamp must use UTC offset +00:00.")
        : ValidateCommands(document.Sections.CustomCommands)
            ?? ValidateAnnouncements(document.Sections.Announcements)
            ?? ValidateGuessing(document.Sections.Guessing)
            ?? ValidatePoints(document.Sections.Points)
            ?? ValidateOverlays(document.Sections.Overlays)
            ?? ValidateAutomations(document.Sections.Automations);

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
        foreach (var item in section.Items)
        {
            if (ValidateAnnouncementSchedule(item) is { } scheduleIssue)
            {
                return scheduleIssue;
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

    private static ConfigurationValidationIssue? ValidateAnnouncementSchedule(AnnouncementV1 item)
    {
        var path = $"sections.announcements.items[{item.Id}].schedule";
        return item.Schedule.Type switch
        {
            AnnouncementScheduleTypeV1.Interval when item.Schedule.IntervalMinutes is null => new(
                $"{path}.intervalMinutes",
                "Interval minutes are required for this schedule."
            ),
            AnnouncementScheduleTypeV1.IntervalAfterChat
                when item.Schedule.IntervalMinutes is null => new(
                $"{path}.intervalMinutes",
                "Interval minutes are required for this schedule."
            ),
            AnnouncementScheduleTypeV1.IntervalAfterChat
                when item.Schedule.RequiredChatMessages is null => new(
                $"{path}.requiredChatMessages",
                "Required chat messages are required for this schedule."
            ),
            AnnouncementScheduleTypeV1.Weekly when item.Schedule.Day is null => new(
                $"{path}.day",
                "UTC weekday is required for a weekly schedule."
            ),
            AnnouncementScheduleTypeV1.Weekly when item.Schedule.Time is null => new(
                $"{path}.time",
                "UTC time is required for a weekly schedule."
            ),
            _ => null,
        };
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
}

using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationTransferCoordinator
{
    private static string AuditSummary(
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        IReadOnlyCollection<ConfigurationSectionId> changedSections
    )
    {
        var sections = selection
            .Sections.Where(x => changedSections.Contains(x.Section))
            .Select(x => new AuditSection(CanonicalId(x.Section), Count(document, x.Section)))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(new AuditSummaryV1(sections));
    }

    private static int Count(ConfigurationDocumentV1 document, ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => document.Sections.CustomCommands is { } custom
                ? custom.Replies.Count + custom.Counters.Count + custom.Commands.Count + 1
                : 0,
            ConfigurationSectionId.Announcements => document.Sections.Announcements?.Items.Count
                ?? 0,
            ConfigurationSectionId.Guessing => document.Sections.Guessing?.Profiles.Count ?? 0,
            ConfigurationSectionId.Points => document.Sections.Points is null ? 0 : 1,
            ConfigurationSectionId.ChannelToolEnablement => document.Sections.ChannelToolEnablement
                is null
                ? 0
                : 1,
            ConfigurationSectionId.Overlays => document.Sections.Overlays is { } overlays
                ? overlays.Instances.Count + overlays.MediaReferences.Count + overlays.Cues.Count
                : 0,
            ConfigurationSectionId.Automations => document.Sections.Automations?.Flows.Count ?? 0,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };

    private static ConfigurationValidationIssue? ValidateSelection(
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection
    )
    {
        if (ConfigurationDocumentValidator.Validate(document) is { } documentIssue)
        {
            return documentIssue;
        }

        if (selection.Sections.Count == 0)
        {
            return new("sections", "Choose at least one section to import.");
        }

        if (
            selection.Sections.Select(x => x.Section).Distinct().Count() != selection.Sections.Count
        )
        {
            return new("sections", "A section can be selected only once.");
        }
        var missing = selection.Sections.FirstOrDefault(x =>
            Count(document, x.Section) == 0 && !SectionPresent(document, x.Section)
        );
        return missing is not null
                ? new(
                    $"sections.{CanonicalId(missing.Section)}",
                    "The selected section is not present in the file."
                )
            : selection.EnablementChanges.Any(feature => !HostFeatureCatalog.IsSelectable(feature))
                ? new(
                    "sections.channelToolEnablement",
                    "An unsupported enablement flag was selected."
                )
            : selection.EnablementChanges.Count > 0
            && Selected(selection, ConfigurationSectionId.ChannelToolEnablement) is null
                ? new(
                    "sections.channelToolEnablement",
                    "Select the enablement section before selecting feature changes."
                )
            : null;
    }

    private static bool SectionPresent(
        ConfigurationDocumentV1 document,
        ConfigurationSectionId section
    ) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => document.Sections.CustomCommands is not null,
            ConfigurationSectionId.Announcements => document.Sections.Announcements is not null,
            ConfigurationSectionId.Guessing => document.Sections.Guessing is not null,
            ConfigurationSectionId.Points => document.Sections.Points is not null,
            ConfigurationSectionId.ChannelToolEnablement => document.Sections.ChannelToolEnablement
                is not null,
            ConfigurationSectionId.Overlays => document.Sections.Overlays is not null,
            ConfigurationSectionId.Automations => document.Sections.Automations is not null,
            _ => false,
        };

    private static string CanonicalId(ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => "customCommands",
            ConfigurationSectionId.Announcements => "announcements",
            ConfigurationSectionId.Guessing => "guessing",
            ConfigurationSectionId.Points => "points",
            ConfigurationSectionId.ChannelToolEnablement => "channelToolEnablement",
            ConfigurationSectionId.Overlays => "overlays",
            ConfigurationSectionId.Automations => "automations",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };

    private static SectionImportSelection? Selected(
        ConfigurationImportSelection selection,
        ConfigurationSectionId section
    ) => selection.Sections.SingleOrDefault(x => x.Section == section);

    private static bool SelectedHostMatches(AuthenticatedSession session, int hostId) =>
        session.State.Match(
            _ => false,
            selected => selected.Selection.Current.Id == hostId,
            _ => false
        );

    private sealed record AuditSummaryV1(IReadOnlyList<AuditSection> Sections);

    private sealed record AuditSection(string Id, int Count);
}

using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService
{
    internal async Task<ConfigurationSectionPreview> PreviewSectionAsync(
        BlokeBotDbContext db,
        BotHost host,
        ConfigurationDocumentV1 document,
        SectionImportSelection selection,
        ConfigurationImportSelection importSelection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var preview = selection.Section switch
        {
            ConfigurationSectionId.CustomCommands => await PreviewCustomCommandsAsync(
                db,
                host.Id,
                document.Sections.CustomCommands,
                selection,
                importSelection,
                references,
                cancellationToken
            ),
            ConfigurationSectionId.Announcements => await PreviewNamesAsync(
                db.CustomAnnouncements.Where(x => x.HostId == host.Id).Select(x => x.Name),
                document.Sections.Announcements?.Items.Select(x => x.Name),
                selection
            ),
            ConfigurationSectionId.Guessing => await PreviewGuessingAsync(
                db,
                host.Id,
                document.Sections.Guessing,
                selection,
                cancellationToken
            ),
            ConfigurationSectionId.Points => await PreviewPointsAsync(
                db,
                host.Id,
                document.Sections.Points,
                selection,
                cancellationToken
            ),
            ConfigurationSectionId.ChannelToolEnablement => new(
                selection.Section,
                new(0, 0, 0, 0),
                [],
                []
            ),
            ConfigurationSectionId.Overlays => await _overlays.PreviewAsync(
                db,
                host,
                document.Sections.Overlays,
                selection,
                references,
                cancellationToken
            ),
            ConfigurationSectionId.Automations => await _automations.PreviewAsync(
                db,
                host,
                document.Sections.Automations,
                selection,
                references,
                cancellationToken
            ),
            _ => new(
                selection.Section,
                new(0, 0, 0, 0),
                [new("sections", "Unsupported section.")],
                []
            ),
        };

        return
            RequiredFeature(selection.Section) is { } feature
            && !host.EnabledFeatures.Contains(feature)
            && HasChanges(preview.Counts)
            ? preview with
            {
                Issues =
                [
                    .. preview.Issues,
                    new(
                        $"sections.{CanonicalId(selection.Section)}",
                        $"{FeatureName(feature)} is off for this channel. Enable {FeatureName(feature)} in Channel setup before the imported configuration can be used.",
                        BlocksApply: false
                    ),
                ],
            }
            : preview;
    }

    private static bool HasChanges(ConfigurationPreviewCount counts) =>
        counts.Add > 0 || counts.Update > 0 || counts.Remove > 0;

    private static HostFeatureFlags? RequiredFeature(ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands or ConfigurationSectionId.Announcements =>
                HostFeatureFlags.CustomCommands,
            ConfigurationSectionId.Guessing => HostFeatureFlags.Guessing,
            ConfigurationSectionId.Points => HostFeatureFlags.Points,
            ConfigurationSectionId.Overlays => HostFeatureFlags.Overlays,
            ConfigurationSectionId.Automations => HostFeatureFlags.Automations,
            ConfigurationSectionId.ChannelToolEnablement => null,
            _ => null,
        };

    private static string FeatureName(HostFeatureFlags feature) =>
        feature switch
        {
            HostFeatureFlags.CustomCommands => "Custom commands",
            HostFeatureFlags.Guessing => "Guessing game",
            HostFeatureFlags.Points => "Points",
            HostFeatureFlags.Overlays => "Overlays",
            HostFeatureFlags.Automations => "Automations",
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
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
            _ => "sections",
        };
}

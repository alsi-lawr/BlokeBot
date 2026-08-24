using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandConfiguration
{
    public string TimeZoneId { get; set; } = "UTC";

    public DateTimeOffset ProjectionReferenceUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<CustomMessageLibraryEntryEditor> MessageEntries { get; set; } = [];

    public List<CustomCommandEditor> Commands { get; set; } = [];

    public List<CustomCounterEditor> Counters { get; set; } = [];

    public List<CustomAnnouncementEditor> Announcements { get; set; } = [];

    public IReadOnlySet<string> BuiltInAliases { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public TwitchAnnouncementReadiness TwitchAnnouncementReadiness { get; set; } =
        new(TwitchAnnouncementAvailability.Unavailable, string.Empty);

    public CustomCommandAlertSummary AlertSummary { get; set; } = new();
}

public sealed class CustomMessageLibraryEntryEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public CustomMessageSelectionMode SelectionMode { get; set; } =
        CustomMessageSelectionMode.Sequential;

    public int CurrentVariantIndex { get; set; }

    public List<CustomMessageVariantEditor> Variants { get; set; } = [];
}

public sealed class CustomMessageVariantEditor
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

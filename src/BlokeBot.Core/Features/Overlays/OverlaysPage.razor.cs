using BlokeBot.Core.Components.Layout;

namespace BlokeBot.Core.Features.Overlays;

public partial class OverlaysPage
{
    private static readonly IReadOnlyList<SegmentedTabItem> _tabs =
    [
        new("sources", "Sources"),
        new("cues", "Cues"),
        new("media", "Media"),
    ];

    private readonly HashSet<string> _visited = [];
    private string _activeKey = "sources";

    private string _pageTitle =>
        _activeKey switch
        {
            "cues" => "BlokeBot | Overlay cues",
            "media" => "BlokeBot | Media library",
            _ => "BlokeBot | Overlays",
        };

    private string _title =>
        _activeKey switch
        {
            "cues" => "Cues",
            "media" => "Media library",
            _ => "Overlays",
        };

    private string _description =>
        _activeKey switch
        {
            "cues" => "Build reusable scenes and try them on a Cue player Browser Source.",
            "media" => "Upload and manage the media used by cues.",
            _ => "Create Browser Sources, choose how they look in OBS, and check delivery.",
        };

    protected override void OnInitialized() =>
        ActivateTab(SegmentedTabs.CanonicalKey(_navigation, _tabs));

    private void ActivateTab(string key)
    {
        _activeKey = key;
        _ = _visited.Add(key);
    }
}

using BlokeBot.Core.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays;

public partial class OverlaySectionTabs
{
    private static readonly IReadOnlyList<SegmentedTabItem> _tabs =
    [
        new("sources", "Sources", "/overlays/sources"),
        new("cues", "Cues", "/overlays/cues"),
        new("media", "Media", "/overlays/media"),
    ];
}

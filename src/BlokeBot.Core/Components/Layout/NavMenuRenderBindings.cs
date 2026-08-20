using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Components.Layout;

public sealed class NavMenuRouteBindings(
    Func<string, bool, string?> currentRoute,
    Func<string, bool> groupIsCurrent,
    Func<string, string?> currentGroup,
    Func<string, string?> routeHelpReference,
    Func<string, string, RenderFragment> routeHelp,
    Func<Task> notifyNavigatedAsync
)
{
    public string? CurrentRoute(string route, bool exact = false) => currentRoute(route, exact);

    public bool GroupIsCurrent(string route) => groupIsCurrent(route);

    public string? CurrentGroup(string route) => currentGroup(route);

    public string? RouteHelpReference(string routeKey) => routeHelpReference(routeKey);

    public RenderFragment RouteHelp(string routeKey, string label) => routeHelp(routeKey, label);

    public Task NotifyNavigatedAsync() => notifyNavigatedAsync();
}

public sealed record NavMenuGroupBindings(
    bool IsOpen,
    string BodyId,
    EventCallback ToggleAsync,
    EventCallback<KeyboardEventArgs> CloseOnEscape
);

public sealed record NavMenuPrimaryVisibility(
    bool ShowAlerts,
    bool ShowHostConfig,
    bool ShowConfigurationTransfer,
    bool ShowChatTools
);

public sealed record NavMenuNativeTwitchVisibility(
    bool ShowPolls,
    bool ShowClipsMarkers,
    bool ShowRewardsRedemptions,
    bool ShowPredictions,
    int DestinationCount
);

public sealed record NavMenuDirectFeatureVisibility(
    bool ShowRaidCollaboration,
    bool ShowCollectives,
    bool ShowRequestBoards,
    bool ShowBounties,
    bool ShowCommunityProgression,
    bool ShowCooperativeGame,
    bool ShowViewerPassports,
    bool ShowBingo,
    bool ShowCompetitions,
    bool ShowPlayQueues,
    bool ShowMoments,
    bool ShowOverlays
);

public sealed record NavMenuFeatureGroupVisibility(bool Visible, NavMenuGroupBindings Group);

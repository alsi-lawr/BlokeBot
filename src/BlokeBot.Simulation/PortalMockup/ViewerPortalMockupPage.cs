namespace BlokeBot.Simulation.PortalMockup;

/// <summary>A whole-page state of the viewer portal mockup.</summary>
public enum ViewerPortalMockupState
{
    Populated,
    Sparse,
    NotFound,
    NoPublicFeatures,
    Loading,
    PartialFailure,
}

/// <summary>The identity presentation of the viewer portal mockup.</summary>
public enum ViewerPortalMockupViewer
{
    Anonymous,
    Authenticated,
    Unavailable,
}

/// <summary>The projection state of one feature entry.</summary>
public enum ViewerPortalMockupFeatureState
{
    Active,
    Quiet,
    Loading,
    Failed,
}

/// <summary>The channel identity shown in the portal header.</summary>
public sealed record ViewerPortalMockupChannel(
    string Login,
    string DisplayName,
    bool IsLive,
    string LiveLine
);

/// <summary>One enabled public feature as the portal projects it.</summary>
public sealed record ViewerPortalMockupFeature(
    string Key,
    string Label,
    string Href,
    ViewerPortalMockupFeatureState State,
    string Status,
    string Tone,
    string Headline,
    string Detail,
    string Action
);

/// <summary>One self-scoped item in the personal section.</summary>
public sealed record ViewerPortalMockupPersonalItem(
    string Label,
    string Value,
    string Detail,
    string Href,
    string LinkText,
    int? Rank
);

/// <summary>One public event in the recent-activity list.</summary>
public sealed record ViewerPortalMockupEvent(string When, string Feature, string Text);

/// <summary>The signed-in viewer identity.</summary>
public sealed record ViewerPortalMockupIdentity(string DisplayName, string Login);

/// <summary>Everything the mockup renders for one requested state.</summary>
public sealed record ViewerPortalMockupPage(
    ViewerPortalMockupState State,
    ViewerPortalMockupViewer Viewer,
    string Theme,
    string RequestedLogin,
    ViewerPortalMockupChannel? Channel,
    ViewerPortalMockupIdentity? Identity,
    IReadOnlyList<ViewerPortalMockupFeature> Features,
    IReadOnlyList<ViewerPortalMockupPersonalItem> Personal,
    IReadOnlyList<ViewerPortalMockupEvent> Recent
)
{
    public string PortalPath => $"/channel/{RequestedLogin}";

    public string Title =>
        Channel is null ? "Channel not found | BlokeBot" : $"{Channel.DisplayName} | BlokeBot";
}

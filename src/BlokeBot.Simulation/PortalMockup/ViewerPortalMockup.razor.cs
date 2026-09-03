using Microsoft.AspNetCore.Components;

namespace BlokeBot.Simulation.PortalMockup;

public partial class ViewerPortalMockup
{
    private const int _highlightLimit = 4;

    [Parameter, EditorRequired]
    public required ViewerPortalMockupPage Page { get; set; }

    private IReadOnlyList<ViewerPortalMockupFeature> _highlighted =>
        [
            .. Page
                .Features.Where(feature =>
                    feature.State
                        is ViewerPortalMockupFeatureState.Active
                            or ViewerPortalMockupFeatureState.Failed
                )
                .Take(_highlightLimit),
        ];

    private string _signInHref =>
        $"/auth/login?start=true&returnUrl={Uri.EscapeDataString(Page.PortalPath)}";

    private string _signOutHref =>
        $"/auth/logout?returnUrl={Uri.EscapeDataString(Page.PortalPath)}";

    private static string PillClass(string tone) => $"status-pill status-pill--{tone}";

    private static string Initial(string value) =>
        value.Length == 0 ? "?" : char.ToUpperInvariant(value[0]).ToString();

    private static string Initials(string value) =>
        string.Concat(
            value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0]))
        );
}

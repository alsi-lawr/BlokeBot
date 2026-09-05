using BlokeBot.Core.Auth.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Features.ViewerPassports;

public partial class PublicViewerPassportPage
{
    [Inject]
    private BlokeBot.Core.Features.ViewerPortal.Boundary.PublicViewerGate _publicGate { get; set; } =
        null!;

    [CascadingParameter]
    private Task<AuthenticationState> _authenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    [Parameter]
    public string Viewer { get; set; } = string.Empty;

    private ViewerPassportView? _passport;
    private bool _loaded;
    private bool _featureDisabled;

    protected override async Task OnParametersSetAsync()
    {
        _passport = null;
        if (!await _publicGate.TryReadAsync(Channel, CancellationToken.None))
        {
            _loaded = true;
            return;
        }
        _loaded = false;
        var context = await _pageContexts.FromAsync(_authenticationState);
        var manager = context.Session.AvailableHosts.Any(host =>
            string.Equals(host.Login, Channel, StringComparison.OrdinalIgnoreCase)
            && host.Role is AuthRole.Streamer or AuthRole.Admin or AuthRole.Moderator
        );
        var outcome = await _passports.GetVisibleAsync(
            Channel,
            Viewer,
            new(context.Session.IsAuthenticated ? context.Session.UserId : null, manager),
            CancellationToken.None
        );
        _featureDisabled = outcome is ViewerPassportQueryOutcome.FeatureDisabled;
        _passport = outcome is ViewerPassportQueryOutcome.Available available
            ? available.Passport
            : null;
        _loaded = true;
    }

    private static string Initials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static RenderFragment Stat(string value, string label) =>
        builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "rounded-xl bg-[var(--app-surface-muted)] p-3");
            builder.OpenElement(2, "b");
            builder.AddAttribute(3, "class", "block text-lg text-[var(--app-text-strong)]");
            builder.AddContent(4, value);
            builder.CloseElement();
            builder.OpenElement(5, "span");
            builder.AddAttribute(6, "class", "text-xs text-muted-foreground");
            builder.AddContent(7, label);
            builder.CloseElement();
            builder.CloseElement();
        };
}

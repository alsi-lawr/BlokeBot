using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Layout;

public partial class MainLayout
{
    private const string _desktopRailId = "desktop-navigation-rail";
    private const string _mobileDrawerId = "mobile-navigation-drawer";
    private const string _mobileMenuButtonId = "mobile-navigation-menu-button";
    private static readonly IReadOnlyDictionary<string, object> _inertAttributes = new Dictionary<
        string,
        object
    >
    {
        ["inert"] = true,
    };
    private static readonly IReadOnlyDictionary<string, object> _open_mobileDrawerAttributes =
        new Dictionary<string, object> { ["role"] = "dialog", ["aria-modal"] = "true" };
    private static readonly IReadOnlyDictionary<string, object> _closed_mobileDrawerAttributes =
        new Dictionary<string, object> { ["aria-hidden"] = "true", ["inert"] = true };

    private DotNetObjectReference<MainLayout>? _mobileNavigationReference;
    private IJSObjectReference? _module;
    private MobileNavigationEffect? _pendingMobileNavigationEffect;
    private bool _mobileNavigationOpen;
    private NavigationPresentation _railPresentation = NavigationPresentation.Expanded;

    private IReadOnlyDictionary<string, object>? _backgroundAttributes =>
        _mobileNavigationOpen ? _inertAttributes : null;

    private IReadOnlyDictionary<string, object> _mobileDrawerAttributes =>
        _mobileNavigationOpen ? _open_mobileDrawerAttributes : _closed_mobileDrawerAttributes;

    private string _mobileDrawerClass =>
        _mobileNavigationOpen
            ? "app-shell__mobile-drawer app-shell__mobile-drawer--open lg:hidden"
            : "app-shell__mobile-drawer lg:hidden";

    private string _mobileDrawerOverlayClass =>
        _mobileNavigationOpen
            ? "app-shell__drawer-overlay app-shell__drawer-overlay--open lg:hidden"
            : "app-shell__drawer-overlay lg:hidden";

    private void CloseMobileNavigation()
    {
        if (!_mobileNavigationOpen)
        {
            return;
        }

        _mobileNavigationOpen = false;
        _pendingMobileNavigationEffect = MobileNavigationEffect.DeactivateAndRestoreMenuButton;
    }

    private void OpenMobileNavigation()
    {
        if (_mobileNavigationOpen)
        {
            return;
        }

        _mobileNavigationOpen = true;
        _pendingMobileNavigationEffect = MobileNavigationEffect.Activate;
    }

    private async Task ToggleRailAsync()
    {
        _railPresentation =
            _railPresentation is NavigationPresentation.Expanded
                ? NavigationPresentation.IconRail
                : NavigationPresentation.Expanded;

        if (_module is not null)
        {
            await _module.InvokeVoidAsync(
                "writeRailPresentation",
                _railPresentation is NavigationPresentation.IconRail
            );
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _module = await _js.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Components/Layout/MainLayout.razor.js"
                );
                var iconRail = await _module.InvokeAsync<bool>("readRailPresentation");
                _railPresentation = iconRail
                    ? NavigationPresentation.IconRail
                    : NavigationPresentation.Expanded;
                await InvokeAsync(StateHasChanged);
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (TaskCanceledException) { }
        }

        var effect = _pendingMobileNavigationEffect;
        _pendingMobileNavigationEffect = null;

        if (effect is MobileNavigationEffect.Activate)
        {
            _mobileNavigationReference ??= DotNetObjectReference.Create(this);
            await _js.InvokeVoidAsync(
                "blokeBotNavigation.activateMobileDrawer",
                _mobileDrawerId,
                _mobileNavigationReference
            );
        }

        if (effect is MobileNavigationEffect.DeactivateAndRestoreMenuButton)
        {
            await _js.InvokeVoidAsync("blokeBotNavigation.deactivateMobileDrawer");
            await _js.InvokeVoidAsync("blokeBotNavigation.focus", _mobileMenuButtonId);
        }
    }

    [JSInvokable]
    public async Task CloseMobileNavigationFromKeyboardAsync()
    {
        CloseMobileNavigation();
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_mobileNavigationReference is not null)
        {
            try
            {
                await _js.InvokeVoidAsync("blokeBotNavigation.deactivateMobileDrawer");
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }

            _mobileNavigationReference.Dispose();
        }

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }
    }

    private enum MobileNavigationEffect
    {
        Activate,
        DeactivateAndRestoreMenuButton,
    }
}

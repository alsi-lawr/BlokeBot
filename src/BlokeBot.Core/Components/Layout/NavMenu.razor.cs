using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Layout;

public partial class NavMenu
{
    private const string _guessingOpenStorageKey = "blokebot.sidebar.guessing.open";
    private const string _pointsOpenStorageKey = "blokebot.sidebar.points.open";
    private const string _customCommandsOpenStorageKey = "blokebot.sidebar.customcommands.open";

    private readonly string _rootId = $"navigation-{Guid.NewGuid():N}";
    private NavigationGroup? _iconRailOpenGroup;
    private bool _guessingOpen = true;
    private bool _pointsOpen = true;
    private bool _customCommandsOpen = true;
    private bool _routeHelpActive;
    private IDisposable? _hostedChannelSubscription;
    private IReadOnlyDictionary<int, HostFeatureFlags> _hostedFeatures =
        new Dictionary<int, HostFeatureFlags>();
    private IReadOnlySet<int> _existingHostIds = new HashSet<int>();
    private IJSObjectReference? _module;

    [Parameter]
    public EventCallback OnNavigate { get; set; }

    [Parameter]
    public NavigationPresentation Presentation { get; set; } = NavigationPresentation.Expanded;

    protected override async Task OnInitializedAsync()
    {
        _navigation.LocationChanged += HandleLocationChanged;
        _hostedChannelSubscription = _events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            InvokeAsync,
            LoadHostedFeaturesAsync,
            StateHasChanged
        );
        await LoadHostedFeaturesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _module = await _js.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Components/Layout/NavMenu.razor.js"
                );
                _guessingOpen = await _module.InvokeAsync<bool>(
                    "readBoolean",
                    _guessingOpenStorageKey,
                    true
                );
                _pointsOpen = await _module.InvokeAsync<bool>(
                    "readBoolean",
                    _pointsOpenStorageKey,
                    true
                );
                _customCommandsOpen = await _module.InvokeAsync<bool>(
                    "readBoolean",
                    _customCommandsOpenStorageKey,
                    true
                );
                await InvokeAsync(StateHasChanged);
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (TaskCanceledException) { }
        }

        if (_module is null)
        {
            return;
        }

        if (Presentation is NavigationPresentation.IconRail && !_routeHelpActive)
        {
            await _module.InvokeVoidAsync("activateRouteHelp", _rootId);
            _routeHelpActive = true;
        }
        else if (Presentation is NavigationPresentation.Expanded && _routeHelpActive)
        {
            await _module.InvokeVoidAsync("deactivateRouteHelp", _rootId);
            _routeHelpActive = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _navigation.LocationChanged -= HandleLocationChanged;
        _hostedChannelSubscription?.Dispose();

        if (_module is null)
        {
            return;
        }

        try
        {
            if (_routeHelpActive)
            {
                await _module.InvokeVoidAsync("deactivateRouteHelp", _rootId);
            }

            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private string? CurrentRoute(string route, bool exact = false)
    {
        return RouteIsCurrent(route, exact) ? "page" : null;
    }

    private bool GroupIsCurrent(string route)
    {
        return RouteIsCurrent(route, exact: false);
    }

    private string? CurrentGroup(string route)
    {
        return GroupIsCurrent(route) ? "page" : null;
    }

    private bool GroupIsOpen(NavigationGroup group)
    {
        return Presentation switch
        {
            NavigationPresentation.Expanded => group switch
            {
                NavigationGroup.Guessing => _guessingOpen,
                NavigationGroup.Points => _pointsOpen,
                NavigationGroup.CustomCommands => _customCommandsOpen,
                _ => throw new UnreachableException(),
            },
            NavigationPresentation.IconRail => _iconRailOpenGroup == group,
            _ => throw new UnreachableException(),
        };
    }

    private string GroupBodyId(NavigationGroup group)
    {
        return $"{_rootId}-{group.ToString().ToLowerInvariant()}-destinations";
    }

    private string? RouteHelpReference(string routeKey)
    {
        return Presentation is NavigationPresentation.IconRail ? RouteHelpId(routeKey) : null;
    }

    private string RouteHelpId(string routeKey)
    {
        return $"{_rootId}-{routeKey}-help";
    }

    private RenderFragment RouteHelp(string routeKey, string label)
    {
        return builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "id", RouteHelpId(routeKey));
            builder.AddAttribute(2, "class", "nav-menu__route-help");
            builder.AddAttribute(3, "role", "tooltip");
            builder.AddContent(4, label);
            builder.CloseElement();
        };
    }

    private async Task ToggleGroupAsync(NavigationGroup group)
    {
        if (Presentation is NavigationPresentation.IconRail)
        {
            _iconRailOpenGroup = _iconRailOpenGroup == group ? null : group;
            return;
        }

        switch (group)
        {
            case NavigationGroup.Guessing:
                _guessingOpen = !_guessingOpen;
                await PersistGroupAsync(_guessingOpenStorageKey, _guessingOpen);
                break;
            case NavigationGroup.Points:
                _pointsOpen = !_pointsOpen;
                await PersistGroupAsync(_pointsOpenStorageKey, _pointsOpen);
                break;
            case NavigationGroup.CustomCommands:
                _customCommandsOpen = !_customCommandsOpen;
                await PersistGroupAsync(_customCommandsOpenStorageKey, _customCommandsOpen);
                break;
            default:
                throw new UnreachableException();
        }
    }

    private void CloseGroupOnEscape(NavigationGroup group, KeyboardEventArgs args)
    {
        if (
            Presentation is NavigationPresentation.IconRail
            && _iconRailOpenGroup == group
            && args.Key == "Escape"
        )
        {
            _iconRailOpenGroup = null;
        }
    }

    private async Task PersistGroupAsync(string storageKey, bool open)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("writeBoolean", storageKey, open);
        }
    }

    private async Task NotifyNavigatedAsync()
    {
        _iconRailOpenGroup = null;
        if (OnNavigate.HasDelegate)
        {
            await OnNavigate.InvokeAsync();
        }
    }

    private bool RouteIsCurrent(string route, bool exact)
    {
        var path = _navigation.ToBaseRelativePath(_navigation.Uri);
        var suffixIndex = path.IndexOfAny(['?', '#']);
        if (suffixIndex >= 0)
        {
            path = path[..suffixIndex];
        }

        path = path.Trim('/');
        route = route.Trim('/');
        return string.Equals(path, route, StringComparison.OrdinalIgnoreCase)
            || (
                !exact
                && route.Length > 0
                && path.StartsWith($"{route}/", StringComparison.OrdinalIgnoreCase)
            );
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _iconRailOpenGroup = null;
        _ = InvokeAsync(StateHasChanged);
    }

    private bool FeatureIsVisible(
        AuthenticatedSession session,
        BotHostSelection? selection,
        HostFeatureFlags feature
    )
    {
        if (!session.CanUseBotFunctions(_existingHostIds) || selection is null)
        {
            return false;
        }

        return _hostedFeatures.TryGetValue(selection.Current.Id, out var features)
            && features.Contains(feature);
    }

    private async Task LoadHostedFeaturesAsync()
    {
        _hostedFeatures = await _features.LoadHostedFeaturesAsync(CancellationToken.None);
        _existingHostIds = _hostedFeatures.Keys.ToHashSet();
    }

    private enum NavigationGroup
    {
        Guessing,
        Points,
        CustomCommands,
    }
}

using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Components.Layout;

public partial class NavMenu
{
    private const string _guessingOpenStorageKey = "blokebot.sidebar.guessing.open";
    private const string _pointsOpenStorageKey = "blokebot.sidebar.points.open";
    private const string _customCommandsOpenStorageKey = "blokebot.sidebar.customcommands.open";

    private bool _guessingOpen = true;
    private bool _pointsOpen = true;
    private bool _customCommandsOpen = true;
    private IDisposable? _hostedChannelSubscription;
    private IReadOnlyDictionary<int, HostFeatureFlags> _hostedFeatures =
        new Dictionary<int, HostFeatureFlags>();
    private IReadOnlySet<int> _existingHostIds = new HashSet<int>();
    private IJSObjectReference? _module;

    [Parameter]
    public EventCallback OnNavigate { get; set; }

    protected override async Task OnInitializedAsync()
    {
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
        if (!firstRender)
        {
            return;
        }

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
            _pointsOpen = await _module.InvokeAsync<bool>("readBoolean", _pointsOpenStorageKey, true);
            _customCommandsOpen = await _module.InvokeAsync<bool>(
                "readBoolean",
                _customCommandsOpenStorageKey,
                true
            );
            StateHasChanged();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _hostedChannelSubscription?.Dispose();

        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task ToggleGuessingAsync()
    {
        _guessingOpen = !_guessingOpen;
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("writeBoolean", _guessingOpenStorageKey, _guessingOpen);
        }
    }

    private async Task TogglePointsAsync()
    {
        _pointsOpen = !_pointsOpen;
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("writeBoolean", _pointsOpenStorageKey, _pointsOpen);
        }
    }

    private async Task ToggleCustomCommandsAsync()
    {
        _customCommandsOpen = !_customCommandsOpen;
        if (_module is not null)
        {
            await _module.InvokeVoidAsync(
                "writeBoolean",
                _customCommandsOpenStorageKey,
                _customCommandsOpen
            );
        }
    }

    private Task NotifyNavigatedAsync()
    {
        return OnNavigate.HasDelegate ? OnNavigate.InvokeAsync() : Task.CompletedTask;
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
}

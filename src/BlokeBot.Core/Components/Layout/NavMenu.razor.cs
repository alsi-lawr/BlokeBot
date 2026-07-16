using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Core.Components.Layout;

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

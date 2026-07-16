using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Alerts;
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

public partial class TopBarControls : IDisposable
{
    private IDisposable? _alertsSubscription;
    private int _activeAlertCount;

    [CascadingParameter]
    private Task<AuthenticationState> _authenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Inject]
    private DurableAlertService _alerts { get; set; } = default!;

    [Inject]
    private EventBus<AppEventKind> _events { get; set; } = default!;

    [Inject]
    private NavigationManager _navigation { get; set; } = default!;

    private bool _showAlertIndicator => _activeAlertCount > 0;

    private string _alertButtonLabel =>
        _activeAlertCount == 1 ? "1 active alert" : $"{_activeAlertCount} active alerts";

    protected override async Task OnInitializedAsync()
    {
        _alertsSubscription = _events.SubscribeForComponentRefresh(
            AppEventKind.AlertsChanged,
            InvokeAsync,
            LoadAlertCountAsync,
            StateHasChanged
        );
        await LoadAlertCountAsync();
    }

    public void Dispose()
    {
        _alertsSubscription?.Dispose();
    }

    private async Task LoadAlertCountAsync()
    {
        var pageContext = await _pageContext.FromAsync(_authenticationState);
        var selectedHost = pageContext.Session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        _activeAlertCount =
            selectedHost is null || pageContext.IsBotAccount
                ? 0
                : await _alerts.CountActiveAsync(selectedHost.Id, CancellationToken.None);
    }

    private void OpenAlerts()
    {
        if (_showAlertIndicator)
        {
            _navigation.NavigateTo("/alerts");
        }
    }

    private static bool ShowsHostSelector(AuthenticatedSession session)
    {
        return !session.IsAdminEditing && !session.IsBotAccount;
    }

    private static string ControlsGridClass(
        BotHostSelection? selection,
        bool isAdminEditing,
        bool isBotAccount,
        bool showHostSelector,
        bool showAlertIndicator
    )
    {
        var alertClass = showAlertIndicator ? " topbar-controls--with-alert" : string.Empty;
        if (isBotAccount)
        {
            return $"topbar-controls topbar-controls--account-only{alertClass}";
        }

        if (selection is null)
        {
            return showHostSelector
                ? $"topbar-controls topbar-controls--selector-account{alertClass}"
                : $"topbar-controls topbar-controls--account-only{alertClass}";
        }

        if (isAdminEditing || !showHostSelector)
        {
            return $"topbar-controls topbar-controls--status-account{alertClass}";
        }

        return $"topbar-controls topbar-controls--status-selector-account{alertClass}";
    }
}

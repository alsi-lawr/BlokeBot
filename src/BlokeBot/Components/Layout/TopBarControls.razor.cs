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
using BlokeBot.Features.Alerts;
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

public partial class TopBarControls : IDisposable
{
    private IDisposable? alertsSubscription;
    private int activeAlertCount;

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Inject]
    private DurableAlertService Alerts { get; set; } = default!;

    [Inject]
    private EventBus<AppEventKind> Events { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private bool ShowAlertIndicator => activeAlertCount > 0;

    private string AlertButtonLabel =>
        activeAlertCount == 1 ? "1 active alert" : $"{activeAlertCount} active alerts";

    protected override async Task OnInitializedAsync()
    {
        alertsSubscription = Events.SubscribeForComponentRefresh(
            AppEventKind.AlertsChanged,
            work => InvokeAsync(work),
            LoadAlertCountAsync,
            StateHasChanged
        );
        await LoadAlertCountAsync();
    }

    public void Dispose()
    {
        alertsSubscription?.Dispose();
    }

    private async Task LoadAlertCountAsync()
    {
        var pageContext = await PageContext.FromAsync(AuthenticationState);
        activeAlertCount =
            pageContext.SelectedHost is null || pageContext.IsBotAccount
                ? 0
                : await Alerts.CountActiveAsync(
                    pageContext.SelectedHost.Id,
                    CancellationToken.None
                );
    }

    private void OpenAlerts()
    {
        if (ShowAlertIndicator)
            Navigation.NavigateTo("/alerts");
    }

    private static bool ShowsHostSelector(AuthenticatedSession session) =>
        !session.IsAdminEditing && !session.IsBotAccount;

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
            return $"topbar-controls topbar-controls--account-only{alertClass}";

        if (selection is null)
        {
            return showHostSelector
                ? $"topbar-controls topbar-controls--selector-account{alertClass}"
                : $"topbar-controls topbar-controls--account-only{alertClass}";
        }

        if (isAdminEditing || !showHostSelector)
            return $"topbar-controls topbar-controls--status-account{alertClass}";

        return $"topbar-controls topbar-controls--status-selector-account{alertClass}";
    }
}

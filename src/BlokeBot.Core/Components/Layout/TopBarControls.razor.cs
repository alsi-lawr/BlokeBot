using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

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

    public void Dispose() => _alertsSubscription?.Dispose();

    private async Task LoadAlertCountAsync()
    {
        var pageContext = await _pageContext.FromAsync(_authenticationState);
        var selectedHost = pageContext.Session.State.Match<BotHostChoice?>(
            static _ => null,
            static selected => selected.Selection.Current,
            static _ => null
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

    private static bool ShowsHostSelector(AuthenticatedSession session) =>
        !session.IsAdminEditing && !session.IsBotAccount;
}

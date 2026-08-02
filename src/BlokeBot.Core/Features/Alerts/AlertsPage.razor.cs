using BlokeBot.Core.Components;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Alerts;

public partial class AlertsPage
{
    private DurableAlertState? _state;
    private bool _canAcknowledge;
    private bool _loadFailed;

    private string _activeSummary =>
        _state?.ActiveCount switch
        {
            null => "Loading active alerts.",
            0 => "No active alerts for this channel.",
            1 => "1 active alert for this channel.",
            var count => $"{count} active alerts for this channel.",
        };

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.AlertsChanged,
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task AcknowledgeAsync(DurableAlertItem alert)
    {
        await LoadPageContextAsync();
        _canAcknowledge = DurableAlertPermissions.CanAcknowledge(PageContext.Session);
        if (HostId == 0 || !_canAcknowledge)
        {
            return;
        }

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                await _alerts
                    .Acknowledge(HostId, alert.Id, ActorLogin)
                    .ExecuteAsync(CancellationToken.None);
                await LoadAsync();
            }
        );
    }

    private async Task LoadAsync()
    {
        _loadFailed = false;
        _state = null;

        try
        {
            await LoadPageContextAsync();
            _canAcknowledge = DurableAlertPermissions.CanAcknowledge(PageContext.Session);
            _state =
                HostId == 0 ? null : await _alerts.LoadStateAsync(HostId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _canAcknowledge = false;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
    }

    private Task RefreshAsync() => LoadAsync();

    private async Task RetryLoadAsync()
    {
        _loadFailed = false;
        await InvokeAsync(StateHasChanged);
        await LoadAsync();
    }

    private static string FormatTimestamp(DateTime? value) =>
        value is null ? "n/a" : value.Value.ToLocalTime().ToString("MMM d, yyyy HH:mm");

    private static string AlertAreaLabel(string source) =>
        source switch
        {
            "twitch-outbound-queue" => "Chat messages",
            _ => "BlokeBot",
        };

    private static string ImportanceLabel(DurableAlertSeverity severity) =>
        severity switch
        {
            DurableAlertSeverity.Critical => "Urgent",
            DurableAlertSeverity.Warning => "Warning",
            _ => "Information",
        };

    private static string SeverityBadgeClass(DurableAlertSeverity severity) =>
        severity switch
        {
            DurableAlertSeverity.Critical =>
                "inline-flex rounded-full border border-rose-200 bg-rose-50 px-2.5 py-1 text-xs font-bold text-rose-700",
            DurableAlertSeverity.Warning =>
                "inline-flex rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-bold text-amber-700",
            _ =>
                "inline-flex rounded-full border border-sky-200 bg-sky-50 px-2.5 py-1 text-xs font-bold text-sky-700",
        };
}

using BlokeBot.Components;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Alerts;

public partial class AlertsPage
{
    private DurableAlertState? state;
    private bool canAcknowledge;

    private string ActiveSummary =>
        state?.ActiveCount switch
        {
            null => "Loading active alerts.",
            0 => "No active alerts for this channel.",
            1 => "1 active alert for this channel.",
            var count => $"{count} active alerts for this channel.",
        };

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                AppEventKind.AlertsChanged,
                work => InvokeAsync(work),
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task AcknowledgeAsync(DurableAlertItem alert)
    {
        await LoadPageContextAsync();
        canAcknowledge = DurableAlertPermissions.CanAcknowledge(PageContext.Session);
        if (HostId == 0 || !canAcknowledge)
            return;

        await Alerts.AcknowledgeAsync(HostId, alert.Id, ActorLogin, CancellationToken.None);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadPageContextAsync();
        canAcknowledge = DurableAlertPermissions.CanAcknowledge(PageContext.Session);
        state =
            HostId == 0
                ? null
                : await Alerts.LoadStateAsync(HostId, CancellationToken.None);
    }

    private Task RefreshAsync() => LoadAsync();

    private static string FormatTimestamp(DateTime? value) =>
        value is null ? "n/a" : value.Value.ToLocalTime().ToString("MMM d, yyyy HH:mm");

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

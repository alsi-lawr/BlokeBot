using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.Dashboard;

public partial class PointsDashboard
{
    private bool _featureEnabled;
    private PointsDashboardState? _state;
    private PointBalanceEntry? _lookupResult;
    private string _lookupLogin = string.Empty;
    private string _giveFrom = string.Empty;
    private string _giveTo = string.Empty;
    private string _giveAmount = string.Empty;
    private string _addLogin = string.Empty;
    private string _addAmount = string.Empty;
    private string _removeLogin = string.Empty;
    private string _removeAmount = string.Empty;

    private string _giveawaySummary =>
        _state?.ActiveGiveaway is null
            ? "No giveaway running."
            : $"Runs until {_state.ActiveGiveaway.EndsAtUtc.ToLocalTime():HH:mm}. {_state.ActiveGiveaway.Entrants.Length} people joined.";

    internal static string LedgerChangeLabel(PointLedgerKind kind) =>
        kind switch
        {
            PointLedgerKind.Add => "Points added",
            PointLedgerKind.Remove => "Points removed",
            PointLedgerKind.DeleteBalance => "Balance deleted",
            PointLedgerKind.TransferOut => "Points given",
            PointLedgerKind.TransferIn => "Points received",
            PointLedgerKind.GambleWin => "Gamble won",
            PointLedgerKind.GambleLoss => "Gamble lost",
            PointLedgerKind.GiveawayWin => "Giveaway prize",
            PointLedgerKind.GuessWin => "Guessing prize",
            PointLedgerKind.RequestReservation => "Request reserved",
            PointLedgerKind.RequestRefund => "Request refunded",
            _ => throw new UnreachableException("Unknown point ledger kind."),
        };

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.PointsChanged, AppEventKind.HostedChannelsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    private Task AddAsync() =>
        RunAsync(() =>
            _dashboard.AddAsync(HostId, _addLogin, _addAmount, ActorLogin, CancellationToken.None)
        );

    private Task CancelGiveawayAsync() =>
        RunAsync(() => _dashboard.CancelGiveawayAsync(HostId, CancellationToken.None));

    private Task EndGiveawayAsync() =>
        RunAsync(() => _dashboard.EndGiveawayAsync(HostId, HostLogin, CancellationToken.None));

    private Task GiveAsync() =>
        RunAsync(() =>
            _dashboard.GiveAsync(HostId, _giveFrom, _giveTo, _giveAmount, CancellationToken.None)
        );

    private async Task LoadAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        await LoadFeatureStateAsync();
        _state = _featureEnabled
            ? await _dashboard.LoadAsync(HostId, CancellationToken.None)
            : null;
    }

    private async Task LookupAsync()
    {
        if (HostId == 0 || !_featureEnabled || string.IsNullOrWhiteSpace(_lookupLogin))
        {
            return;
        }

        _lookupResult = await _dashboard.LookupAsync(HostId, _lookupLogin, CancellationToken.None);
    }

    private async Task RefreshAsync() => await LoadAsync();

    private Task RemoveAsync() =>
        RunAsync(() =>
            _dashboard.RemoveAsync(
                HostId,
                _removeLogin,
                _removeAmount,
                ActorLogin,
                CancellationToken.None
            )
        );

    private Task RemoveLeaderboardEntryAsync(string login) =>
        RunAsync(() =>
            _dashboard.RemoveBalanceAsync(HostId, login, ActorLogin, CancellationToken.None)
        );

    private async Task RunAsync(Func<Task<PointOperationOutcome>> operation)
    {
        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            return;
        }

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await operation();
                PublishResult(result);
                await LoadAsync();
            }
        );
    }

    private Task StartGiveawayAsync() =>
        RunAsync(() => _dashboard.StartGiveawayAsync(HostId, HostLogin, CancellationToken.None));

    private async Task LoadFeatureStateAsync() =>
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Points,
                CancellationToken.None
            );

    private void PublishResult(PointOperationOutcome outcome) =>
        _ = outcome.Match(
            succeeded =>
            {
                if (!string.IsNullOrWhiteSpace(succeeded.Message))
                {
                    _ = _toasts.Publish(new ToastRequest<SuccessToastStrategy>(succeeded.Message));
                }

                return true;
            },
            failed =>
            {
                if (!string.IsNullOrWhiteSpace(failed.Message))
                {
                    _ = _toasts.Publish(new ToastRequest<WarningToastStrategy>(failed.Message));
                }

                return true;
            }
        );
}

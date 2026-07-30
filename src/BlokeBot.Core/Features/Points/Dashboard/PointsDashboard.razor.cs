using System.Diagnostics;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

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

    internal static string LedgerChangeLabel(PointLedgerKind kind)
    {
        return kind switch
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
            _ => throw new UnreachableException("Unknown point ledger kind."),
        };
    }

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.PointsChanged, AppEventKind.HostedChannelsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadPageContextAsync();
        await LoadAsync();
    }

    private Task AddAsync()
    {
        return RunAsync(() =>
            _dashboard.AddAsync(HostId, _addLogin, _addAmount, ActorLogin, CancellationToken.None)
        );
    }

    private Task CancelGiveawayAsync()
    {
        return RunAsync(() => _dashboard.CancelGiveawayAsync(HostId, CancellationToken.None));
    }

    private Task EndGiveawayAsync()
    {
        return RunAsync(() =>
            _dashboard.EndGiveawayAsync(HostId, HostLogin, CancellationToken.None)
        );
    }

    private Task GiveAsync()
    {
        return RunAsync(() =>
            _dashboard.GiveAsync(HostId, _giveFrom, _giveTo, _giveAmount, CancellationToken.None)
        );
    }

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

    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private Task RemoveAsync()
    {
        return RunAsync(() =>
            _dashboard.RemoveAsync(
                HostId,
                _removeLogin,
                _removeAmount,
                ActorLogin,
                CancellationToken.None
            )
        );
    }

    private Task RemoveLeaderboardEntryAsync(string login)
    {
        return RunAsync(() =>
            _dashboard.RemoveBalanceAsync(HostId, login, ActorLogin, CancellationToken.None)
        );
    }

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

    private Task StartGiveawayAsync()
    {
        return RunAsync(() =>
            _dashboard.StartGiveawayAsync(HostId, HostLogin, CancellationToken.None)
        );
    }

    private async Task LoadFeatureStateAsync()
    {
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Points,
                CancellationToken.None
            );
    }

    private void PublishResult(PointOperationOutcome outcome)
    {
        _ = outcome.Match(
            succeeded =>
            {
                if (!string.IsNullOrWhiteSpace(succeeded.Message))
                {
                    _toasts.Publish(new ToastRequest<SuccessToastStrategy>(succeeded.Message));
                }

                return true;
            },
            failed =>
            {
                if (!string.IsNullOrWhiteSpace(failed.Message))
                {
                    _toasts.Publish(new ToastRequest<WarningToastStrategy>(failed.Message));
                }

                return true;
            }
        );
    }
}

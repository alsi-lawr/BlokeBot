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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Points.Dashboard;

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
            : $"Runs until {_state.ActiveGiveaway.EndsAtUtc.ToLocalTime():HH:mm}. {_state.ActiveGiveaway.Entrants.Count} people joined.";

    private static string LedgerChangeLabel(string kind)
    {
        return kind switch
        {
            "Add" => "Points added",
            "Remove" => "Points removed",
            "DeleteBalance" => "Balance deleted",
            "TransferOut" => "Points given",
            "TransferIn" => "Points received",
            "GambleWin" => "Gamble won",
            "GambleLoss" => "Gamble lost",
            "GiveawayWin" => "Giveaway prize",
            "GuessWin" => "Guessing prize",
            _ => "Points changed",
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
        return RunAsync(() => _dashboard.EndGiveawayAsync(HostId, HostLogin, CancellationToken.None));
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
        _state = _featureEnabled ? await _dashboard.LoadAsync(HostId, CancellationToken.None) : null;
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

    private async Task RunAsync(Func<Task<PointOperationResult>> operation)
    {
        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            return;
        }

        var result = await operation();
        PublishResult(result);
        await LoadAsync();
    }

    private Task StartGiveawayAsync()
    {
        return RunAsync(() => _dashboard.StartGiveawayAsync(HostId, HostLogin, CancellationToken.None));
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

    private void PublishResult(PointOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return;
        }

        _toasts.Publish(result.Success ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}

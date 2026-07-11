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
    private bool featureEnabled;
    private PointsDashboardState? state;
    private PointBalanceEntry? lookupResult;
    private string lookupLogin = string.Empty;
    private string giveFrom = string.Empty;
    private string giveTo = string.Empty;
    private string giveAmount = string.Empty;
    private string addLogin = string.Empty;
    private string addAmount = string.Empty;
    private string removeLogin = string.Empty;
    private string removeAmount = string.Empty;

    private string GiveawaySummary =>
        state?.ActiveGiveaway is null
            ? "No giveaway running."
            : $"Runs until {state.ActiveGiveaway.EndsAtUtc.ToLocalTime():HH:mm}. {state.ActiveGiveaway.Entrants.Count} people joined.";

    private static string LedgerChangeLabel(string kind) =>
        kind switch
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

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                [AppEventKind.PointsChanged, AppEventKind.HostedChannelsChanged],
                work => InvokeAsync(work),
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadPageContextAsync();
        await LoadAsync();
    }

    private Task AddAsync() =>
        RunAsync(() =>
            Dashboard.AddAsync(HostId, addLogin, addAmount, ActorLogin, CancellationToken.None)
        );

    private Task CancelGiveawayAsync() =>
        RunAsync(() => Dashboard.CancelGiveawayAsync(HostId, CancellationToken.None));

    private Task EndGiveawayAsync() =>
        RunAsync(() => Dashboard.EndGiveawayAsync(HostId, HostLogin, CancellationToken.None));

    private Task GiveAsync() =>
        RunAsync(() =>
            Dashboard.GiveAsync(HostId, giveFrom, giveTo, giveAmount, CancellationToken.None)
        );

    private async Task LoadAsync()
    {
        if (HostId == 0)
            return;

        await LoadFeatureStateAsync();
        state = featureEnabled ? await Dashboard.LoadAsync(HostId, CancellationToken.None) : null;
    }

    private async Task LookupAsync()
    {
        if (HostId == 0 || !featureEnabled || string.IsNullOrWhiteSpace(lookupLogin))
            return;

        lookupResult = await Dashboard.LookupAsync(HostId, lookupLogin, CancellationToken.None);
    }

    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private Task RemoveAsync() =>
        RunAsync(() =>
            Dashboard.RemoveAsync(
                HostId,
                removeLogin,
                removeAmount,
                ActorLogin,
                CancellationToken.None
            )
        );

    private Task RemoveLeaderboardEntryAsync(string login) =>
        RunAsync(() =>
            Dashboard.RemoveBalanceAsync(HostId, login, ActorLogin, CancellationToken.None)
        );

    private async Task RunAsync(Func<Task<PointOperationResult>> operation)
    {
        await LoadFeatureStateAsync();
        if (!featureEnabled)
            return;

        var result = await operation();
        PublishResult(result);
        await LoadAsync();
    }

    private Task StartGiveawayAsync() =>
        RunAsync(() => Dashboard.StartGiveawayAsync(HostId, HostLogin, CancellationToken.None));

    private async Task LoadFeatureStateAsync()
    {
        featureEnabled =
            HostId != 0
            && await Features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Points,
                CancellationToken.None
            );
    }

    private void PublishResult(PointOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
            return;

        Toasts.Publish(result.Success ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}

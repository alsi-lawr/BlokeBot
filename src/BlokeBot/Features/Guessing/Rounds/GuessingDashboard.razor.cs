using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Commands;
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
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Guessing.Rounds;

public partial class GuessingDashboard
{
    private enum DashboardTab
    {
        Live,
        History,
    }

    private DashboardTab activeTab;
    private bool featureEnabled;
    private GuessLeaderboardPage? leaderboard;
    private int historyPage = 1;
    private int historyPageSize = 25;
    private int historyProfileId;
    private DateTime? historyFromDate = DateTime.Today.AddDays(-30);
    private DateTime? historyToDate = DateTime.Today;
    private string historyUsername = string.Empty;
    private int selectedProfileId;
    private GuessingDashboardState? state;
    private string winnerName = string.Empty;

    private string RoundStartedText =>
        state?.CurrentRound is null
            ? "No active or stopped round"
            : state.CurrentRound.StartedAtUtc.ToLocalTime().ToString("MMM d, HH:mm");

    private string RoundStatusText =>
        state?.CurrentRound is null
            ? "Idle"
            : $"{state.CurrentRound.ProfileName}: {state.CurrentRound.Status}";

    private string SegmentedControlClass =>
        activeTab == DashboardTab.History
            ? "segmented-motion segmented-motion--history"
            : "segmented-motion";

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                [AppEventKind.GuessingChanged, AppEventKind.HostedChannelsChanged],
                work => InvokeAsync(work),
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadPageContextAsync();
        await LoadFeatureStateAsync();
        await LoadAsync();
    }

    private async Task ActivateTabAsync(DashboardTab tab)
    {
        activeTab = tab;

        if (tab == DashboardTab.History && leaderboard is null)
            await LoadHistoryAsync();
    }

    private async Task ReloadForEventAsync()
    {
        await LoadFeatureStateAsync();
        if (!featureEnabled)
        {
            state = null;
            leaderboard = null;
            return;
        }

        if (activeTab == DashboardTab.History)
            await LoadHistoryAsync();
        else
            await LoadAsync();
    }

    private async Task ResetAndLoadHistoryAsync()
    {
        historyPage = 1;
        await LoadHistoryAsync();
    }

    private async Task DeclareWinnerAsync()
    {
        if (string.IsNullOrWhiteSpace(winnerName))
        {
            Toasts.Warning("Choose one of the saved winner names first.");
            return;
        }

        await RunAsync(() => Rounds.DeclareWinnerAsync(HostId, winnerName, CancellationToken.None));
        winnerName = string.Empty;
    }

    private async Task LoadAsync()
    {
        if (HostId == 0)
            return;

        await LoadFeatureStateAsync();
        if (!featureEnabled)
        {
            state = null;
            return;
        }

        state = await Dashboard.LoadStateAsync(HostId, CancellationToken.None);
        if (selectedProfileId == 0)
            selectedProfileId =
                state.CurrentRound?.ProfileId
                ?? state.Profiles.FirstOrDefault(x => x.IsDefault)?.Id
                ?? state.Profiles.FirstOrDefault()?.Id
                ?? 0;
    }

    private async Task LoadHistoryAsync()
    {
        if (HostId == 0)
            return;

        await LoadFeatureStateAsync();
        if (!featureEnabled)
        {
            state = null;
            leaderboard = null;
            return;
        }

        state = await Dashboard.LoadStateAsync(HostId, CancellationToken.None);
        leaderboard = await History.LoadLeaderboardAsync(
            HostId,
            new GuessHistoryQuery
            {
                FromUtc = StartOfLocalDateUtc(historyFromDate),
                Page = historyPage,
                PageSize = historyPageSize,
                ProfileId = historyProfileId == 0 ? null : historyProfileId,
                ToUtc = EndOfLocalDateUtc(historyToDate),
                Username = historyUsername,
            },
            CancellationToken.None
        );

        historyPage = leaderboard.Page;
    }

    private async Task NextHistoryPageAsync()
    {
        if (leaderboard is null || leaderboard.Page >= leaderboard.PageCount)
            return;

        historyPage++;
        await LoadHistoryAsync();
    }

    private async Task PreviousHistoryPageAsync()
    {
        if (leaderboard is null || leaderboard.Page <= 1)
            return;

        historyPage--;
        await LoadHistoryAsync();
    }

    private Task RefreshAsync() =>
        activeTab == DashboardTab.History ? LoadHistoryAsync() : LoadAsync();

    private async Task LoadFeatureStateAsync()
    {
        featureEnabled =
            HostId != 0
            && await Features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
    }

    private Task StartRoundAsync() =>
        RunAsync(() => Rounds.StartRoundAsync(HostId, selectedProfileId, CancellationToken.None));

    private static DateTime? StartOfLocalDateUtc(DateTime? value)
    {
        return value is { } date
            ? DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime()
            : null;
    }

    private static DateTime? EndOfLocalDateUtc(DateTime? value)
    {
        return value is { } date
            ? DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime()
            : null;
    }

    private Task StopGuessingAsync() =>
        RunAsync(() => Rounds.StopGuessingAsync(HostId, CancellationToken.None));

    private string TabClass(DashboardTab tab)
    {
        return activeTab == tab
            ? "segmented-motion__tab segmented-motion__tab--active"
            : "segmented-motion__tab";
    }

    private async Task RunAsync(Func<Task<GuessingOperationResult>> operation)
    {
        if (HostId == 0)
            return;

        await LoadFeatureStateAsync();
        if (!featureEnabled)
            return;

        var result = await operation();
        if (result.Succeeded)
            await Chat.SendAsync(Host!.Login, result.Message, CancellationToken.None);

        PublishResult(result);
        await LoadAsync();

        if (leaderboard is not null)
            await LoadHistoryAsync();
    }

    private void PublishResult(GuessingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
            return;

        Toasts.Publish(result.Succeeded ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}

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
        Leaderboard,
    }

    private const int RecentRoundCount = 20;
    private DashboardTab activeTab;
    private bool featureEnabled;
    private GuessLeaderboardPage? leaderboard;
    private int leaderboardPage = 1;
    private int leaderboardPageSize = 25;
    private int leaderboardProfileId;
    private DateTime? leaderboardFromDate = DateTime.Today.AddDays(-30);
    private DateTime? leaderboardToDate = DateTime.Today;
    private string leaderboardUsername = string.Empty;
    private IReadOnlyList<GuessRoundHistoryEntry>? recentRounds;
    private int selectedProfileId;
    private GuessingDashboardState? state;
    private string winnerName = string.Empty;

    private string RoundStartedText =>
        state?.CurrentRound is null
            ? "Start a round when you're ready"
            : state.CurrentRound.StartedAtUtc.ToLocalTime().ToString("MMM d, HH:mm");

    private string RoundStatusText =>
        state?.CurrentRound is null
            ? "No round running"
            : $"{state.CurrentRound.ProfileName}: {RoundStatusLabel(state.CurrentRound.Status)}";

    private static string RoundStatusLabel(GuessRoundStatus status) =>
        status switch
        {
            GuessRoundStatus.Open => "Accepting guesses",
            GuessRoundStatus.Closed => "Waiting for a winner",
            GuessRoundStatus.Completed => "Finished",
            _ => "Not running",
        };

    private string SegmentedControlClass =>
        activeTab switch
        {
            DashboardTab.History =>
                "segmented-motion segmented-motion--three segmented-motion--second",
            DashboardTab.Leaderboard =>
                "segmented-motion segmented-motion--three segmented-motion--third",
            _ => "segmented-motion segmented-motion--three",
        };

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

        if (tab == DashboardTab.History && recentRounds is null)
            await LoadRecentRoundsAsync();

        if (tab == DashboardTab.Leaderboard && leaderboard is null)
            await LoadLeaderboardAsync();
    }

    private async Task ReloadForEventAsync()
    {
        await LoadFeatureStateAsync();
        if (!featureEnabled)
        {
            state = null;
            leaderboard = null;
            recentRounds = null;
            return;
        }

        await (
            activeTab switch
            {
                DashboardTab.History => LoadRecentRoundsAsync(),
                DashboardTab.Leaderboard => LoadLeaderboardAsync(),
                _ => LoadAsync(),
            }
        );
    }

    private async Task ResetAndLoadLeaderboardAsync()
    {
        leaderboardPage = 1;
        await LoadLeaderboardAsync();
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

    private async Task LoadLeaderboardAsync()
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
                FromUtc = StartOfLocalDateUtc(leaderboardFromDate),
                Page = leaderboardPage,
                PageSize = leaderboardPageSize,
                ProfileId = leaderboardProfileId == 0 ? null : leaderboardProfileId,
                ToUtc = EndOfLocalDateUtc(leaderboardToDate),
                Username = leaderboardUsername,
            },
            CancellationToken.None
        );

        leaderboardPage = leaderboard.Page;
    }

    private async Task LoadRecentRoundsAsync()
    {
        if (HostId == 0)
            return;

        await LoadFeatureStateAsync();
        if (!featureEnabled)
        {
            state = null;
            recentRounds = null;
            return;
        }

        recentRounds = await History.LoadRecentCompletedRoundsAsync(
            HostId,
            RecentRoundCount,
            CancellationToken.None
        );
    }

    private async Task NextLeaderboardPageAsync()
    {
        if (leaderboard is null || leaderboard.Page >= leaderboard.PageCount)
            return;

        leaderboardPage++;
        await LoadLeaderboardAsync();
    }

    private async Task PreviousLeaderboardPageAsync()
    {
        if (leaderboard is null || leaderboard.Page <= 1)
            return;

        leaderboardPage--;
        await LoadLeaderboardAsync();
    }

    private Task RefreshAsync() =>
        activeTab switch
        {
            DashboardTab.History => LoadRecentRoundsAsync(),
            DashboardTab.Leaderboard => LoadLeaderboardAsync(),
            _ => LoadAsync(),
        };

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

    private static string FormatEndedAt(GuessRoundHistoryEntry round) =>
        round.ClosedAtUtc?.ToLocalTime().ToString("MMM d, HH:mm") ?? "Not recorded";

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
            await LoadLeaderboardAsync();

        if (recentRounds is not null)
            await LoadRecentRoundsAsync();
    }

    private void PublishResult(GuessingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
            return;

        Toasts.Publish(result.Succeeded ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}

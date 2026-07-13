using System.Diagnostics;
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

    private const int _recentRoundCount = 20;
    private DashboardTab _activeTab;
    private bool _featureEnabled;
    private GuessLeaderboardPage? _leaderboard;
    private int _leaderboardPage = 1;
    private int _leaderboardPageSize = 25;
    private int _leaderboardProfileId;
    private DateTime? _leaderboardFromDate = DateTime.Today.AddDays(-30);
    private DateTime? _leaderboardToDate = DateTime.Today;
    private string _leaderboardUsername = string.Empty;
    private IReadOnlyList<GuessRoundHistoryEntry>? _recentRounds;
    private int _selectedProfileId;
    private GuessingDashboardState? _state;
    private string _winnerName = string.Empty;

    private string _roundStartedText =>
        _state?.CurrentRound is null
            ? "Start a round when you're ready"
            : _state.CurrentRound.StartedAtUtc.ToLocalTime().ToString("MMM d, HH:mm");

    private string _roundStatusText =>
        _state?.CurrentRound is null
            ? "No round running"
            : $"{_state.CurrentRound.ProfileName}: {RoundStatusLabel(_state.CurrentRound.Status)}";

    private static string RoundStatusLabel(GuessRoundStatus status)
    {
        return status switch
        {
            GuessRoundStatus.Open => "Accepting guesses",
            GuessRoundStatus.Closed => "Waiting for a winner",
            GuessRoundStatus.Completed => "Finished",
            _ => "Not running",
        };
    }

    private string _segmentedControlClass =>
        _activeTab switch
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
            _events.SubscribeForComponentRefresh(
                [AppEventKind.GuessingChanged, AppEventKind.HostedChannelsChanged],
                InvokeAsync,
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
        _activeTab = tab;

        if (tab == DashboardTab.History && _recentRounds is null)
        {
            await LoadRecentRoundsAsync();
        }

        if (tab == DashboardTab.Leaderboard && _leaderboard is null)
        {
            await LoadLeaderboardAsync();
        }
    }

    private async Task ReloadForEventAsync()
    {
        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            _state = null;
            _leaderboard = null;
            _recentRounds = null;
            return;
        }

        await (
            _activeTab switch
            {
                DashboardTab.History => LoadRecentRoundsAsync(),
                DashboardTab.Leaderboard => LoadLeaderboardAsync(),
                _ => LoadAsync(),
            }
        );
    }

    private async Task ResetAndLoadLeaderboardAsync()
    {
        _leaderboardPage = 1;
        await LoadLeaderboardAsync();
    }

    private async Task DeclareWinnerAsync()
    {
        if (string.IsNullOrWhiteSpace(_winnerName))
        {
            _toasts.Warning("Choose one of the saved winner names first.");
            return;
        }

        await RunAsync(() =>
            _rounds.DeclareWinnerAsync(HostId, _winnerName, CancellationToken.None)
        );
        _winnerName = string.Empty;
    }

    private async Task LoadAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            _state = null;
            return;
        }

        _state = await _dashboard.LoadStateAsync(HostId, CancellationToken.None);
        if (_selectedProfileId == 0)
        {
            _selectedProfileId =
                _state.CurrentRound?.ProfileId
                ?? _state.Profiles.FirstOrDefault(x => x.IsDefault)?.Id
                ?? _state.Profiles.FirstOrDefault()?.Id
                ?? 0;
        }
    }

    private async Task LoadLeaderboardAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            _state = null;
            _leaderboard = null;
            return;
        }

        _state = await _dashboard.LoadStateAsync(HostId, CancellationToken.None);
        _leaderboard = await _history.LoadLeaderboardAsync(
            HostId,
            new GuessHistoryQuery
            {
                FromUtc = StartOfLocalDateUtc(_leaderboardFromDate),
                Page = _leaderboardPage,
                PageSize = _leaderboardPageSize,
                ProfileId = _leaderboardProfileId == 0 ? null : _leaderboardProfileId,
                ToUtc = EndOfLocalDateUtc(_leaderboardToDate),
                Username = _leaderboardUsername,
            },
            CancellationToken.None
        );

        _leaderboardPage = _leaderboard.Page;
    }

    private async Task LoadRecentRoundsAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            _state = null;
            _recentRounds = null;
            return;
        }

        _recentRounds = await _history.LoadRecentCompletedRoundsAsync(
            HostId,
            _recentRoundCount,
            CancellationToken.None
        );
    }

    private async Task NextLeaderboardPageAsync()
    {
        if (_leaderboard is null || _leaderboard.Page >= _leaderboard.PageCount)
        {
            return;
        }

        _leaderboardPage++;
        await LoadLeaderboardAsync();
    }

    private async Task PreviousLeaderboardPageAsync()
    {
        if (_leaderboard is null || _leaderboard.Page <= 1)
        {
            return;
        }

        _leaderboardPage--;
        await LoadLeaderboardAsync();
    }

    private Task RefreshAsync()
    {
        return _activeTab switch
        {
            DashboardTab.History => LoadRecentRoundsAsync(),
            DashboardTab.Leaderboard => LoadLeaderboardAsync(),
            _ => LoadAsync(),
        };
    }

    private async Task LoadFeatureStateAsync()
    {
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
    }

    private Task StartRoundAsync()
    {
        return RunAsync(() =>
            _rounds.StartRoundAsync(HostId, _selectedProfileId, CancellationToken.None)
        );
    }

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

    private static string FormatEndedAt(GuessRoundHistoryEntry round)
    {
        return round.ClosedAtUtc?.ToLocalTime().ToString("MMM d, HH:mm") ?? "Not recorded";
    }

    private Task StopGuessingAsync()
    {
        return RunAsync(() => _rounds.StopGuessingAsync(HostId, CancellationToken.None));
    }

    private string TabClass(DashboardTab tab)
    {
        return _activeTab == tab
            ? "segmented-motion__tab segmented-motion__tab--active"
            : "segmented-motion__tab";
    }

    private async Task RunAsync(Func<Task<GuessingOperationResult>> operation)
    {
        if (HostId == 0)
        {
            return;
        }

        await LoadFeatureStateAsync();
        if (!_featureEnabled)
        {
            return;
        }

        var result = await operation();
        if (result.Succeeded)
        {
            var outcome = await _chat.SendAsync(
                Host!.Login,
                result.Message,
                new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                CancellationToken.None
            );
            switch (outcome)
            {
                case PublicChatSendOutcome.Accepted:
                    PublishResult(result);
                    break;
                case PublicChatSendOutcome.Rejected:
                    _toasts.Publish(
                        ToastKind.Warning,
                        "The action completed, but its chat message could not be queued."
                    );
                    break;
                default:
                    throw new UnreachableException("Unknown public-chat send outcome.");
            }
        }
        else
        {
            PublishResult(result);
        }
        await LoadAsync();

        if (_leaderboard is not null)
        {
            await LoadLeaderboardAsync();
        }

        if (_recentRounds is not null)
        {
            await LoadRecentRoundsAsync();
        }
    }

    private void PublishResult(GuessingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return;
        }

        _toasts.Publish(result.Succeeded ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}

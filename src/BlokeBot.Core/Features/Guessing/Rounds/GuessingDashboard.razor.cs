using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public partial class GuessingDashboard
{
    private static readonly IReadOnlyList<SegmentedTabItem> _dashboardTabs =
    [
        new("live", "Live"),
        new("history", "History"),
        new("leaderboard", "Leaderboard"),
    ];

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
            : _state
                .CurrentRound.Lifecycle.StartedAtUtc.ToLocalTime()
                .ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);

    private string _roundStatusText =>
        _state?.CurrentRound is null
            ? "No round running"
            : $"{_state.CurrentRound.ProfileName}: {RoundStatusLabel(_state.CurrentRound.Lifecycle)}";

    private static string RoundStatusLabel(GuessRoundLifecycle lifecycle) =>
        lifecycle.Match(
            static _ => "Accepting guesses",
            static _ => "Waiting for a winner",
            static _ => "Finished"
        );

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.GuessingChanged, AppEventKind.HostedChannelsChanged],
                InvokeAsync,
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        _ = await LoadPageContextAsync();
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
            _ = _toasts.Publish(
                new ToastRequest<WarningToastStrategy>(
                    "Choose one of the saved winner names first."
                )
            );
            return;
        }

        await RunAsync(() =>
            _rounds
                .DeclareWinner(HostId, _winnerName)
                .Map(outcome =>
                    outcome.Match(
                        completed => completed.Result,
                        failed => new GuessingOperationOutcome.Rejected(
                            failed.Message,
                            failed.Target
                        )
                    )
                )
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

    private Task RefreshAsync() =>
        _activeTab switch
        {
            DashboardTab.History => LoadRecentRoundsAsync(),
            DashboardTab.Leaderboard => LoadLeaderboardAsync(),
            _ => LoadAsync(),
        };

    private async Task LoadFeatureStateAsync() =>
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );

    private Task StartRoundAsync() =>
        RunAsync(() => _rounds.StartRound(HostId, _selectedProfileId));

    private static DateTime? StartOfLocalDateUtc(DateTime? value) =>
        value is { } date
            ? DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime()
            : null;

    private static DateTime? EndOfLocalDateUtc(DateTime? value) =>
        value is { } date
            ? DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime()
            : null;

    private static string FormatEndedAt(GuessRoundHistoryEntry round) =>
        round
            .Lifecycle.ClosedAtUtc.ToLocalTime()
            .ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);

    private Task StopGuessingAsync() => RunAsync(() => _rounds.StopGuessing(HostId));

    private static string DashboardTabKey(DashboardTab tab) =>
        tab switch
        {
            DashboardTab.Live => "live",
            DashboardTab.History => "history",
            DashboardTab.Leaderboard => "leaderboard",
            _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, null),
        };

    private Task ActivateTabAsync(string tab) =>
        ActivateTabAsync(
            tab switch
            {
                "live" => DashboardTab.Live,
                "history" => DashboardTab.History,
                "leaderboard" => DashboardTab.Leaderboard,
                _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, null),
            }
        );

    private async Task RunAsync(Func<IO<GuessingOperationOutcome, Never>> operation)
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

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var execution = await operation().ExecuteAsync(CancellationToken.None);
                var result = execution.Match(value => value, _ => throw new UnreachableException());
                if (result is GuessingOperationOutcome.Succeeded)
                {
                    var outcome = await _chat.SendAsync(
                        Host!.Login,
                        result.Message,
                        new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                        CancellationToken.None
                    );
                    outcome
                        .Match<Action>(
                            _ => () => PublishResult(result),
                            _ =>
                                () =>
                                    _toasts.Publish(
                                        new ToastRequest<WarningToastStrategy>(
                                            "The action completed, but its chat message could not be queued."
                                        )
                                    )
                        )
                        .Invoke();
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
        );
    }

    private void PublishResult(GuessingOperationOutcome result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return;
        }

        if (result is GuessingOperationOutcome.Succeeded)
        {
            _ = _toasts.Publish(new ToastRequest<SuccessToastStrategy>(result.Message));
        }
        else
        {
            _ = _toasts.Publish(new ToastRequest<WarningToastStrategy>(result.Message));
        }
    }
}

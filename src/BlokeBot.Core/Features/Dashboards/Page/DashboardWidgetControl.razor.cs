using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardWidgetControl
{
    [Parameter, EditorRequired]
    public required DashboardWidgetView View { get; set; }

    [Parameter]
    public int SelectedHostId { get; set; }

    [Parameter, EditorRequired]
    public required AuthenticatedSession Session { get; set; }

    [Parameter]
    public bool Editing { get; set; }

    [Parameter]
    public DashboardEditorTargets EditorTargets { get; set; } = new(new());

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public DashboardRecentViewerView? RecentViewers { get; set; }

    [Inject]
    private TimeProvider _clock { get; set; } = default!;

    [Inject]
    private DashboardBindingReader _bindingReader { get; set; } = default!;

    [Inject]
    private DashboardAutomationControls _automation { get; set; } = default!;

    [Inject]
    private DashboardPollControls _polls { get; set; } = default!;

    [Inject]
    private DashboardPredictionControls _predictions { get; set; } = default!;

    [Inject]
    private DashboardGiveawayControls _giveaways { get; set; } = default!;

    [Inject]
    private DashboardGuessingControls _guessingControls { get; set; } = default!;

    [Inject]
    private DashboardValueReader _values { get; set; } = default!;

    [Inject]
    private AutomationUiChanges _automationChanges { get; set; } = default!;

    [Inject]
    private DashboardGiveawayChanges _giveawayChanges { get; set; } = default!;

    [Inject]
    private EventBus<AppEventKind> _events { get; set; } = default!;

    [Inject]
    private IJSRuntime _js { get; set; } = default!;
    private DashboardWidget _widget => View.Widget;
    private DashboardHostId _selectedHost => new(SelectedHostId);
    private readonly List<IDisposable> _bindings = [];
    private readonly List<IDisposable> _runBindings = [];
    private readonly ObserverIdentity _observer = ObserverIdentity.Named(
        $"DashboardControl.{Guid.NewGuid():N}"
    );
    private DashboardRefreshQueue? _refresh;
    private DashboardValueSession? _value;
    private (
        int Host,
        DashboardWidgetId _widget,
        DashboardWidgetConfiguration Configuration,
        bool Editing
    )? _identity;
    private bool _disposed;
    private long _generation;
    private bool _busy;
    private bool _previousVisible = true;
    private string? _unavailable;
    private string? _feedback;
    private string _status = "No runs yet";
    private string _winner = string.Empty;
    private int _duration;
    private ImmutableArray<AutomationRunId> _runIds = [];
    private AutomationCountdownSnapshot? _countdown;
    private AutomationCountdownSnapshot? _selectedCountdown;
    private ImmutableArray<AutomationCountdownSnapshot> _liveCountdowns = [];
    private PollControlState? _poll;
    private PredictionControlState? _prediction;
    private PointsGiveawayCurrent? _giveaway;
    private GuessingControlState? _guessing;
    private bool _needsViewer =>
        _value?.State
            is DashboardValueState.Available { Definition.Scope: CustomValueScope.User }
                or DashboardValueState.Unavailable
                {
                    Reason: DashboardValueUnavailableReason.ViewerRequired
                        or DashboardValueUnavailableReason.ViewerNotFound
                };
    private string _valueText =>
        _value?.State is DashboardValueState.Available available ? available.Value.Value : "—";
    private string _valueStatus =>
        _value?.State.Match(
            available =>
                available.Viewer is null
                    ? available.Definition.Name
                    : $"{available.Definition.Name} · {available.Viewer.Login}",
            unavailable =>
                unavailable.Reason switch
                {
                    DashboardValueUnavailableReason.ViewerRequired => "Choose a viewer",
                    DashboardValueUnavailableReason.ViewerNotFound =>
                        "Viewer is no longer in the recent list",
                    _ => "Value unavailable",
                },
            rejected => $"Access unavailable: {rejected.Reason}"
        ) ?? "Loading value";
    private string _remainingText =>
        _countdown?.DeadlineUtc is { } deadline
        && _countdown.State == AutomationCountdownState.Running
            ? TimeSpan
                .FromSeconds(
                    Math.Max(0, Math.Ceiling((deadline - _clock.GetUtcNow()).TotalSeconds))
                )
                .ToString(@"mm\:ss", CultureInfo.InvariantCulture)
            : "00:00";

    protected override async Task OnParametersSetAsync()
    {
        var identity = (SelectedHostId, _widget.Id, _widget.Configuration, Editing);
        if (_identity != identity)
        {
            var generation = ++_generation;
            await UnbindAsync();
            _identity = identity;
            _unavailable = Editing ? null : "Loading target";
            _feedback = null;
            _busy = false;
            _duration = 0;
            _countdown = null;
            _selectedCountdown = null;
            _liveCountdowns = [];
            _poll = null;
            _prediction = null;
            _giveaway = null;
            _guessing = null;
            _runIds = [];
            _winner = string.Empty;
            _status = "No runs yet";
            if (Editing)
            {
                return;
            }
            if (_widget.Configuration.Binding is DashboardBinding.Unconfigured)
            {
                _unavailable = "Unconfigured · choose a target in Editor";
                return;
            }
            _refresh = new(
                () => InvokeAsync(() => RefreshAsync(generation)),
                exception => ReportUiFault("Dashboard target refresh", exception)
            );

            var host = new AutomationHostId(SelectedHostId);
            switch (_widget.Configuration)
            {
                case DashboardWidgetConfiguration.Countdown timer
                    when timer.Binding
                        is DashboardBinding.Configured { Target: DashboardTarget.Countdown target }:
                    _duration = (int)timer.DefaultDuration.TotalSeconds;
                    _bindings.Add(
                        _automationChanges.Subscribe(
                            new AutomationUiChange.Countdown(host, target.Name),
                            _observer,
                            (notification, ct) => QueueCurrent(generation)
                        )
                    );
                    _bindings.Add(
                        _automationChanges.Subscribe(
                            new AutomationUiChange.CountdownHost(host),
                            _observer,
                            (notification, ct) => QueueCurrent(generation)
                        )
                    );
                    break;
                case DashboardWidgetConfiguration.Value value:
                    _value = _values.CreateSession(() =>
                        new ValueTask(
                            InvokeAsync(() =>
                            {
                                if (!_disposed && _generation == generation)
                                {
                                    StateHasChanged();
                                }
                            })
                        )
                    );
                    _value.SetVisible(Visible);
                    await _value.BindAsync(Session, _selectedHost, value, CancellationToken.None);
                    if (_disposed || generation != _generation)
                    {
                        return;
                    }
                    _unavailable = null;
                    break;
                case DashboardWidgetConfiguration.Poll or DashboardWidgetConfiguration.Prediction:
                    _bindings.Add(
                        _events.Subscribe(
                            AppEventKind.TwitchOperationsChanged,
                            _observer,
                            (notification, ct) => QueueCurrent(generation)
                        )
                    );
                    break;
                case DashboardWidgetConfiguration.Giveaway:
                    _bindings.Add(
                        _giveawayChanges.Subscribe(
                            _selectedHost,
                            _observer,
                            (_, _) => QueueCurrent(generation)
                        )
                    );
                    break;
                case DashboardWidgetConfiguration.Guessing:
                    _bindings.Add(
                        _events.Subscribe(
                            AppEventKind.GuessingChanged,
                            _observer,
                            (notification, ct) => QueueCurrent(generation)
                        )
                    );
                    break;
            }
            _bindings.Add(
                _events.Subscribe(
                    AppEventKind.HostedChannelsChanged,
                    _observer,
                    (notification, ct) => QueueCurrent(generation)
                )
            );
            _refresh.SetVisible(Visible);
            _refresh.Request();
        }
        if (_previousVisible != Visible)
        {
            _previousVisible = Visible;
            _refresh?.SetVisible(Visible);
            _value?.SetVisible(Visible);
        }
        if (
            _value?.Viewer is { } selected
            && RecentViewers?.Host == _selectedHost
            && RecentViewers.Result
                is DashboardResult<ImmutableArray<CustomCommandViewer>>.Succeeded
            && !_viewerOptions.Any(viewer => viewer.TwitchUserId == selected.TwitchUserId)
        )
        {
            await _value.RefreshAsync(CancellationToken.None);
        }
    }

    private ValueTask QueueCurrent(long generation)
    {
        if (!_disposed && generation == _generation)
        {
            _refresh?.Request();
        }
        return ValueTask.CompletedTask;
    }

    private async Task RefreshAsync(long generation)
    {
        if (_disposed || Editing || generation != _generation || !Visible)
        {
            return;
        }
        var widget = _widget;
        var host = _selectedHost;
        var session = Session;
        switch (widget.Configuration)
        {
            case DashboardWidgetConfiguration.Automation:
                var bindings = await _bindingReader.ReadAsync(
                    session,
                    host,
                    DashboardDocument.Create(12, (widget, new(0, 0, 6, 4, 0))),
                    CancellationToken.None
                );
                if (!Current(generation, widget, host) || !Visible)
                {
                    return;
                }
                if (
                    bindings
                        is not DashboardResult<IReadOnlyList<DashboardWidgetBindingState>>.Succeeded
                        {
                            Value: var bindingStates
                        }
                    || bindingStates.Single().Availability != DashboardBindingAvailability.Ready
                )
                {
                    _unavailable =
                        "Automation unavailable · check its target and channel enablement";
                    break;
                }
                _unavailable = null;
                var runs = await _automation.ReadRunsAsync(
                    session,
                    host,
                    _runIds,
                    CancellationToken.None
                );
                if (!Current(generation, widget, host) || !Visible)
                {
                    return;
                }
                if (
                    runs is DashboardResult<AutomationRunStatusOutcome>.Succeeded
                    {
                        Value: AutomationRunStatusOutcome.Available currentRuns
                    }
                )
                {
                    _status = currentRuns.Runs.IsEmpty
                        ? "No runs yet"
                        : string.Join(" · ", currentRuns.Runs.Select(r => r.State.ToString()));
                }
                else
                {
                    _unavailable = "Automation status unavailable";
                }
                break;
            case DashboardWidgetConfiguration.Countdown timer:
                var requestedTimer = _selectedCountdown;
                var live = await _automation.ReadLiveCountdownsAsync(
                    session,
                    host,
                    CancellationToken.None
                );
                if (
                    !Current(generation, widget, host)
                    || !Visible
                    || _selectedCountdown != requestedTimer
                )
                {
                    return;
                }
                _liveCountdowns = live
                    is DashboardResult<ImmutableArray<AutomationCountdownSnapshot>>.Succeeded timers
                    ? timers.Value
                    : [];
                if (
                    requestedTimer is not null
                    && !_liveCountdowns.Any(value =>
                        value.OccurrenceId == requestedTimer.OccurrenceId
                    )
                )
                {
                    _selectedCountdown = null;
                    requestedTimer = null;
                }
                var timerResult = await _automation.ReadCountdownAsync(
                    session,
                    host,
                    timer,
                    CancellationToken.None,
                    requestedTimer
                );
                if (
                    !Current(generation, widget, host)
                    || !Visible
                    || _selectedCountdown != requestedTimer
                )
                {
                    return;
                }
                if (
                    timerResult is DashboardResult<AutomationCountdownControlOutcome>.Succeeded
                    {
                        Value: AutomationCountdownControlOutcome.Available available
                    }
                )
                {
                    _countdown = available.Countdown;
                    _unavailable = null;
                }
                else
                {
                    _unavailable = "Countdown unavailable · check channel Automations";
                }
                break;
            case DashboardWidgetConfiguration.Poll poll:
                var pollResult = await _polls.ReadAsync(
                    session,
                    host,
                    poll,
                    CancellationToken.None
                );
                if (!Current(generation, widget, host) || !Visible)
                {
                    return;
                }
                if (pollResult is DashboardResult<PollControlState>.Succeeded availablePoll)
                {
                    _poll = availablePoll.Value;
                    _unavailable =
                        _poll.Template is null ? "Poll template missing · rebind in Editor"
                        : _poll.Authorization is PollAuthorizationReadiness.Ready ? null
                        : "Polls need broadcaster authorization or channel enablement";
                }
                else
                {
                    _unavailable = "Poll unavailable";
                }
                break;
            case DashboardWidgetConfiguration.Prediction prediction:
                var predictionResult = await _predictions.ReadAsync(
                    session,
                    host,
                    prediction,
                    CancellationToken.None
                );
                if (!Current(generation, widget, host) || !Visible)
                {
                    return;
                }
                if (
                    predictionResult
                    is DashboardResult<PredictionControlState>.Succeeded availablePrediction
                )
                {
                    _prediction = availablePrediction.Value;
                    _unavailable =
                        _prediction.Template is null
                            ? "Prediction template missing · rebind in Editor"
                        : _prediction.Authorization is PredictionAuthorizationReadiness.Ready ? null
                        : "Predictions unavailable · check broadcaster authorization and eligibility";
                }
                else
                {
                    _unavailable = "Prediction unavailable";
                }
                break;
            case DashboardWidgetConfiguration.Giveaway giveaway:
                var giveawayResult = await _giveaways.ReadAsync(
                    session,
                    host,
                    giveaway,
                    CancellationToken.None
                );
                if (!Current(generation, widget, host) || !Visible)
                {
                    return;
                }
                if (
                    giveawayResult
                    is DashboardResult<DashboardGiveawayResult<PointsGiveawayCurrent?>>.Succeeded
                    {
                        Value: DashboardGiveawayResult<PointsGiveawayCurrent?>.Ready readyGiveaway
                    }
                )
                {
                    _giveaway = readyGiveaway.Value;
                    _unavailable = null;
                }
                else
                {
                    _unavailable = "Giveaway unavailable · check Points settings";
                }
                break;
            case DashboardWidgetConfiguration.Guessing guessing:
                var guessingResult = await _guessingControls.ReadAsync(
                    session,
                    host,
                    guessing,
                    CancellationToken.None
                );
                if (!Current(generation, widget, host) || !Visible)
                {
                    return;
                }
                if (
                    guessingResult
                    is DashboardResult<DashboardGuessingResult<GuessingControlState>>.Succeeded
                    {
                        Value: DashboardGuessingResult<GuessingControlState>.Ready readyGuessing
                    }
                )
                {
                    _guessing = readyGuessing.Value;
                    _unavailable = null;
                }
                else
                {
                    _unavailable = "Guessing profile unavailable · rebind in Editor";
                }
                break;
        }
        StateHasChanged();
    }

    private bool Current(long generation, DashboardWidget widget, DashboardHostId host) =>
        !_disposed
        && generation == _generation
        && _selectedHost == host
        && _widget.Id == widget.Id
        && _widget.Configuration == widget.Configuration
        && !Editing;

    private async Task UnbindAsync()
    {
        foreach (var binding in _bindings)
        {
            binding.Dispose();
        }
        _bindings.Clear();
        foreach (var binding in _runBindings)
        {
            binding.Dispose();
        }
        _runBindings.Clear();
        _value?.Dispose();
        _value = null;
        if (_refresh is not null)
        {
            await _refresh.DisposeAsync();
            _refresh = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        ++_generation;
        await UnbindAsync();
        Dispose();
    }

    private ImmutableArray<CustomCommandViewer> _viewerOptions =>
        RecentViewers?.Host == _selectedHost
        && RecentViewers.Result
            is DashboardResult<ImmutableArray<CustomCommandViewer>>.Succeeded result
            ? result.Value
            : [];

    private string _viewerPlaceholder =>
        RecentViewers?.Host != _selectedHost || RecentViewers.Result is null
            ? "Loading recent viewers"
        : RecentViewers.Result is DashboardResult<ImmutableArray<CustomCommandViewer>>.Rejected
            ? "Recent viewers unavailable"
        : _viewerOptions.IsEmpty ? "No recent chatters"
        : "Choose a known viewer";

    private Task SelectViewerAsync(ChangeEventArgs args) =>
        !Editing && _value is not null
            ? _value.SelectViewerAsync(
                _viewerOptions.FirstOrDefault(viewer =>
                    viewer.TwitchUserId == args.Value?.ToString()
                ),
                CancellationToken.None
            )
            : Task.CompletedTask;
}

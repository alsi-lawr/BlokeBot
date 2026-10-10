using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

internal enum DashboardPredictionAction
{
    Start,
    Lock,
    Cancel,
    Resolve,
}

internal enum DashboardGiveawayAction
{
    Start,
    End,
    Cancel,
}

internal enum DashboardGuessingAction
{
    Start,
    Stop,
    Declare,
}

public partial class DashboardWidgetControl
{
    [Inject]
    private DashboardGuessingReplies _replies { get; set; } = default!;

    private async Task OperateAsync(
        Func<AuthenticatedSession, DashboardHostId, DashboardWidget, long, Task> operation
    )
    {
        if (Editing || _disposed || _busy)
        {
            return;
        }
        var generation = _generation;
        var host = _selectedHost;
        var widget = _widget;
        _busy = true;
        _feedback = null;
        StateHasChanged();
        try
        {
            _ = await LoadPageContextAsync();
            if (!Current(generation, widget, host))
            {
                return;
            }
            await operation(PageContext.Session, host, widget, generation);
        }
        catch (Exception exception)
        {
            ReportUiFault("Dashboard control operation", exception);
            if (Current(generation, widget, host))
            {
                _feedback =
                    "The operation could not be confirmed. Check its current state before trying again.";
            }
        }
        finally
        {
            if (Current(generation, widget, host))
            {
                _busy = false;
                _refresh?.Request();
            }
        }
    }

    private Task RunAsync() =>
        OperateAsync(
            async (session, host, widget, generation) =>
            {
                if (widget.Configuration is not DashboardWidgetConfiguration.Automation config)
                {
                    return;
                }
                var result = await _automation.RunAsync(
                    session,
                    host,
                    config,
                    CancellationToken.None
                );
                if (!Current(generation, widget, host))
                {
                    return;
                }
                if (
                    result is DashboardResult<AutomationManualRunOutcome>.Succeeded
                    {
                        Value: AutomationManualRunOutcome.Dispatched dispatched
                    }
                )
                {
                    foreach (var binding in _runBindings)
                    {
                        binding.Dispose();
                    }
                    _runBindings.Clear();
                    _runIds = dispatched.Dispatch.RunIds;
                    _feedback =
                        dispatched.Dispatch.Status == AutomationDispatchStatus.Accepted
                            ? "Run admitted."
                            : $"Run not admitted: {dispatched.Dispatch.Status}.";
                    foreach (var id in _runIds)
                    {
                        _runBindings.Add(
                            _automationChanges.Subscribe(
                                new AutomationUiChange.Run(new(host.Value), id),
                                _observer,
                                (notification, ct) =>
                                {
                                    if (
                                        !_disposed
                                        && _generation == generation
                                        && _runIds.Contains(id)
                                    )
                                    {
                                        _refresh?.Request();
                                    }
                                    return ValueTask.CompletedTask;
                                }
                            )
                        );
                    }
                    await RefreshAsync(generation);
                }
                else
                {
                    _feedback = "This automation source is unavailable.";
                }
            }
        );

    private Task CountdownAsync(CountdownOperation operation) =>
        OperateAsync(
            async (session, host, widget, generation) =>
            {
                if (widget.Configuration is not DashboardWidgetConfiguration.Countdown config)
                {
                    return;
                }
                var result = await _automation.ApplyCountdownAsync(
                    session,
                    host,
                    config,
                    operation,
                    TimeSpan.FromSeconds(_duration),
                    CancellationToken.None
                );
                if (!Current(generation, widget, host))
                {
                    return;
                }
                _feedback = result
                    is DashboardResult<AutomationCountdownControlOutcome>.Succeeded
                    {
                        Value: AutomationCountdownControlOutcome.Available
                    }
                    ? null
                    : "Countdown operation unavailable. Check the duration and channel setup.";
            }
        );

    private Task StartPollAsync() => PollAsync(false);

    private Task EndPollAsync() => PollAsync(true);

    private Task PollAsync(bool end) =>
        OperateAsync(
            async (session, host, widget, generation) =>
            {
                if (widget.Configuration is not DashboardWidgetConfiguration.Poll config)
                {
                    return;
                }
                var result = end
                    ? await _polls.EndAsync(session, host, config, false, CancellationToken.None)
                    : await _polls.StartAsync(session, host, config, CancellationToken.None);
                if (
                    result is DashboardResult<PollOperationOutcome>.Succeeded
                    {
                        Value: PollOperationOutcome.ConfirmationRequired
                    }
                )
                {
                    if (
                        !await _js.InvokeAsync<bool>(
                            "confirm",
                            "End the poll that was started elsewhere?"
                        )
                    )
                    {
                        return;
                    }
                    _ = await LoadPageContextAsync();
                    if (!Current(generation, widget, host))
                    {
                        return;
                    }
                    result = await _polls.EndAsync(
                        PageContext.Session,
                        host,
                        config,
                        true,
                        CancellationToken.None
                    );
                }
                if (!Current(generation, widget, host))
                {
                    return;
                }
                _feedback = result.Match(
                    ready =>
                        ready.Value switch
                        {
                            PollOperationOutcome.Started => "Poll started.",
                            PollOperationOutcome.Ended => "Poll ended.",
                            PollOperationOutcome.NotReady unavailable => unavailable.Message,
                            PollOperationOutcome.ProviderRejected rejected => rejected.Message,
                            PollOperationOutcome.InvalidTemplate invalid => invalid.Message,
                            PollOperationOutcome.ActivePollExists => "A poll is already active.",
                            PollOperationOutcome.TemplateNotFound => "Poll template missing.",
                            PollOperationOutcome.ConfirmationRequired => "Confirmation required.",
                            PollOperationOutcome.TemplateSaved => "Template saved.",
                            _ => throw new UnreachableException(),
                        },
                    denied => $"Access unavailable: {denied.Reason}."
                );
            }
        );

    private Task PredictionAsync(DashboardPredictionAction action) =>
        OperateAsync(
            async (session, host, widget, generation) =>
            {
                if (widget.Configuration is not DashboardWidgetConfiguration.Prediction config)
                {
                    return;
                }
                var winner = _winner;
                Task<DashboardResult<PredictionOperationOutcome>> Invoke(
                    AuthenticatedSession current,
                    bool confirmed
                ) =>
                    action switch
                    {
                        DashboardPredictionAction.Start => _predictions.StartAsync(
                            current,
                            host,
                            config,
                            CancellationToken.None
                        ),
                        DashboardPredictionAction.Lock => _predictions.LockAsync(
                            current,
                            host,
                            config,
                            confirmed,
                            CancellationToken.None
                        ),
                        DashboardPredictionAction.Cancel => _predictions.CancelAsync(
                            current,
                            host,
                            config,
                            confirmed,
                            CancellationToken.None
                        ),
                        DashboardPredictionAction.Resolve => _predictions.ResolveAsync(
                            current,
                            host,
                            config,
                            winner,
                            confirmed,
                            CancellationToken.None
                        ),
                    };
                var result = await Invoke(session, false);
                if (
                    result is DashboardResult<PredictionOperationOutcome>.Succeeded
                    {
                        Value: PredictionOperationOutcome.ConfirmationRequired
                    }
                )
                {
                    if (
                        !await _js.InvokeAsync<bool>("confirm", $"{action} the current prediction?")
                    )
                    {
                        return;
                    }
                    _ = await LoadPageContextAsync();
                    if (!Current(generation, widget, host))
                    {
                        return;
                    }
                    result = await Invoke(PageContext.Session, true);
                }
                if (!Current(generation, widget, host))
                {
                    return;
                }
                _feedback = result.Match(
                    ready =>
                        ready.Value switch
                        {
                            PredictionOperationOutcome.Started => "Prediction started.",
                            PredictionOperationOutcome.Updated => "Prediction updated.",
                            PredictionOperationOutcome.NotReady v => v.Message,
                            PredictionOperationOutcome.Ineligible v => v.Message,
                            PredictionOperationOutcome.Unavailable v => v.Message,
                            PredictionOperationOutcome.InvalidTemplate v => v.Message,
                            PredictionOperationOutcome.ProviderRejected v => v.Message,
                            PredictionOperationOutcome.ActivePredictionExists =>
                                "A prediction is already active.",
                            PredictionOperationOutcome.TemplateNotFound =>
                                "Prediction template missing.",
                            PredictionOperationOutcome.InvalidOutcome =>
                                "Choose a current winning outcome.",
                            PredictionOperationOutcome.ConfirmationRequired =>
                                "Confirmation required.",
                            PredictionOperationOutcome.TemplateSaved => "Template saved.",
                            PredictionOperationOutcome.TemplateDeleted => "Template deleted.",
                            _ => throw new UnreachableException(),
                        },
                    denied => $"Access unavailable: {denied.Reason}."
                );
            }
        );

    private Task GiveawayAsync(DashboardGiveawayAction action) =>
        OperateAsync(
            async (session, host, widget, generation) =>
            {
                if (widget.Configuration is not DashboardWidgetConfiguration.Giveaway config)
                {
                    return;
                }
                var result = await (
                    action switch
                    {
                        DashboardGiveawayAction.Start => _giveaways.StartAsync(
                            session,
                            host,
                            config,
                            CancellationToken.None
                        ),
                        DashboardGiveawayAction.End => _giveaways.EndAsync(
                            session,
                            host,
                            config,
                            CancellationToken.None
                        ),
                        DashboardGiveawayAction.Cancel => _giveaways.CancelAsync(
                            session,
                            host,
                            config,
                            CancellationToken.None
                        ),
                    }
                );
                if (!Current(generation, widget, host))
                {
                    return;
                }
                _feedback = result.Match(
                    ready =>
                        ready.Value.Match(
                            value =>
                                value.Value.Match(
                                    success => success.Message,
                                    failed => failed.Message
                                ),
                            unavailable => $"Giveaway unavailable: {unavailable.Reason}."
                        ),
                    denied => $"Access unavailable: {denied.Reason}."
                );
            }
        );

    private Task GuessingAsync(DashboardGuessingAction action) =>
        OperateAsync(
            async (session, host, widget, generation) =>
            {
                if (widget.Configuration is not DashboardWidgetConfiguration.Guessing config)
                {
                    return;
                }
                var channel = HostLogin;
                GuessingOperationOutcome? outcome;
                if (action == DashboardGuessingAction.Declare)
                {
                    var declared = await _guessingControls.DeclareWinnerAsync(
                        session,
                        host,
                        config,
                        new GuessName(_winner),
                        CancellationToken.None
                    );
                    outcome = declared
                        is DashboardResult<
                            DashboardGuessingResult<GuessingWinnerDeclarationOutcome>
                        >.Succeeded
                        {
                            Value: DashboardGuessingResult<GuessingWinnerDeclarationOutcome>.Ready
                            {
                                Value: GuessingWinnerDeclarationOutcome.Completed completed
                            }
                        }
                        ? completed.Result
                        : null;
                }
                else
                {
                    var changed =
                        action == DashboardGuessingAction.Start
                            ? await _guessingControls.StartAsync(
                                session,
                                host,
                                config,
                                CancellationToken.None
                            )
                            : await _guessingControls.StopAsync(
                                session,
                                host,
                                config,
                                CancellationToken.None
                            );
                    outcome = changed
                        is DashboardResult<
                            DashboardGuessingResult<GuessingOperationOutcome>
                        >.Succeeded
                        {
                            Value: DashboardGuessingResult<GuessingOperationOutcome>.Ready ready
                        }
                        ? ready.Value
                        : null;
                }
                // Delivery belongs to the committed action's channel even if the operator navigates meanwhile.
                var warning = outcome is null ? null : await _replies.QueueAsync(channel, outcome);
                if (!Current(generation, widget, host))
                {
                    return;
                }
                _feedback = outcome is null
                    ? "The round action did not complete. Check the current round and payout settings."
                    : warning ?? outcome.Message;
            }
        );
}

using BlokeBot.Core.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardPage
{
    private DashboardWidgetId? _selected;
    private bool _toolboxOpen;
    private bool _focusToolbox;
    private bool _focusSettings;
    private string _search = string.Empty;
    private EditorToolbox? _toolbox;
    private EditorToolboxToggle? _toggle;
    private DashboardWidget? _selectedWidget =>
        _document.Widgets.FirstOrDefault(w => w.Id == _selected);
    private IReadOnlyList<DashboardWidgetKind> _toolboxKinds =>
        Enum.GetValues<DashboardWidgetKind>()
            .Where(k =>
                DashboardWidgetKinds.Label(k).Contains(_search, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

    private Task ToggleToolboxAsync() => _toolboxOpen ? CloseToolboxAsync() : OpenToolboxAsync();

    private Task OpenToolboxAsync()
    {
        _toolboxOpen = true;
        _focusToolbox = true;
        return Task.CompletedTask;
    }

    private async Task CloseToolboxAsync()
    {
        _toolboxOpen = false;
        if (_toggle is not null)
        {
            await _toggle.FocusAsync();
        }
    }

    private Task SettingsAsync(DashboardWidgetId id)
    {
        _selected = id;
        _focusSettings = true;
        return Task.CompletedTask;
    }

    private async Task CloseSettingsAsync()
    {
        var selected = _selected;
        _selected = null;
        if (_grid is not null && selected is not null)
        {
            await _grid.InvokeVoidAsync("focusSettings", selected.Value.Value.ToString());
        }
    }

    private Task SettingsKeyAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CloseSettingsAsync() : Task.CompletedTask;

    private Task ConfigureWidget(DashboardWidget widget)
    {
        if (_editing)
        {
            _edit?.Configure(widget);
        }
        return Task.CompletedTask;
    }

    private async Task AddAsync(DashboardWidgetKind kind)
    {
        if (!_editing || _edit is null)
        {
            return;
        }
        var widget = new DashboardWidget(
            new(Guid.NewGuid()),
            DashboardWidgetKinds.Label(kind),
            _edit.FreePlacement(),
            DashboardWidgetKinds.Create(kind)
        );
        var proposal = _edit.Propose(widget, DashboardPlacementIntent.Add);
        if (proposal is not null && _edit.Apply(proposal, false))
        {
            _selected = widget.Id;
            _focusSettings = true;
        }
        await CloseToolboxAsync();
    }

    [JSInvokable]
    public async Task ToolboxDropped(
        long generation,
        long interaction,
        string kind,
        int column,
        int row
    )
    {
        if (
            !_editing
            || _busy
            || generation != _loadGeneration
            || interaction != _interactionGeneration
            || _edit is null
            || !Enum.TryParse<DashboardWidgetKind>(kind, out var parsed)
            || !Enum.IsDefined(parsed)
        )
        {
            return;
        }
        var session = _edit;
        var widget = new DashboardWidget(
            new(Guid.NewGuid()),
            DashboardWidgetKinds.Label(parsed),
            session.FreePlacement() with
            {
                Column = column,
                Row = row,
            },
            DashboardWidgetKinds.Create(parsed)
        );
        while (
            !_disposed
            && _edit == session
            && generation == _loadGeneration
            && interaction == _interactionGeneration
        )
        {
            var proposal = session.Propose(widget, DashboardPlacementIntent.Add);
            if (proposal is null)
            {
                break;
            }
            var confirmed =
                !session.NeedsConfirmation(proposal)
                || await _js.InvokeAsync<bool>(
                    "confirm",
                    $"Replace {proposal.Victims.Length} covered widget(s)? Undo can restore them before Save."
                );
            if (
                !confirmed
                || _disposed
                || _edit != session
                || generation != _loadGeneration
                || interaction != _interactionGeneration
            )
            {
                break;
            }
            if (proposal.Generation != session.Generation)
            {
                continue;
            }
            if (session.Apply(proposal, confirmed))
            {
                _selected = widget.Id;
                _focusSettings = true;
            }
            break;
        }
        if (_disposed || generation != _loadGeneration || interaction != _interactionGeneration)
        {
            return;
        }
        _localDirty = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ChangePlacementAsync(
        DashboardWidget widget,
        DashboardPlacement placement,
        bool resize
    )
    {
        if (!_editing || _edit is null)
        {
            return;
        }
        var session = _edit;
        var interaction = _interactionGeneration;
        var generation = _loadGeneration;
        while (
            !_disposed
            && _edit == session
            && interaction == _interactionGeneration
            && generation == _loadGeneration
        )
        {
            var current = session.Document.Widgets.FirstOrDefault(w => w.Id == widget.Id);
            if (current is null)
            {
                return;
            }
            var proposal = session.Propose(
                current with
                {
                    Placement = placement,
                },
                resize ? DashboardPlacementIntent.Resize : DashboardPlacementIntent.Move
            );
            if (proposal is null)
            {
                _feedback = "The widget must fit within the dashboard columns.";
                return;
            }
            var confirmed =
                !session.NeedsConfirmation(proposal)
                || await _js.InvokeAsync<bool>(
                    "confirm",
                    $"Replace {proposal.Victims.Length} covered widget(s)? Undo can restore them before Save."
                );
            if (
                !confirmed
                || _disposed
                || _edit != session
                || interaction != _interactionGeneration
                || generation != _loadGeneration
            )
            {
                return;
            }
            if (proposal.Generation != session.Generation)
            {
                continue;
            }
            if (!session.Apply(proposal, confirmed))
            {
                _feedback = "These widget slots cannot be swapped. Choose another position.";
            }
            return;
        }
    }

    private async Task GridKeyAsync(
        DashboardWidget widget,
        KeyboardEventArgs args,
        bool resize = false
    )
    {
        var p = widget.Placement;
        var next = args.Key switch
        {
            "ArrowLeft" => resize
                ? p with
                {
                    Width = Math.Max(1, p.Width - 1),
                }
                : p with
                {
                    Column = Math.Max(0, p.Column - 1),
                },
            "ArrowRight" => resize
                ? p with
                {
                    Width = p.Width + 1,
                }
                : p with
                {
                    Column = p.Column + 1,
                },
            "ArrowUp" => resize
                ? p with
                {
                    Height = Math.Max(1, p.Height - 1),
                }
                : p with
                {
                    Row = Math.Max(0, p.Row - 1),
                },
            "ArrowDown" => resize ? p with { Height = p.Height + 1 } : p with { Row = p.Row + 1 },
            _ => p,
        };
        if (next != p)
        {
            await ChangePlacementAsync(widget, next, resize);
        }
    }

    private async Task RemoveAsync(DashboardWidgetId id)
    {
        if (!_editing)
        {
            return;
        }
        await CancelGestureAsync(() =>
        {
            _edit?.Remove(id);
            if (_selected == id)
            {
                _selected = null;
            }
        });
        if (_toggle is not null)
        {
            await _toggle.FocusAsync();
        }
    }

    private async Task CancelGestureAsync(Action change)
    {
        var interaction = ++_interactionGeneration;
        _localDirty = false;
        change();
        if (_grid is not null)
        {
            await _grid.InvokeVoidAsync("cancel", interaction);
        }
    }

    private Task UndoAsync() => CancelGestureAsync(() => _edit?.Undo());

    private Task RedoAsync() => CancelGestureAsync(() => _edit?.Redo());

    private Task CancelAsync() =>
        CancelGestureAsync(() =>
        {
            _edit?.Cancel();
            _selected = null;
            _feedback = null;
        });

    private async Task SaveAsync()
    {
        if (!_editing || _edit is null || _busy)
        {
            return;
        }
        _busy = true;
        var session = _edit;
        var generation = _loadGeneration;
        var interaction = _interactionGeneration;
        try
        {
            if (_grid is not null)
            {
                var pending = await _grid.InvokeAsync<DashboardPendingPlacement[]>("flush");
                foreach (var item in pending.OrderBy(p => p.Sequence))
                {
                    await PlacementChanged(
                        generation,
                        item.Interaction,
                        item.Id,
                        item.Placement,
                        item.Resize,
                        item.Sequence
                    );
                }
            }
            if (
                _disposed
                || generation != _loadGeneration
                || interaction != _interactionGeneration
                || _edit != session
            )
            {
                return;
            }
            var host = new DashboardHostId(HostId);
            _ = await LoadPageContextAsync();
            if (
                _disposed
                || generation != _loadGeneration
                || interaction != _interactionGeneration
                || _edit != session
                || HostId != host.Value
            )
            {
                return;
            }
            var outcome = await _configuration.SaveAsync(
                PageContext.Session,
                session.Command(host),
                CancellationToken.None
            );
            if (
                _disposed
                || generation != _loadGeneration
                || interaction != _interactionGeneration
                || _edit != session
                || HostId != host.Value
            )
            {
                return;
            }
            switch (outcome)
            {
                case DashboardSaveOutcome.Saved saved:
                    await CancelGestureAsync(() =>
                    {
                        session.Accept(saved.Dashboard);
                        _snapshot = saved.Dashboard;
                        _feedback = "Dashboard saved.";
                    });
                    break;
                case DashboardSaveOutcome.Conflict conflict:
                    session.PreserveConflict(conflict.Current);
                    _feedback =
                        "Someone else saved this dashboard. Your unsaved changes are still here.";
                    break;
                case DashboardSaveOutcome.Rejected rejected:
                    _feedback = $"Dashboard not saved: {rejected.Reason}.";
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ReloadConflictAsync()
    {
        var session = _edit;
        var generation = _loadGeneration;
        var interaction = _interactionGeneration;
        if (
            session?.Conflict is { } current
            && await _js.InvokeAsync<bool>(
                "confirm",
                "Discard your local changes and load the saved dashboard?"
            )
            && !_disposed
            && generation == _loadGeneration
            && interaction == _interactionGeneration
            && _edit == session
        )
        {
            await CancelGestureAsync(() =>
            {
                session.Accept(current);
                _snapshot = current;
                _feedback = null;
            });
        }
    }
}

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
            DashboardWidgetKinds.Create(kind)
        );
        if (
            _edit.FreePlacement(_arrangementKind) is { } placement
            && _edit.Add(widget, _arrangementKind, placement)
        )
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
            || _disposed
            || generation != _loadGeneration
            || interaction != _interactionGeneration
            || _edit is null
            || !Enum.TryParse<DashboardWidgetKind>(kind, out var parsed)
            || !Enum.IsDefined(parsed)
        )
        {
            return;
        }
        var widget = new DashboardWidget(
            new(Guid.NewGuid()),
            DashboardWidgetKinds.Label(parsed),
            DashboardWidgetKinds.Create(parsed)
        );
        var free = _edit.FreePlacement(_arrangementKind);
        if (free is null)
        {
            _localDirty = false;
            return;
        }
        var placement = free with { Column = column, Row = row };
        if (await ApplyPlacementAsync(_edit.ProposeAdd(widget, _arrangementKind, placement)))
        {
            _selected = widget.Id;
            _focusSettings = true;
        }
        else
        {
            _feedback = "Choose a position within the dashboard columns.";
        }
        _localDirty = false;
        await InvokeAsync(StateHasChanged);
    }

    private bool _confirming;

    private Task<bool> ChangePlacementAsync(
        DashboardWidget widget,
        DashboardPlacement placement,
        bool resize
    ) => ApplyPlacementAsync(_edit?.Propose(_arrangementKind, widget.Id, placement, resize));

    private async Task<bool> ApplyPlacementAsync(DashboardPlacementProposal? proposal)
    {
        if (!_editing || _edit is null || _disposed || _loading || _confirming)
        {
            return false;
        }
        if (proposal is null)
        {
            _feedback = "Choose a position within the dashboard columns.";
            return false;
        }
        var edit = _edit;
        var confirmed = false;
        if (proposal.NeedsConfirmation)
        {
            _confirming = true;
            var context = _editorContext;
            var names = proposal.Victims.Select(id =>
                $"“{edit.Document.Widgets.Single(w => w.Id == id).Label}”"
            );
            try
            {
                confirmed = await _js.InvokeAsync<bool>(
                    "confirm",
                    $"Replace {string.Join(", ", names)}? These widgets will be removed from both Desktop and Phone arrangements. Their underlying controls are not deleted. Undo can restore the widgets before Save."
                );
                if (!confirmed || _edit != edit || context != _editorContext)
                {
                    return false;
                }
            }
            finally
            {
                _confirming = false;
                if (!_disposed)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        var applied = edit.Apply(proposal, confirmed);
        if (applied)
        {
            if (_selected is { } selected && !edit.Document.Widgets.Any(w => w.Id == selected))
            {
                _selected = null;
            }
            _feedback = null;
        }
        return applied;
    }

    private async Task GridKeyAsync(
        DashboardWidget widget,
        KeyboardEventArgs args,
        bool resize = false
    )
    {
        var p = Placement(widget.Id);
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
            _ = await ChangePlacementAsync(widget, next, resize);
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

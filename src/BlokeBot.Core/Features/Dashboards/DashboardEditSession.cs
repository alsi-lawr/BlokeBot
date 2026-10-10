using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Dashboards;

internal enum DashboardPlacementIntent
{
    Move,
    Resize,
    Add,
}

internal sealed record DashboardPlacementProposal(
    long Generation,
    DashboardWidget Widget,
    DashboardPlacementIntent Intent,
    ImmutableArray<DashboardWidgetId> Victims
);

internal sealed class DashboardEditSession(DashboardSnapshot baseline)
{
    private readonly Stack<(string Name, DashboardDocument Document)> _undo = [];
    private readonly Stack<(string Name, DashboardDocument Document)> _redo = [];
    internal DashboardSnapshot Baseline { get; private set; } = baseline;
    internal string Name { get; private set; } = baseline.Name;
    internal DashboardDocument Document { get; private set; } = baseline.Document;
    internal long Generation { get; private set; }
    internal bool Dirty =>
        Name != Baseline.Name || !Document.Widgets.SequenceEqual(Baseline.Document.Widgets);
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;
    internal DashboardSnapshot? Conflict { get; private set; }

    internal void Rename(string name)
    {
        if (name != Name)
        {
            Remember();
            Name = name;
        }
    }

    internal void Configure(DashboardWidget widget)
    {
        var index = Index(Document.Widgets, widget.Id);
        if (index < 0 || Document.Widgets[index] == widget)
        {
            return;
        }
        Remember();
        Document = Document with { Widgets = Document.Widgets.SetItem(index, widget) };
    }

    internal void Remove(DashboardWidgetId id)
    {
        var widgets = Document.Widgets.Where(w => w.Id != id).ToImmutableArray();
        if (widgets.Length == Document.Widgets.Length)
        {
            return;
        }
        Remember();
        Document = Document with { Widgets = widgets };
    }

    internal DashboardPlacementProposal? Propose(
        DashboardWidget widget,
        DashboardPlacementIntent intent
    )
    {
        if (!WithinBounds(widget.Placement))
        {
            return null;
        }
        var victims = Document
            .Widgets.Where(w => w.Id != widget.Id && Overlaps(w.Placement, widget.Placement))
            .Select(w => w.Id)
            .ToImmutableArray();
        return new(Generation, widget, intent, victims);
    }

    internal bool NeedsConfirmation(DashboardPlacementProposal proposal) =>
        !proposal.Victims.IsEmpty
        && (proposal.Intent != DashboardPlacementIntent.Move || proposal.Victims.Length != 1);

    internal bool Apply(DashboardPlacementProposal proposal, bool overwrite)
    {
        if (proposal.Generation != Generation || !WithinBounds(proposal.Widget.Placement))
        {
            return false;
        }
        var current = Propose(proposal.Widget, proposal.Intent);
        if (
            current is null
            || !current.Victims.SequenceEqual(proposal.Victims)
            || (NeedsConfirmation(proposal) && !overwrite)
        )
        {
            return false;
        }
        var widgets = Document.Widgets;
        var index = Index(widgets, proposal.Widget.Id);
        if (
            proposal.Intent == DashboardPlacementIntent.Move
            && proposal.Victims.Length == 1
            && index >= 0
        )
        {
            var victimIndex = Index(widgets, proposal.Victims[0]);
            var moving = widgets[index];
            var victim = widgets[victimIndex];
            var swappedMoving = moving with { Placement = victim.Placement };
            var swappedVictim = victim with { Placement = moving.Placement };
            if (
                !WithinBounds(swappedMoving.Placement)
                || !WithinBounds(swappedVictim.Placement)
                || widgets.Any(w =>
                    w.Id != moving.Id
                    && w.Id != victim.Id
                    && (
                        Overlaps(w.Placement, swappedMoving.Placement)
                        || Overlaps(w.Placement, swappedVictim.Placement)
                    )
                )
            )
            {
                return false;
            }
            Remember();
            Document = Document with
            {
                Widgets = widgets.SetItem(index, swappedMoving).SetItem(victimIndex, swappedVictim),
            };
            return true;
        }
        var next = widgets
            .Where(w => !proposal.Victims.Contains(w.Id) && w.Id != proposal.Widget.Id)
            .Append(proposal.Widget)
            .OrderBy(w => w.Placement.Order)
            .ToImmutableArray();
        if (widgets.SequenceEqual(next))
        {
            return true;
        }
        Remember();
        Document = Document with { Widgets = next };
        return true;
    }

    internal DashboardPlacement FreePlacement()
    {
        var width = Math.Min(6, Document.Columns);
        for (var row = 0; ; row++)
        {
            for (var column = 0; column <= Document.Columns - width; column++)
            {
                var placement = new DashboardPlacement(
                    column,
                    row,
                    width,
                    4,
                    Document.Widgets.IsEmpty ? 0 : Document.Widgets.Max(w => w.Placement.Order) + 1
                );
                if (!Document.Widgets.Any(w => Overlaps(w.Placement, placement)))
                {
                    return placement;
                }
            }
        }
    }

    internal void Undo()
    {
        if (_undo.TryPop(out var state))
        {
            _redo.Push((Name, Document));
            (Name, Document) = state;
            Generation++;
            Conflict = null;
        }
    }

    internal void Redo()
    {
        if (_redo.TryPop(out var state))
        {
            _undo.Push((Name, Document));
            (Name, Document) = state;
            Generation++;
            Conflict = null;
        }
    }

    internal void Cancel() => Accept(Baseline);

    internal void Accept(DashboardSnapshot saved)
    {
        Baseline = saved;
        Name = saved.Name;
        Document = saved.Document;
        _undo.Clear();
        _redo.Clear();
        Conflict = null;
        Generation++;
    }

    internal void PreserveConflict(DashboardSnapshot current) => Conflict = current;

    internal SaveDashboardCommand Command(DashboardHostId host) =>
        new(host, Baseline.Id, Baseline.Revision, Name, Document);

    private static int Index(ImmutableArray<DashboardWidget> widgets, DashboardWidgetId id)
    {
        for (var i = 0; i < widgets.Length; i++)
        {
            if (widgets[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    private void Remember()
    {
        _undo.Push((Name, Document));
        _redo.Clear();
        Generation++;
        Conflict = null;
    }

    private bool WithinBounds(DashboardPlacement p) =>
        p.Column >= 0
        && p.Row >= 0
        && p.Width > 0
        && p.Height > 0
        && (long)p.Column + p.Width <= Document.Columns;

    internal static bool Overlaps(DashboardPlacement a, DashboardPlacement b) =>
        a.Column < (long)b.Column + b.Width
        && b.Column < (long)a.Column + a.Width
        && a.Row < (long)b.Row + b.Height
        && b.Row < (long)a.Row + a.Height;
}

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
    DashboardArrangementKind Arrangement,
    DashboardWidget Widget,
    DashboardPlacement Placement,
    DashboardPlacementIntent Intent,
    ImmutableArray<DashboardWidgetId> Victims,
    bool Swap
)
{
    internal bool NeedsConfirmation => !Victims.IsEmpty && !Swap;
}

internal sealed class DashboardEditSession(DashboardSnapshot baseline)
{
    private readonly Stack<(string Name, DashboardDocument Document)> _undo = [];
    private readonly Stack<(string Name, DashboardDocument Document)> _redo = [];
    internal DashboardSnapshot Baseline { get; private set; } = baseline;
    internal string Name { get; private set; } = baseline.Name;
    internal DashboardDocument Document { get; private set; } = baseline.Document;
    internal long Generation { get; private set; }
    internal bool Dirty =>
        Name != Baseline.Name || !DashboardArrangements.Equivalent(Document, Baseline.Document);
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

    internal bool SetGrid(DashboardArrangementKind kind, int columns, int cellHeight)
    {
        var arrangement = Document.Arrangement(kind);
        var next = arrangement with { Columns = columns, CellHeight = cellHeight };
        if (!DashboardArrangements.Valid(next, Document.Widgets.Select(w => w.Id).ToArray()))
        {
            return false;
        }
        if (next == arrangement)
        {
            return true;
        }
        Remember();
        Document = Document.WithArrangement(kind, next);
        return true;
    }

    internal void Remove(DashboardWidgetId id)
    {
        if (!Document.Widgets.Any(w => w.Id == id))
        {
            return;
        }
        Remember();
        Document = Without(Document, [id]);
    }

    internal bool Add(
        DashboardWidget widget,
        DashboardArrangementKind kind,
        DashboardPlacement placement
    ) => ProposeAdd(widget, kind, placement) is { } proposal && Apply(proposal);

    internal DashboardPlacementProposal? ProposeAdd(
        DashboardWidget widget,
        DashboardArrangementKind kind,
        DashboardPlacement placement
    ) => Propose(widget, kind, placement, DashboardPlacementIntent.Add);

    internal DashboardPlacementProposal? Propose(
        DashboardArrangementKind kind,
        DashboardWidgetId id,
        DashboardPlacement placement,
        bool resize = false
    ) =>
        Document.Widgets.FirstOrDefault(w => w.Id == id) is { } widget
            ? Propose(
                widget,
                kind,
                placement,
                resize ? DashboardPlacementIntent.Resize : DashboardPlacementIntent.Move
            )
            : null;

    private DashboardPlacementProposal? Propose(
        DashboardWidget widget,
        DashboardArrangementKind kind,
        DashboardPlacement placement,
        DashboardPlacementIntent intent
    )
    {
        var arrangement = Document.Arrangement(kind);
        var adding = intent == DashboardPlacementIntent.Add;
        var exists = Document.Widgets.Any(w => w.Id == widget.Id);
        if (!DashboardArrangements.WithinBounds(placement, arrangement.Columns) || adding == exists)
        {
            return null;
        }
        var victims = arrangement
            .Placements.Where(p =>
                p.WidgetId != widget.Id && DashboardArrangements.Overlaps(p.Placement, placement)
            )
            .Select(p => p.WidgetId)
            .ToImmutableArray();
        var swap =
            intent == DashboardPlacementIntent.Move
            && victims.Length == 1
            && CanSwap(arrangement, widget.Id, placement, victims[0]);
        return new(Generation, kind, widget, placement, intent, victims, swap);
    }

    private static bool CanSwap(
        DashboardArrangement arrangement,
        DashboardWidgetId id,
        DashboardPlacement placement,
        DashboardWidgetId victim
    )
    {
        var original = arrangement.Placements.Single(p => p.WidgetId == id).Placement;
        var covered = arrangement.Placements.Single(p => p.WidgetId == victim).Placement;
        if (placement.Width != original.Width || placement.Height != original.Height)
        {
            return false;
        }
        var movedVictim = covered with { Column = original.Column, Row = original.Row };
        // Swap positions only: never borrow another widget's dimensions or order.
        var swapped = arrangement with
        {
            Placements =
            [
                .. arrangement.Placements.Select(p =>
                    p.WidgetId == id
                        ? new DashboardWidgetPlacement(
                            id,
                            placement with
                            {
                                Column = covered.Column,
                                Row = covered.Row,
                            }
                        )
                    : p.WidgetId == victim ? new DashboardWidgetPlacement(victim, movedVictim)
                    : p
                ),
            ],
        };
        return DashboardArrangements.Valid(
            swapped,
            arrangement.Placements.Select(p => p.WidgetId).ToArray()
        );
    }

    internal bool Apply(DashboardPlacementProposal proposal, bool confirmed = false)
    {
        var current = Propose(
            proposal.Widget,
            proposal.Arrangement,
            proposal.Placement,
            proposal.Intent
        );
        if (
            proposal.Generation != Generation
            || current is null
            || current.Swap != proposal.Swap
            || !current.Victims.SequenceEqual(proposal.Victims)
            || (proposal.NeedsConfirmation && !confirmed)
        )
        {
            return false;
        }
        var next = proposal.Swap ? Document : Without(Document, proposal.Victims);
        var arrangement = next.Arrangement(proposal.Arrangement);
        var placement = proposal.Placement;
        if (proposal.Swap)
        {
            var original = arrangement
                .Placements.Single(p => p.WidgetId == proposal.Widget.Id)
                .Placement;
            var victim = proposal.Victims[0];
            var covered = arrangement.Placements.Single(p => p.WidgetId == victim).Placement;
            placement = placement with { Column = covered.Column, Row = covered.Row };
            arrangement = arrangement with
            {
                Placements =
                [
                    .. arrangement.Placements.Select(p =>
                        p.WidgetId == victim
                            ? p with
                            {
                                Placement = p.Placement with
                                {
                                    Column = original.Column,
                                    Row = original.Row,
                                },
                            }
                            : p
                    ),
                ],
            };
        }
        if (proposal.Intent == DashboardPlacementIntent.Add)
        {
            var otherKind =
                proposal.Arrangement == DashboardArrangementKind.Desktop
                    ? DashboardArrangementKind.Phone
                    : DashboardArrangementKind.Desktop;
            var other = next.Arrangement(otherKind);
            var linked = DashboardArrangements.FirstFit(
                other,
                Math.Min(proposal.Placement.Width, other.Columns),
                proposal.Placement.Height,
                proposal.Placement.Order
            );
            if (linked is null)
            {
                return false;
            }
            next = (next with { Widgets = next.Widgets.Add(proposal.Widget) }).WithArrangement(
                otherKind,
                other with
                {
                    Placements = other.Placements.Add(new(proposal.Widget.Id, linked)),
                }
            );
            arrangement = arrangement with
            {
                Placements = arrangement.Placements.Add(
                    new(proposal.Widget.Id, proposal.Placement)
                ),
            };
        }
        else
        {
            arrangement = arrangement with
            {
                Placements =
                [
                    .. arrangement.Placements.Select(p =>
                        p.WidgetId == proposal.Widget.Id ? p with { Placement = placement } : p
                    ),
                ],
            };
        }
        next = next.WithArrangement(proposal.Arrangement, arrangement);
        var ids = next.Widgets.Select(w => w.Id).ToArray();
        if (
            !DashboardArrangements.Valid(next.Desktop, ids)
            || !DashboardArrangements.Valid(next.Phone!, ids)
        )
        {
            return false;
        }
        if (DashboardArrangements.Equivalent(next, Document))
        {
            return true;
        }
        Remember();
        Document = next;
        return true;
    }

    private static DashboardDocument Without(
        DashboardDocument document,
        ImmutableArray<DashboardWidgetId> ids
    ) =>
        document with
        {
            Widgets = [.. document.Widgets.Where(w => !ids.Contains(w.Id))],
            Desktop = document.Desktop with
            {
                Placements = [.. document.Desktop.Placements.Where(p => !ids.Contains(p.WidgetId))],
            },
            Phone = document.Phone! with
            {
                Placements = [.. document.Phone.Placements.Where(p => !ids.Contains(p.WidgetId))],
            },
        };

    internal DashboardPlacement? FreePlacement(DashboardArrangementKind kind) =>
        DashboardArrangements.FirstFit(
            Document.Arrangement(kind),
            Math.Min(6, Document.Arrangement(kind).Columns),
            4,
            Document.Widgets.Length
        );

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
}

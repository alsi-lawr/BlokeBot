using System.Collections.Immutable;
using BlokeBot.Core.Features.Dashboards;
using Shouldly;

namespace BlokeBot.Core.Tests;

public class DashboardEditSessionTests
{
    [Test]
    public void Swap_UsesCompleteSlots_AndUndoRestoresSettings()
    {
        var a = Widget("A", new(0, 0, 4, 3, 0));
        var b = Widget("B", new(6, 0, 6, 5, 1));
        var session = Session(a, b);
        var proposal = session.Propose(
            a with
            {
                Placement = a.Placement with { Column = 6 },
            },
            DashboardPlacementIntent.Move
        )!;
        session.NeedsConfirmation(proposal).ShouldBeFalse();
        session.Apply(proposal, false).ShouldBeTrue();
        session.Document.Widgets.Single(w => w.Id == a.Id).Placement.ShouldBe(b.Placement);
        session.Document.Widgets.Single(w => w.Id == b.Id).Placement.ShouldBe(a.Placement);
        session.Undo();
        session.Document.Widgets.ShouldBe(new[] { a, b });
        session.Redo();
        session.Dirty.ShouldBeTrue();
        session.Cancel();
        session.Document.Widgets.ShouldBe(new[] { a, b });
        session.CanUndo.ShouldBeFalse();
        session.CanRedo.ShouldBeFalse();
    }

    [Test]
    public void Overwrite_RequiresCurrentConfirmation_RemainsLocal_AndUndoRestoresVictims()
    {
        var a = Widget("A", new(0, 0, 4, 3, 0));
        var b = Widget("B", new(4, 0, 4, 3, 1));
        var c = Widget("C", new(8, 0, 4, 3, 2));
        var session = Session(a, b, c);
        var incoming = Widget("New", new(0, 0, 8, 3, 3));
        var proposal = session.Propose(incoming, DashboardPlacementIntent.Add)!;
        session.Apply(proposal, false).ShouldBeFalse();
        session.Document.Widgets.ShouldBe(new[] { a, b, c });
        session.Configure(c with { Label = "Retained changed label" });
        session.Apply(proposal, true).ShouldBeFalse();
        session
            .Apply(session.Propose(incoming, DashboardPlacementIntent.Add)!, true)
            .ShouldBeTrue();
        session.Document.Widgets.Select(w => w.Id).ShouldBe([c.Id, incoming.Id]);
        session.Baseline.Document.Widgets.ShouldBe(new[] { a, b, c });
        session.Undo();
        session.Document.Widgets.Select(w => w.Id).ShouldBe([a.Id, b.Id, c.Id]);
        session.Document.Widgets.Last().Label.ShouldBe("Retained changed label");
    }

    [Test]
    public void ResizeMultiCollision_DeclinedKeepsState_ConflictAndSaveEndHistory()
    {
        var a = Widget("A", new(0, 0, 4, 3, 0));
        var b = Widget("B", new(4, 0, 4, 3, 1));
        var c = Widget("C", new(8, 0, 4, 3, 2));
        var session = Session(a, b, c);
        var proposal = session.Propose(
            a with
            {
                Placement = a.Placement with { Width = 12 },
            },
            DashboardPlacementIntent.Resize
        )!;
        session.NeedsConfirmation(proposal).ShouldBeTrue();
        session.Apply(proposal, false).ShouldBeFalse();
        session.Dirty.ShouldBeFalse();
        session.Apply(proposal, true).ShouldBeTrue();
        session.Document.Widgets.Single().Id.ShouldBe(a.Id);
        var current = session.Baseline with { Revision = new(2), Name = "Someone else's name" };
        session.PreserveConflict(current);
        session.Name.ShouldNotBe(current.Name);
        session.Dirty.ShouldBeTrue();
        session.CanUndo.ShouldBeTrue();
        session.Accept(current);
        session.Dirty.ShouldBeFalse();
        session.CanUndo.ShouldBeFalse();
        session.CanRedo.ShouldBeFalse();
    }

    private static DashboardEditSession Session(params DashboardWidget[] widgets) =>
        new(new(new(Guid.NewGuid()), "Controls", new(1), new(12, widgets.ToImmutableArray())));

    private static DashboardWidget Widget(string label, DashboardPlacement placement) =>
        new(
            new(Guid.NewGuid()),
            label,
            placement,
            new DashboardWidgetConfiguration.Automation(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected),
                new DashboardTextOverride.Replace(label)
            )
        );
}

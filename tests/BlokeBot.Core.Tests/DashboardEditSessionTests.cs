using BlokeBot.Core.Features.Dashboards;
using Shouldly;

namespace BlokeBot.Core.Tests;

public class DashboardEditSessionTests
{
    [Test]
    public void BothArrangementsAndSharedConfigurationTravelTogetherThroughHistory()
    {
        var a = Widget("A");
        var session = Session((a, new(0, 0, 4, 3, 0)));
        session.SetGrid(DashboardArrangementKind.Desktop, 8, 72).ShouldBeTrue();
        session.SetGrid(DashboardArrangementKind.Phone, 3, 64).ShouldBeFalse();
        session
            .Apply(session.Propose(DashboardArrangementKind.Phone, a.Id, new(0, 8, 3, 5, 0))!)
            .ShouldBeTrue();
        session.SetGrid(DashboardArrangementKind.Phone, 3, 64).ShouldBeTrue();
        session.Configure(a with { Label = "Shared label" });
        session.Rename("Changed");
        var changed = session.Document;
        session.Dirty.ShouldBeTrue();
        for (var i = 0; i < 5; i++)
        {
            session.Undo();
        }
        session.Dirty.ShouldBeFalse();
        session.Name.ShouldBe("Controls");
        for (var i = 0; i < 5; i++)
        {
            session.Redo();
        }
        DashboardArrangements.Equivalent(session.Document, changed).ShouldBeTrue();
        session.Cancel();
        session.Dirty.ShouldBeFalse();
        session.CanUndo.ShouldBeFalse();
        session.CanRedo.ShouldBeFalse();
    }

    [Test]
    public void RefusedShrinkPreservesExactDocumentDirtyAndRedo()
    {
        var a = Widget("A");
        var session = Session((a, new(6, 0, 6, 5, 0)));
        session.Rename("Unsaved");
        session.SetGrid(DashboardArrangementKind.Phone, 5, 80).ShouldBeTrue();
        session.Undo();
        var document = session.Document;
        var generation = session.Generation;
        session.SetGrid(DashboardArrangementKind.Desktop, 8, 72).ShouldBeFalse();
        ReferenceEquals(document, session.Document).ShouldBeTrue();
        session.Generation.ShouldBe(generation);
        session.Dirty.ShouldBeTrue();
        session.CanRedo.ShouldBeTrue();
        session.Redo();
        session.Document.Phone!.CellHeight.ShouldBe(80);
    }

    [Test]
    public void SharedAddAndRemoveAreSingleHistoryStepsWithoutMovingInactiveWidgets()
    {
        var a = Widget("A");
        var b = Widget("B");
        var session = Session((a, new(6, 0, 6, 5, 0)));
        var originalPhone = session.Document.Phone!;
        session.Add(b, DashboardArrangementKind.Desktop, new(0, 10, 6, 3, 1)).ShouldBeTrue();
        session.Document.Phone!.Placements[0].ShouldBe(originalPhone.Placements[0]);
        session.Document.Phone.Placements[1].WidgetId.ShouldBe(b.Id);
        session.Document.Phone.Placements[1].Placement.ShouldBe(new(0, 5, 4, 3, 1));
        DashboardDocuments.Valid(session.Document).ShouldBeTrue();
        session.Undo();
        session.Dirty.ShouldBeFalse();
        session.Redo();
        var added = session.Document;
        session.Remove(a.Id);
        session.Document.Widgets.Single().Id.ShouldBe(b.Id);
        session.Document.Desktop.Placements.Single().WidgetId.ShouldBe(b.Id);
        session.Document.Phone.Placements.Single().WidgetId.ShouldBe(b.Id);
        session.Undo();
        DashboardArrangements.Equivalent(session.Document, added).ShouldBeTrue();
    }

    [Test]
    public void MissingPhoneInitializesOnceAndSavedPhoneNeverRegenerates()
    {
        var a = Widget("A");
        var b = Widget("B");
        var source = DashboardDocument.Create(
            12,
            (a, new(6, 4, 6, 5, 2)),
            (b, new(0, 0, 3, 2, 1))
        ) with
        {
            Phone = null,
        };
        var initialized = DashboardDocuments.Deserialize(DashboardDocuments.Serialize(source));
        initialized.Desktop.Placements.ShouldBe(source.Desktop.Placements);
        initialized.Phone!.Placements.Select(p => p.WidgetId).ShouldBe([a.Id, b.Id]);
        initialized
            .Phone.Placements.Select(p => p.Placement)
            .ShouldBe([new(0, 0, 4, 5, 2), new(0, 5, 3, 2, 1)]);
        var session = new DashboardEditSession(
            new(new(Guid.NewGuid()), "Controls", new(1), initialized)
        );
        session
            .Apply(session.Propose(DashboardArrangementKind.Phone, b.Id, new(1, 20, 3, 4, 1))!)
            .ShouldBeTrue();
        var reopened = DashboardDocuments.Deserialize(
            DashboardDocuments.Serialize(session.Document)
        );
        DashboardArrangements.Equivalent(reopened, session.Document).ShouldBeTrue();
    }

    [Test]
    public void GeometryEditsDoNotLoseWidgetsAndOldProposalsCannotCrossHistoryBoundary()
    {
        var a = Widget("A");
        var b = Widget("B");
        var session = Session((a, new(0, 0, 4, 3, 0)), (b, new(4, 0, 4, 3, 1)));
        var collision = session.Propose(
            DashboardArrangementKind.Desktop,
            a.Id,
            new(0, 0, 8, 3, 0),
            true
        )!;
        collision.NeedsConfirmation.ShouldBeTrue();
        session.Apply(collision).ShouldBeFalse();
        var old = session.Propose(DashboardArrangementKind.Desktop, a.Id, new(0, 10, 4, 3, 0))!;
        session.SetGrid(DashboardArrangementKind.Phone, 5, 72).ShouldBeTrue();
        session.Apply(old).ShouldBeFalse();
        session.Document.Widgets.ShouldBe([a, b]);
        session.Document.Desktop.Placements[0].Placement.ShouldBe(new(0, 0, 4, 3, 0));
    }

    [Test]
    public void IndependentGeometryValidationRejectsOrphansOverlapAndOverflow()
    {
        var a = Widget("A");
        var b = Widget("B");
        var document = DashboardDocument.Create(
            12,
            (a, new(0, 0, 4, 3, 0)),
            (b, new(4, 0, 4, 3, 1))
        );
        DashboardDocuments
            .Valid(
                document with
                {
                    Phone = document.Phone! with
                    {
                        Placements = document.Phone.Placements.RemoveAt(0),
                    },
                }
            )
            .ShouldBeFalse();
        DashboardDocuments
            .Valid(
                document with
                {
                    Desktop = document.Desktop with
                    {
                        Placements = document.Desktop.Placements.SetItem(
                            1,
                            new(b.Id, new(2, 0, 4, 3, 1))
                        ),
                    },
                }
            )
            .ShouldBeFalse();
        DashboardDocuments
            .Valid(
                document with
                {
                    Phone = document.Phone! with
                    {
                        Placements = document.Phone.Placements.SetItem(
                            1,
                            new(b.Id, new(0, int.MaxValue, 4, 2, 1))
                        ),
                    },
                }
            )
            .ShouldBeFalse();
        DashboardDocuments.Valid(document).ShouldBeTrue();
    }

    [Test]
    public void ConfirmedReplacementRemovesExactlyReviewedSharedWidgetsInOneHistoryStep()
    {
        var a = Widget("A");
        var b = Widget("B");
        var c = Widget("C");
        var session = Session(
            (a, new(0, 0, 4, 3, 0)),
            (b, new(4, 0, 4, 3, 1)),
            (c, new(8, 0, 4, 3, 2))
        );
        var original = session.Document;
        var proposal = session.Propose(
            DashboardArrangementKind.Desktop,
            a.Id,
            new(0, 0, 8, 3, 0),
            true
        )!;
        proposal.Victims.ShouldBe([b.Id]);
        session.Apply(proposal).ShouldBeFalse();
        session.Document.ShouldBeSameAs(original);
        session.CanUndo.ShouldBeFalse();
        session.Apply(proposal, confirmed: true).ShouldBeTrue();
        session.Document.Widgets.ShouldBe([a, c]);
        session.Document.Phone!.Placements.ShouldBe(
            original.Phone!.Placements.Where(p => p.WidgetId != b.Id)
        );
        var replaced = session.Document;
        session.Undo();
        session.Document.ShouldBeSameAs(original);
        session.Dirty.ShouldBeFalse();
        session.CanUndo.ShouldBeFalse();
        session.Redo();
        session.Document.ShouldBeSameAs(replaced);
        session.Apply(proposal, confirmed: true).ShouldBeFalse();
    }

    [Test]
    public void SafeSwapsPreserveDimensionsAndInactiveArrangement_UnsafeSwapsRequireConfirmation()
    {
        var a = Widget("A");
        var b = Widget("B");
        var c = Widget("C");
        var session = Session((a, new(0, 0, 3, 2, 7)), (b, new(6, 0, 4, 3, 9)));
        var phone = session.Document.Phone;
        var swap = session.Propose(DashboardArrangementKind.Desktop, a.Id, new(6, 0, 3, 2, 7))!;
        swap.Swap.ShouldBeTrue();
        swap.NeedsConfirmation.ShouldBeFalse();
        session.Apply(swap).ShouldBeTrue();
        session
            .Document.Desktop.Placements.Select(p => p.Placement)
            .ShouldBe([new(6, 0, 3, 2, 7), new(0, 0, 4, 3, 9)]);
        session.Document.Phone.ShouldBeSameAs(phone);
        session.Undo();
        session.Add(c, DashboardArrangementKind.Desktop, new(3, 0, 2, 2, 11)).ShouldBeTrue();
        var unsafeSwap = session.Propose(
            DashboardArrangementKind.Desktop,
            a.Id,
            new(6, 0, 3, 2, 7)
        )!;
        unsafeSwap.Swap.ShouldBeFalse();
        unsafeSwap.NeedsConfirmation.ShouldBeTrue();
        session.Apply(unsafeSwap).ShouldBeFalse();
        session.Document.Widgets.ShouldBe([a, b, c]);
    }

    [Test]
    public void OverlappingAddLinksOneFreshWidgetAndPreservesInactiveSurvivors_StaleApprovalDoesNothing()
    {
        var a = Widget("A");
        var b = Widget("B");
        var added = Widget("Added");
        var session = Session((a, new(0, 0, 4, 3, 0)), (b, new(8, 0, 4, 3, 1)));
        var proposal = session.ProposeAdd(
            added,
            DashboardArrangementKind.Desktop,
            new(0, 0, 6, 4, 2)
        )!;
        session.Rename("Unsaved");
        var before = session.Document;
        session.Apply(proposal, confirmed: true).ShouldBeFalse();
        session.Document.ShouldBeSameAs(before);
        session.Undo();
        session.CanRedo.ShouldBeTrue();
        var current = session.ProposeAdd(
            added,
            DashboardArrangementKind.Desktop,
            new(0, 0, 6, 4, 2)
        )!;
        session.Apply(current).ShouldBeFalse();
        session.CanRedo.ShouldBeTrue();
        session.Apply(current, confirmed: true).ShouldBeTrue();
        session.Document.Widgets.ShouldBe([b, added]);
        session
            .Document.Phone!.Placements.First(p => p.WidgetId == b.Id)
            .ShouldBe(before.Phone!.Placements.First(p => p.WidgetId == b.Id));
        session.Document.Phone.Placements.Count(p => p.WidgetId == added.Id).ShouldBe(1);
        DashboardDocuments.Valid(session.Document).ShouldBeTrue();
        session
            .ProposeAdd(Widget("Invalid"), DashboardArrangementKind.Desktop, new(10, 0, 6, 4, 3))
            .ShouldBeNull();
    }

    [Test]
    public void ApprovalCannotCoverChangedVictimConfigurationOrAChangedVictimSet()
    {
        var a = Widget("A");
        var b = Widget("B");
        var c = Widget("C");
        var session = Session(
            (a, new(0, 0, 4, 3, 0)),
            (b, new(4, 0, 4, 3, 1)),
            (c, new(8, 0, 4, 3, 2))
        );
        var proposal = session.Propose(
            DashboardArrangementKind.Desktop,
            a.Id,
            new(0, 0, 12, 3, 0),
            true
        )!;
        proposal.Victims.ShouldBe([b.Id, c.Id]);
        session.Configure(
            b with
            {
                Label = "Different reviewed widget",
                Configuration = b.Configuration with
                {
                    Binding = new DashboardBinding.Unconfigured(
                        DashboardBindingIssue.NotSelected,
                        "Changed target"
                    ),
                },
            }
        );
        var changed = session.Document;
        session.Apply(proposal, confirmed: true).ShouldBeFalse();
        session.Document.ShouldBeSameAs(changed);
        var current = session.Propose(
            DashboardArrangementKind.Desktop,
            a.Id,
            new(0, 0, 12, 3, 0),
            true
        )!;
        session.Remove(c.Id);
        var removed = session.Document;
        session.Apply(current, confirmed: true).ShouldBeFalse();
        session.Document.ShouldBeSameAs(removed);
        session.Undo();
        session.Document.ShouldBeSameAs(changed);
    }

    [Test]
    public void LayoutEditingPreservesIncompleteSharedConfigurationUntilSaveValidation()
    {
        var a = Widget("A");
        var session = Session((a, new(0, 0, 4, 3, 0)));
        var incomplete = a with
        {
            Configuration = new DashboardWidgetConfiguration.Value(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected),
                new DashboardValueSelection.DictionaryEntry(string.Empty)
            ),
        };
        session.Configure(incomplete);
        session
            .Apply(session.Propose(DashboardArrangementKind.Phone, a.Id, new(0, 10, 4, 3, 0))!)
            .ShouldBeTrue();
        session.Document.Widgets.Single().ShouldBe(incomplete);
        DashboardDocuments.Valid(session.Document).ShouldBeFalse();
        session.Undo();
        session.Document.Widgets.Single().ShouldBe(incomplete);
        session.Document.Phone!.Placements.Single().Placement.Row.ShouldBe(0);
    }

    private static DashboardEditSession Session(
        params (DashboardWidget Widget, DashboardPlacement Placement)[] widgets
    ) => new(new(new(Guid.NewGuid()), "Controls", new(1), DashboardDocument.Create(12, widgets)));

    private static DashboardWidget Widget(string label) =>
        new(
            new(Guid.NewGuid()),
            label,
            new DashboardWidgetConfiguration.Automation(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected),
                new DashboardTextOverride.Replace(label)
            )
        );
}

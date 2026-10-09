using System.Text.Json;
using BlokeBot.Core.Features.Overlays.Full;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task DraftAndPublication_RestartLosslesslyWithIndependentSelections()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var original = created.Overlay;
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(original, "<p>selected</p>"),
                _ct
            )
        ).Overlay;
        var incomplete = Save(published, "<not-known keep='all'><unfinished");
        incomplete = incomplete with
        {
            Document = incomplete.Document with
            {
                Widgets = incomplete.Document.Widgets.Add(
                    incomplete.Document.Widgets[0] with
                    {
                        Id = new(Guid.NewGuid()),
                        Kind = new("unresolved-plugin"),
                        Configuration = JsonSerializer.SerializeToElement("unfinished binding"),
                    }
                ),
            },
        };
        var saved = Value(await fixture.Service.SaveAsync(fixture.Owner, incomplete, _ct));
        var restarted = fixture.NewService(new UnavailableFullOverlayPublicationAdmission());
        var loaded = Value(await restarted.GetAsync(fixture.Owner, original.Id, _ct));
        loaded.Draft.Html.ShouldBe(saved.Draft.Html);
        loaded.Draft.Css.ShouldBe(original.Draft.Css);
        loaded.Draft.Id.ShouldBe(original.Id);
        loaded.Draft.Widgets[0].Id.ShouldBe(original.Draft.Widgets.First().Id);
        loaded
            .Draft.Widgets.First()
            .Configuration.GetProperty("unexpectedExtension")[1]
            .GetString()
            .ShouldBe("also");
        loaded.Draft.Widgets[0].Authoring.ShouldBe(original.Draft.Widgets.First().Authoring);
        loaded.Draft.Widgets[0].Audio.ShouldBe(original.Draft.Widgets.First().Audio);
        loaded.Draft.Diagnostics.ShouldBe(original.Draft.Diagnostics);
        loaded.Draft.Widgets[1].Id.ShouldBe(incomplete.Document.Widgets[1].Id);
        loaded.Draft.Widgets[1].Kind.ShouldBe(incomplete.Document.Widgets[1].Kind);
        loaded.Draft.Widgets[1].Configuration.GetString().ShouldBe("unfinished binding");
        var live = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        live.Document.Html.ShouldBe("<p>selected</p>");
        live.Document.Diagnostics.ShouldBeEmpty();
        live.Version.ShouldBe(published.PublishedVersion!.Value);
        var history = Value(await restarted.HistoryAsync(fixture.Owner, original.Id, _ct));
        history.Single().Document.Html.ShouldBe("<p>selected</p>");
        history.Single().AuthorUserId.ShouldBe("author-id");
        Rejected(
            await restarted.SaveAndPublishAsync(
                fixture.Owner,
                Save(loaded, "<p>not admitted</p>"),
                _ct
            ),
            FullOverlayRejectionKind.PublicationUnavailable
        );
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe("<p>selected</p>");
    }

    [Test]
    public async Task RejectedAdmissionAndPersistenceFailure_LeaveDraftSelectionAndHistoryUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "<p>live</p>"),
                _ct
            )
        ).Overlay;
        fixture.Admission.Admit = (_, _) =>
            Task.FromResult<FullOverlayAdmission>(new FullOverlayAdmission.Rejected([]));
        Rejected(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(published, "<script>rejected</script>"),
                _ct
            ),
            FullOverlayRejectionKind.PublicationRejected
        );
        var unchanged = Value(await fixture.Service.GetAsync(fixture.Owner, published.Id, _ct));
        unchanged.Revision.ShouldBe(published.Revision);
        unchanged.Draft.Html.ShouldBe(published.Draft.Html);
        fixture.Admission.Admit = (_, _) =>
            Task.FromResult<FullOverlayAdmission>(new FullOverlayAdmission.Admitted([]));
        fixture.Failure.FailPublication = true;
        _ = await Should.ThrowAsync<IOException>(() =>
            fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(published, "<p>failed save</p>"),
                _ct
            )
        );
        unchanged = Value(await fixture.Service.GetAsync(fixture.Owner, published.Id, _ct));
        unchanged.Revision.ShouldBe(published.Revision);
        unchanged.Draft.Html.ShouldBe(published.Draft.Html);
        Value(await fixture.Service.HistoryAsync(fixture.Owner, published.Id, _ct))
            .Count.ShouldBe(1);
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe("<p>live</p>");
    }

    [Test]
    public async Task AdmissionWindow_ConcurrentDraftSavePreventsOlderPublicationOverwritingIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "<p>live</p>"),
                _ct
            )
        ).Overlay;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Admission.Admit = async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new FullOverlayAdmission.Admitted([]);
        };
        var pending = fixture.Service.SaveAndPublishAsync(
            fixture.Owner,
            Save(published, "<p>stale publish</p>"),
            _ct
        );
        await entered.Task;
        var newer = Value(
            await fixture
                .NewService(fixture.Admission)
                .SaveAsync(fixture.Owner, Save(published, "<p>newer draft</p>"), _ct)
        );
        release.SetResult();
        Rejected(await pending, FullOverlayRejectionKind.Conflict);
        Value(await fixture.Service.GetAsync(fixture.Owner, published.Id, _ct))
            .Draft.Html.ShouldBe(newer.Draft.Html);
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe("<p>live</p>");
        Value(await fixture.Service.HistoryAsync(fixture.Owner, published.Id, _ct))
            .Count.ShouldBe(1);
    }

    [Test]
    public async Task RollbackWarnsForMissingDependencies_PreservesDraftAndSelectedHistoryAgainstRetention()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var first = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "<p>first</p>"),
                _ct
            )
        ).Overlay;
        var second = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(first, "<p>second</p>"),
                _ct
            )
        ).Overlay;
        var draft = Value(
            await fixture.Service.SaveAsync(fixture.Owner, Save(second, "<unfinished>"), _ct)
        );
        var warning = new FullOverlayDiagnostic(
            "missing-media",
            "Media is unavailable.",
            FullOverlayDiagnosticSeverity.Warning,
            draft.Draft.Widgets.Single().Id
        );
        fixture.Admission.Admit = (_, _) =>
            Task.FromResult<FullOverlayAdmission>(new FullOverlayAdmission.Admitted([warning]));
        var rollback = Value(
            await fixture.Service.RollbackAsync(
                fixture.Owner,
                new(draft.Id, draft.Revision, first.PublishedVersion!.Value),
                _ct
            )
        );
        rollback.Warnings.ShouldBe([warning]);
        rollback.Overlay.Draft.Html.ShouldBe(draft.Draft.Html);
        rollback.Overlay.PublishedVersion.ShouldBe(first.PublishedVersion);
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe("<p>first</p>");
        Rejected(
            await fixture.Service.ForgetVersionAsync(
                fixture.Owner,
                new(draft.Id, rollback.Overlay.Revision, first.PublishedVersion.Value),
                _ct
            ),
            FullOverlayRejectionKind.SelectedVersion
        );
        var retained = Value(
            await fixture.Service.ForgetVersionAsync(
                fixture.Owner,
                new(draft.Id, rollback.Overlay.Revision, second.PublishedVersion!.Value),
                _ct
            )
        );
        Value(await fixture.Service.HistoryAsync(fixture.Owner, draft.Id, _ct))
            .Single()
            .Version.ShouldBe(first.PublishedVersion.Value);
        var third = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(retained, "<p>third</p>"),
                _ct
            )
        ).Overlay;
        third.PublishedVersion!.Value.Value.ShouldBeGreaterThan(
            second.PublishedVersion.Value.Value
        );
    }

    [Test]
    public async Task RetentionDuringRollbackAdmission_RejectsObsoleteSelectionWithoutDanglingHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var first = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "<p>first</p>"),
                _ct
            )
        ).Overlay;
        var second = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(first, "<p>second</p>"),
                _ct
            )
        ).Overlay;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Admission.Admit = async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new FullOverlayAdmission.Admitted([]);
        };
        var pending = fixture.Service.RollbackAsync(
            fixture.Owner,
            new(second.Id, second.Revision, first.PublishedVersion!.Value),
            _ct
        );
        await entered.Task;
        _ = Value(
            await fixture.Service.ForgetVersionAsync(
                fixture.Owner,
                new(second.Id, second.Revision, first.PublishedVersion.Value),
                _ct
            )
        );
        release.SetResult();
        Rejected(await pending, FullOverlayRejectionKind.Conflict);
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe("<p>second</p>");
        Value(await fixture.Service.HistoryAsync(fixture.Owner, second.Id, _ct))
            .Single()
            .Version.ShouldBe(second.PublishedVersion!.Value);
    }
}

using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task DuplicateArchiveRestoreAndDelete_PreserveIndependentDraftAndSharedAssets()
    {
        await using var fixture = await Fixture.CreateAsync();
        var assetId = Guid.NewGuid();
        await using (var db = fixture.Database.CreateDbContext())
        {
            _ = db.OverlayMediaAssets.Add(
                new()
                {
                    PublicId = assetId,
                    HostId = fixture.HostId,
                    Name = "Shared audio",
                    ContentRevision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Document = new()
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "audio/mpeg",
                        ByteLength = 42,
                        StorageKey = Guid.NewGuid().ToString("N"),
                        State = OverlayMediaDocumentState.Available,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                    },
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var doc = Document($"<audio src='/media/{assetId}'></audio>");
        var created = Value(
            await fixture.Service.CreateAsync(fixture.Owner, new("Audio", doc), _ct)
        );
        var live = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, doc.Html),
                _ct
            )
        ).Overlay;
        var duplicate = Value(
            await fixture.Service.DuplicateAsync(
                fixture.Owner,
                new(live.Id, live.Revision, "Copy"),
                _ct
            )
        );
        duplicate.Overlay.Id.ShouldNotBe(live.Id);
        duplicate.PrivateAccess.AccessKey.ShouldNotBe(created.PrivateAccess.AccessKey);
        duplicate.Overlay.Draft.Id.ShouldBe(duplicate.Overlay.Id);
        duplicate.Overlay.Draft.Html.ShouldBe(live.Draft.Html);
        duplicate.Overlay.Draft.Widgets.Single().Audio.ShouldBe(live.Draft.Widgets.Single().Audio);
        duplicate.Overlay.PublishedVersion.ShouldBeNull();
        Value(await fixture.Service.HistoryAsync(fixture.Owner, duplicate.Overlay.Id, _ct))
            .ShouldBeEmpty();
        (await fixture.Reader.ResolveAsync(duplicate.PrivateAccess.AccessKey, _ct)).ShouldBeNull();
        _ = Value(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                Save(duplicate.Overlay, "<p>copy only</p>"),
                _ct
            )
        );
        Value(await fixture.Service.GetAsync(fixture.Owner, live.Id, _ct))
            .Draft.Html.ShouldBe(doc.Html);
        var archived = Value(
            await fixture.Service.ArchiveAsync(fixture.Owner, new(live.Id, live.Revision), _ct)
        );
        (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)).ShouldBeNull();
        Value(await fixture.Service.ListAsync(fixture.Owner, FullOverlayCollection.Active, _ct))
            .ShouldNotContain(row => row.Id == live.Id);
        Value(await fixture.Service.ListAsync(fixture.Owner, FullOverlayCollection.Archived, _ct))
            .Single()
            .Id.ShouldBe(live.Id);
        var restored = Value(
            await fixture.Service.RestoreAsync(fixture.Owner, new(live.Id, archived.Revision), _ct)
        );
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe(doc.Html);
        Rejected(
            await fixture.Service.DeleteAsync(
                fixture.Owner,
                new(live.Id, restored.Revision, FullOverlayDeleteConfirmation.Unconfirmed),
                _ct
            ),
            FullOverlayRejectionKind.ConfirmationRequired
        );
        _ = Value(
            await fixture.Service.DeleteAsync(
                fixture.Owner,
                new(live.Id, restored.Revision, FullOverlayDeleteConfirmation.Confirmed),
                _ct
            )
        );
        (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)).ShouldBeNull();
        await using var final = fixture.Database.CreateDbContext();
        (await final.FullOverlayPublications.CountAsync()).ShouldBe(0);
        (await final.OverlayMediaAssets.SingleAsync()).PublicId.ShouldBe(assetId);
        (await final.OverlayMediaDocuments.SingleAsync()).State.ShouldBe(
            OverlayMediaDocumentState.Available
        );
        JsonSerializer
            .Serialize(duplicate.PrivateAccess)
            .ShouldNotContain(duplicate.PrivateAccess.AccessKey);
    }

    [Test]
    public async Task CrossHostBotAndRevokedModerator_CannotReadHistoryOrAlterPublication()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var live = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "<p>private host document</p>"),
                _ct
            )
        ).Overlay;
        var other = Session(fixture.OtherHostId);
        Rejected(
            await fixture.Service.GetAsync(other, live.Id, _ct),
            FullOverlayRejectionKind.NotFound
        );
        Rejected(
            await fixture.Service.HistoryAsync(other, live.Id, _ct),
            FullOverlayRejectionKind.NotFound
        );
        Rejected(
            await fixture.Service.SaveAndPublishAsync(other, Save(live, "<p>cross-host</p>"), _ct),
            FullOverlayRejectionKind.NotFound
        );
        Rejected(
            await fixture.Service.DeleteAsync(
                Session(fixture.HostId, AuthRole.Bot),
                new(live.Id, live.Revision, FullOverlayDeleteConfirmation.Confirmed),
                _ct
            ),
            FullOverlayRejectionKind.Unauthorized
        );
        var moderator = Session(fixture.HostId, AuthRole.Moderator);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Admission.Admit = async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new FullOverlayAdmission.Admitted([]);
        };
        var pending = fixture.Service.SaveAndPublishAsync(
            moderator,
            Save(live, "<p>revoked during admission</p>"),
            _ct
        );
        await entered.Task;
        fixture.Moderator.Outcome = new ModeratorAuthorityOutcome.Revoked();
        release.SetResult();
        Rejected(await pending, FullOverlayRejectionKind.Unauthorized);
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe(live.Draft.Html);
        Value(await fixture.Service.GetAsync(fixture.Owner, live.Id, _ct))
            .Revision.ShouldBe(live.Revision);
        await using var db = fixture.Database.CreateDbContext();
        _ = await db
            .Hosts.Where(host => host.Id == fixture.HostId)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.None)
            );
        (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)).ShouldBeNull();
        Rejected(
            await fixture.Service.SaveAsync(fixture.Owner, Save(live, "<p>disabled</p>"), _ct),
            FullOverlayRejectionKind.FeatureDisabled
        );
    }

    [Test]
    public async Task CancellationFromPostCommitObserver_DoesNotReportPublicationAsUncommitted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        using var cancellation = new CancellationTokenSource();
        using var subscription = fixture.Events.Subscribe(
            AppEventKind.OverlaysChanged,
            ObserverIdentity.Named("cancel-after-publication"),
            (_, token) =>
            {
                cancellation.Cancel();
                token.IsCancellationRequested.ShouldBeFalse();
                return ValueTask.CompletedTask;
            }
        );
        var selected = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "<p>committed</p>"),
                cancellation.Token
            )
        );
        cancellation.IsCancellationRequested.ShouldBeTrue();
        (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!.Version.ShouldBe(
            selected.Overlay.PublishedVersion!.Value
        );
        Value(await fixture.Service.HistoryAsync(fixture.Owner, created.Overlay.Id, _ct))
            .Count.ShouldBe(1);
    }
}

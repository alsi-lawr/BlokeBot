using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task ProtectedAccessRecoversSameKeyAfterKeyRingRestartWithoutCrossHostReadOrSilentRotation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var root = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-workspace",
            "overlay-composition-20261001",
            "writer287",
            "keys",
            Guid.NewGuid().ToString("N")
        );
        _ = Directory.CreateDirectory(root);
        try
        {
            var first = fixture.NewService(
                fixture.Admission,
                DataProtectionProvider.Create(new DirectoryInfo(root))
            );
            var created = Value(
                await first.CreateAsync(fixture.Owner, new("Protected", Document()), _ct)
            );
            var restarted = fixture.NewService(
                fixture.Admission,
                DataProtectionProvider.Create(new DirectoryInfo(root))
            );
            Value(await restarted.ReadAccessAsync(fixture.Owner, created.Overlay.Id, _ct))
                .AccessKey.ShouldBe(created.PrivateAccess.AccessKey);
            Rejected(
                await restarted.ReadAccessAsync(
                    Session(fixture.OtherHostId),
                    created.Overlay.Id,
                    _ct
                ),
                FullOverlayRejectionKind.NotFound
            );
            var wrongRing = fixture.NewService(
                fixture.Admission,
                new EphemeralDataProtectionProvider()
            );
            Rejected(
                await wrongRing.ReadAccessAsync(fixture.Owner, created.Overlay.Id, _ct),
                FullOverlayRejectionKind.AccessUnavailable
            );
            await using var db = fixture.Database.CreateDbContext();
            var stored = await db.FullOverlays.SingleAsync(row =>
                row.PublicId == created.Overlay.Id
            );
            stored.Revision.ShouldBe(created.Overlay.Revision.Value);
            _ = stored.ProtectedAccessKey.ShouldNotBeNull();
            stored.ProtectedAccessKey!.ShouldNotContain(created.PrivateAccess.AccessKey);
            stored.AccessKeyDigest.ShouldBe(
                OverlayAccessKeyDigest.Compute(created.PrivateAccess.AccessKey)
            );
            JsonSerializer.Serialize(created).ShouldNotContain(created.PrivateAccess.AccessKey);
            JsonSerializer
                .Serialize(Value(await restarted.GetAsync(fixture.Owner, created.Overlay.Id, _ct)))
                .ShouldNotContain(stored.ProtectedAccessKey!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task EffectiveSelectionChangesAdvanceExistingCounterWithoutRewritingHistoryOrAdvancingOnDraftAndForget()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateOverlayAsync();
        var a = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "A"),
                _ct
            )
        ).Overlay;
        var original = Value(await fixture.Service.HistoryAsync(fixture.Owner, a.Id, _ct)).Single();
        var b = Value(
            await fixture.Service.SaveAndPublishAsync(fixture.Owner, Save(a, "B"), _ct)
        ).Overlay;
        var selectedB = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var again = Value(
            await fixture.Service.RollbackAsync(
                fixture.Owner,
                new(b.Id, b.Revision, a.PublishedVersion!.Value),
                _ct
            )
        ).Overlay;
        var selectedA = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        selectedA.Generation.Value.ShouldBeGreaterThan(selectedB.Generation.Value);
        selectedA.Version.ShouldBe(original.Version);
        var retained = Value(await fixture.Service.HistoryAsync(fixture.Owner, a.Id, _ct))
            .Single(version => version.Version == original.Version);
        retained.Version.ShouldBe(original.Version);
        retained.AuthorUserId.ShouldBe(original.AuthorUserId);
        retained.PublishedAtUtc.ShouldBe(original.PublishedAtUtc);
        FullOverlayDocuments
            .Serialize(retained.Document)
            .ShouldBe(FullOverlayDocuments.Serialize(original.Document));
        var saved = Value(
            await fixture.Service.SaveAsync(fixture.Owner, Save(again, "DRAFT"), _ct)
        );
        var forgotten = Value(
            await fixture.Service.ForgetVersionAsync(
                fixture.Owner,
                new(saved.Id, saved.Revision, b.PublishedVersion!.Value),
                _ct
            )
        );
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Generation.ShouldBe(selectedA.Generation);
        var archived = Value(
            await fixture.Service.ArchiveAsync(
                fixture.Owner,
                new(forgotten.Id, forgotten.Revision),
                _ct
            )
        );
        var restored = Value(
            await fixture.Service.RestoreAsync(
                fixture.Owner,
                new(archived.Id, archived.Revision),
                _ct
            )
        );
        var restoredSelection = (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!;
        restoredSelection.Generation.Value.ShouldBeGreaterThan(selectedA.Generation.Value);
        restoredSelection.Version.ShouldBe(original.Version);
        var c = Value(
            await fixture.Service.SaveAndPublishAsync(fixture.Owner, Save(restored, "C"), _ct)
        ).Overlay;
        c.PublishedVersion!.Value.Value.ShouldBeGreaterThan(b.PublishedVersion!.Value.Value + 1);
    }
}

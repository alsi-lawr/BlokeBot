using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Eventing;
using BlokeBot.Plugins.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Transfer_FailedDatabaseCommitAndFailedCleanupRemainNonAuthoritativeAndRestartReclaimable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var deletion = new TransferDeletion();
        await using var environment = new WidgetEnvironment(fixture, fileDeletion: deletion);
        var transfer = Transfer(fixture, environment);
        var destination = Value(
            await fixture.Service.CreateAsync(
                Session(fixture.OtherHostId),
                new("Keep destination", Document()),
                _ct
            )
        );
        var package = await MediaPackage(fixture, environment, transfer, [1, 2, 3, 4]);
        fixture.Failure.FailImport = true;
        deletion.Fail = true;
        _ = (
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(package), _ct)
        ).ShouldBeOfType<FullOverlayResult<FullOverlayImportApplied>.Rejected>();
        fixture.Failure.FailImport = false;
        await using (var db = fixture.Database.CreateDbContext())
        {
            (await db.FullOverlays.CountAsync()).ShouldBe(1);
            (
                await db.OverlayMediaAssets.CountAsync(asset => asset.HostId == fixture.OtherHostId)
            ).ShouldBe(0);
        }
        var unchanged = Value(
            await fixture.Service.GetAsync(
                Session(fixture.OtherHostId),
                destination.Overlay.Id,
                _ct
            )
        );
        FullOverlayDocuments
            .Serialize(unchanged.Draft)
            .ShouldBe(FullOverlayDocuments.Serialize(destination.Overlay.Draft));
        Directory
            .EnumerateFiles(environment.MediaRoot)
            .Any(path => Path.GetFileName(path).StartsWith(".import-", StringComparison.Ordinal))
            .ShouldBeTrue();
        deletion.Fail = false;
        var interrupted = Path.Combine(environment.MediaRoot, ".import-interrupted");
        await File.WriteAllBytesAsync(interrupted, [6, 7]);
        var state = Path.GetDirectoryName(Path.GetDirectoryName(environment.MediaRoot))!;
        using (
            var recovery = new OverlayMediaMaintenanceService(
                fixture.Database,
                Options.Create(new BlokeBotOptions { StateDirectory = state }),
                deletion,
                TimeProvider.System,
                NullLogger<OverlayMediaMaintenanceService>.Instance
            )
        )
        {
            await recovery.RecoverAsync(_ct);
        }
        Directory.EnumerateFiles(environment.MediaRoot).Count().ShouldBe(1);
        File.Exists(interrupted).ShouldBeFalse();
        var successful = Value(
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(package), _ct)
        );
        successful.Created.Overlay.PublishedVersion.ShouldBeNull();
        await environment.Maintenance.RecoverAsync(_ct);
        var assetId = successful
            .Created.Overlay.Draft.Widgets[0]
            .Configuration.GetProperty("assetId")
            .GetGuid();
        var content = (
            await environment.Media.ResolveContentAsync(fixture.OtherHostId, assetId, 1, _ct)
        )!;
        (await File.ReadAllBytesAsync(content.Path)).ShouldBe([1, 2, 3, 4]);
    }

    [Test]
    public async Task Transfer_CommitTimeAuthorityAndDeclarationChangesRejectWithoutPartialState()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        PublishPortableWidget(plugin);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        var transfer = Transfer(fixture, environment, plugin.Declarations);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var package = await Export(transfer, fixture.Owner, Document() with { Widgets = [widget] });
        fixture.Failure.DuringImportSave = () =>
            plugin.Declarations.Remove(plugin.Manifest.Manifest.Id, plugin.State.Fence);
        Rejected(
            await transfer.ImportAsync(
                Session(fixture.OtherHostId),
                new MemoryStream(package),
                _ct
            ),
            FullOverlayRejectionKind.Conflict
        );
        fixture.Failure.DuringImportSave = null;
        PublishPortableWidget(plugin, PluginWidgetFieldPortability.Withheld);
        var imported = Value(
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(package), _ct)
        );
        imported
            .Created.Overlay.Draft.Widgets[0]
            .Configuration.TryGetProperty("text", out _)
            .ShouldBeFalse();
        imported.Created.Overlay.Draft.Widgets[0].RequiresSetup.ShouldBeTrue();
        fixture.Failure.DuringImportSave = () =>
            fixture.Moderator.Outcome =
                new BlokeBot.Core.Auth.Moderation.ModeratorAuthorityOutcome.Revoked();
        Rejected(
            await transfer.ImportAsync(
                Session(fixture.OtherHostId, BlokeBot.Core.Auth.Sessions.AuthRole.Moderator),
                new MemoryStream(package),
                _ct
            ),
            FullOverlayRejectionKind.Unauthorized
        );
        fixture.Failure.DuringImportSave = null;
        await using var db = fixture.Database.CreateDbContext();
        (await db.FullOverlays.CountAsync()).ShouldBe(1);
        (await db.OverlayMediaAssets.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Transfer_QuotaAndDecompressedUploadRejectionPreserveExistingAssetsAndMissingMediaStaysExplicit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(
            fixture,
            mediaOptions: new() { MaximumUploadBytes = 1024, MaximumHostStorageBytes = 1024 }
        );
        var transfer = Transfer(fixture, environment);
        var package = await MediaPackage(fixture, environment, transfer, new byte[600]);
        var existing = (
            await environment.Media.UploadAssetAsync(
                Session(fixture.OtherHostId),
                "Existing",
                "image/png",
                new MemoryStream(new byte[700]),
                _ct
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        var rejected = (
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(package), _ct)
        ).ShouldBeOfType<FullOverlayResult<FullOverlayImportApplied>.Rejected>();
        rejected.Reason.Diagnostics[0].Message.ShouldContain("quota");
        var oversized = await HandmadeMediaPackage(1025);
        rejected = (
            await transfer.ImportAsync(
                Session(fixture.OtherHostId),
                new MemoryStream(oversized),
                _ct
            )
        ).ShouldBeOfType<FullOverlayResult<FullOverlayImportApplied>.Rejected>();
        rejected.Reason.Diagnostics[0].Message.ShouldContain("1024");
        await using (var db = fixture.Database.CreateDbContext())
        {
            (await db.FullOverlays.CountAsync()).ShouldBe(0);
            (
                await db.OverlayMediaAssets.CountAsync(asset => asset.HostId == fixture.OtherHostId)
            ).ShouldBe(1);
        }
        var content = (
            await environment.Media.ResolveContentAsync(fixture.OtherHostId, existing.Id, 1, _ct)
        )!;
        (await File.ReadAllBytesAsync(content.Path)).Length.ShouldBe(700);
        var missing = environment.Registry.Create(new("image"), Guid.NewGuid())! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayMediaConfiguration(Guid.NewGuid(), true)
            ),
            Audio = new(true, .25),
        };
        var bytes = await Export(transfer, fixture.Owner, Document() with { Widgets = [missing] });
        var imported = Value(
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(bytes), _ct)
        );
        var retained = imported.Created.Overlay.Draft.Widgets.Single();
        retained.Id.ShouldBe(missing.Id);
        retained.Audio.ShouldBe(missing.Audio);
        retained.RequiresSetup.ShouldBeTrue();
        retained.Configuration.GetProperty("assetId").GetGuid().ShouldBe(Guid.Empty);
    }

    [Test]
    public async Task Transfer_CancellationAndInputFailureReclaimStagingWhilePostCommitObserverFailureIsAppliedFollowUp()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var transfer = Transfer(fixture, environment);
        var package = await MediaPackage(fixture, environment, transfer, [1, 3, 5]);
        using var cancellation = new CancellationTokenSource();
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            transfer.ImportAsync(
                Session(fixture.OtherHostId),
                new TransferReadStream(package, () => cancellation.Cancel()),
                cancellation.Token
            )
        );
        _ = (
            await transfer.ImportAsync(
                Session(fixture.OtherHostId),
                new TransferReadStream(package, () => throw new IOException("owned failure")),
                _ct
            )
        ).ShouldBeOfType<FullOverlayResult<FullOverlayImportApplied>.Rejected>();
        Directory.EnumerateFiles(environment.MediaRoot).Count().ShouldBe(1);
        using var afterCommit = new CancellationTokenSource();
        using var subscription = fixture.Events.Subscribe(
            AppEventKind.OverlaysChanged,
            ObserverIdentity.Named("import-failure"),
            async (_, token) =>
            {
                token.IsCancellationRequested.ShouldBeFalse();
                await using var db = fixture.Database.CreateDbContext();
                (await db.FullOverlays.CountAsync()).ShouldBe(1);
                (
                    await db.OverlayMediaAssets.CountAsync(asset =>
                        asset.HostId == fixture.OtherHostId
                    )
                ).ShouldBe(1);
                afterCommit.Cancel();
                throw new InvalidOperationException("observer-private-response");
            }
        );
        var applied = Value(
            await transfer.ImportAsync(
                Session(fixture.OtherHostId),
                new MemoryStream(package),
                afterCommit.Token
            )
        );
        applied.NotificationPending.ShouldBeTrue();
        afterCommit.IsCancellationRequested.ShouldBeTrue();
        Value(
            await fixture.Service.GetAsync(
                Session(fixture.OtherHostId),
                applied.Created.Overlay.Id,
                _ct
            )
        )
            .Revision.Value.ShouldBe(1);
        await environment.Maintenance.RecoverAsync(_ct);
        var id = applied
            .Created.Overlay.Draft.Widgets[0]
            .Configuration.GetProperty("assetId")
            .GetGuid();
        _ = (
            await environment.Media.ResolveContentAsync(fixture.OtherHostId, id, 1, _ct)
        ).ShouldNotBeNull();
    }

    private static async Task<byte[]> MediaPackage(
        Fixture fixture,
        WidgetEnvironment environment,
        FullOverlayTransferService transfer,
        byte[] bytes
    )
    {
        var asset = (
            await environment.Media.UploadAssetAsync(
                fixture.Owner,
                "Packaged image",
                "image/png",
                new MemoryStream(bytes),
                _ct
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        var widget = environment.Registry.Create(new("image"), Guid.NewGuid())! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayMediaConfiguration(asset.Id, false)
            ),
        };
        return await Export(transfer, fixture.Owner, Document() with { Widgets = [widget] });
    }

    private static async Task<byte[]> HandmadeMediaPackage(int length)
    {
        using var output = new MemoryStream();
        var asset = Guid.NewGuid();
        var content = new FullOverlayPackageContent(Guid.NewGuid(), "image/png", length);
        var document = Document() with
        {
            Widgets =
            [
                new(
                    new(Guid.NewGuid()),
                    new("image"),
                    JsonSerializer.SerializeToElement(
                        new FullOverlayMediaConfiguration(asset, false)
                    ),
                    FullOverlayAuthoringMetadata.Default,
                    FullOverlayAudio.Default
                ),
            ],
        };
        await using (
            var archive = await ZipArchive.CreateAsync(
                output,
                ZipArchiveMode.Create,
                true,
                null,
                _ct
            )
        )
        {
            await FullOverlayPackageCodec.WriteManifestAsync(
                archive,
                new(1, "Imported", document, [new(asset, "File", content.Id)], [content]),
                _ct
            );
            await using var stream = await archive
                .CreateEntry(content.EntryName, CompressionLevel.SmallestSize)
                .OpenAsync(_ct);
            await stream.WriteAsync(new byte[length], _ct);
        }
        return output.ToArray();
    }

    private sealed class TransferDeletion : IOverlayMediaFileDeletion
    {
        internal bool Fail { get; set; }

        public OverlayMediaFileDeletionOutcome Delete(string path) =>
            Fail
                ? new OverlayMediaFileDeletionOutcome.Unavailable()
                : new SystemOverlayMediaFileDeletion().Delete(path);
    }

    private sealed class TransferReadStream(byte[] bytes, Action read) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken ct = default
        )
        {
            read();
            ct.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, ct);
        }
    }
}

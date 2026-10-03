using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Transfer_MalformedAndUndeclaredArchiveEntriesNeverCreateStateOrLeaveStaging()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var transfer = Transfer(fixture, environment);
        var existing = Value(
            await fixture.Service.CreateAsync(fixture.Owner, new("Existing", Document()), _ct)
        );
        var valid = await Export(transfer, fixture.Owner, Document());
        byte[] manifest;
        using (var archive = new ZipArchive(new MemoryStream(valid), ZipArchiveMode.Read))
        {
            using var output = new MemoryStream();
            await archive.GetEntry("document.json")!.Open().CopyToAsync(output, _ct);
            manifest = output.ToArray();
        }
        var nullDocument = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                formatVersion = 1,
                name = "Malformed",
                document = (object?)null,
                assets = Array.Empty<object>(),
                contents = Array.Empty<object>(),
            }
        );
        (string Name, byte[] Bytes)[][] packages =
        [
            [("document.json", nullDocument)],
            [("document.json", manifest), ("document.json", manifest)],
            [("document.json", manifest), ("../escaped.png", [1])],
        ];
        foreach (var entries in packages)
        {
            using var input = new MemoryStream(await Archive(entries));
            Rejected(
                await transfer.ImportAsync(fixture.Owner, input, _ct),
                FullOverlayRejectionKind.Invalid
            );
            Directory.GetFiles(environment.MediaRoot).ShouldBeEmpty();
        }
        Rejected(
            await transfer.ImportAsync(fixture.Owner, new MemoryStream([1, 2, 3]), _ct),
            FullOverlayRejectionKind.Invalid
        );
        await using var db = fixture.Database.CreateDbContext();
        (await db.FullOverlays.CountAsync()).ShouldBe(1);
        (await db.OverlayMediaAssets.CountAsync()).ShouldBe(0);
        FullOverlayDocuments
            .Serialize(
                Value(await fixture.Service.GetAsync(fixture.Owner, existing.Overlay.Id, _ct)).Draft
            )
            .ShouldBe(FullOverlayDocuments.Serialize(existing.Overlay.Draft));
    }

    [Test]
    public async Task Transfer_PrecommitFeatureChangeAndDifferentSelectedActorLeaveDestinationUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var transfer = Transfer(fixture, environment);
        var package = await Export(transfer, fixture.Owner, Document());
        var changed = false;
        using var input = new TransferReadStream(
            package,
            () =>
            {
                if (changed)
                {
                    return;
                }
                changed = true;
                using var db = fixture.Database.CreateDbContext();
                _ = db
                    .Hosts.Where(host => host.Id == fixture.OtherHostId)
                    .ExecuteUpdate(setters =>
                        setters.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.None)
                    );
            }
        );
        Rejected(
            await transfer.ImportAsync(Session(fixture.OtherHostId), input, _ct),
            FullOverlayRejectionKind.FeatureDisabled
        );
        await using var staging = await environment.Media.BeginTransferAsync(_ct);
        Rejected(
            await fixture.Service.ApplyImportAsync(
                fixture.Owner,
                new(fixture.OtherHostId, fixture.Owner.UserId, fixture.Owner.Login),
                new(1, "Wrong selection", Document(), [], []),
                staging,
                new(new BlokeBot.Plugins.Features.PluginFeatureDeclarationRegistry()),
                Microsoft
                    .Extensions
                    .Logging
                    .Abstractions
                    .NullLogger<FullOverlayTransferService>
                    .Instance,
                _ct
            ),
            FullOverlayRejectionKind.Unauthorized
        );
        await using var verify = fixture.Database.CreateDbContext();
        (await verify.FullOverlays.CountAsync()).ShouldBe(0);
        (await verify.OverlayMediaAssets.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Transfer_PreparedExportReleasesMediaGateBeforeConsumerAndCloseReclaimsCanceledDownload()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var transfer = Transfer(fixture, environment);
        var raw = Document("<main><!-- exact streamed source --></main>", "/* raw remains */");
        var prepared = Value(await transfer.PrepareExportAsync(fixture.Owner, "Export", raw, _ct));
        (await environment.Maintenance.Gate.WaitAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        _ = environment.Maintenance.Gate.Release();
        await environment.Maintenance.RecoverAsync(_ct);
        using var copied = new MemoryStream();
        await prepared.CopyToAsync(copied, _ct);
        using (var zip = new ZipArchive(new MemoryStream(copied.ToArray()), ZipArchiveMode.Read))
        {
            await using var staging = await environment.Media.BeginTransferAsync(_ct);
            var parsed = Value(await FullOverlayPackageCodec.ReadAsync(zip, staging, _ct));
            parsed.Document.Html.ShouldBe(raw.Html);
        }
        await prepared.DisposeAsync();
        var canceled = Value(await transfer.PrepareExportAsync(fixture.Owner, "Cancel", raw, _ct));
        _ = await canceled.ReadAsync(new byte[16], _ct);
        await canceled.DisposeAsync();
        await environment.Maintenance.RecoverAsync(_ct);
        Directory.GetFiles(environment.MediaRoot).ShouldBeEmpty();
        await using var db = fixture.Database.CreateDbContext();
        (await db.FullOverlays.CountAsync()).ShouldBe(0);
        (await db.OverlayMediaAssets.CountAsync()).ShouldBe(0);
    }

    private static async Task<byte[]> Archive((string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in entries)
            {
                using var stream = zip.CreateEntry(entry.Name).Open();
                await stream.WriteAsync(entry.Bytes, _ct);
            }
        }
        return output.ToArray();
    }
}

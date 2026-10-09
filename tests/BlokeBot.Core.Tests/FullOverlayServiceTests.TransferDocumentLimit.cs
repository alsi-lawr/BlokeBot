using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Transfer_ConfiguredDocumentBoundaryAcceptsVerbatimSourceAndCurrentMedia()
    {
        const int Maximum = 4096;
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(
            fixture,
            mediaOptions: new() { MaximumUploadBytes = Maximum }
        );
        var transfer = Transfer(fixture, environment);
        var package = await MediaPackage(fixture, environment, transfer, [3, 7, 11]);
        var manifest = await ReadManifest(package);
        var raw = manifest.Document with
        {
            Html = "<!-- exact import boundary -->" + manifest.Document.Html,
            Css = "@future opaque { --anything: [preserved; }",
        };
        manifest = manifest with { Document = raw };
        var length = JsonSerializer
            .SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Length;
        raw = raw with { Html = raw.Html + new string('x', Maximum - length) };
        package = await Export(transfer, fixture.Owner, raw);
        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            archive.GetEntry("document.json")!.Length.ShouldBe(Maximum);
        }
        var applied = Value(
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(package), _ct)
        );
        var reopened = Value(
            await fixture.Service.GetAsync(
                Session(fixture.OtherHostId),
                applied.Created.Overlay.Id,
                _ct
            )
        );
        reopened.PublishedVersion.ShouldBeNull();
        reopened.Draft.Html.ShouldBe(raw.Html);
        reopened.Draft.Css.ShouldBe(raw.Css);
        reopened.Draft.Widgets.Single().Id.ShouldBe(raw.Widgets.Single().Id);
        var assetId = reopened
            .Draft.Widgets.Single()
            .Configuration.GetProperty("assetId")
            .GetGuid();
        var content = (
            await environment.Media.ResolveContentAsync(fixture.OtherHostId, assetId, 1, _ct)
        )!;
        (await File.ReadAllBytesAsync(content.Path)).ShouldBe([3, 7, 11]);
        Directory.GetFiles(environment.MediaRoot).Length.ShouldBe(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Transfer_CompressedOversizedOrMalformedDocumentPreservesDestination(
        bool falseDeclaredLength
    )
    {
        const int Maximum = 4096;
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(
            fixture,
            mediaOptions: new() { MaximumUploadBytes = Maximum }
        );
        var transfer = Transfer(fixture, environment);
        var package = await MediaPackage(fixture, environment, transfer, [1, 2, 3]);
        var manifest = await ReadManifest(package);
        var raw = manifest.Document with { Html = "<!--" + new string('x', 200_000) + "-->" };
        var saved = Value(
            await fixture.Service.CreateAsync(fixture.Owner, new("Large saved source", raw), _ct)
        );
        // Save/export are not restricted by the selected import-only policy.
        package = await Export(transfer, fixture.Owner, saved.Overlay.Draft);
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        using (var compressed = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open();
                using var target = compressed
                    .CreateEntry(entry.FullName, CompressionLevel.SmallestSize)
                    .Open();
                await input.CopyToAsync(target, _ct);
            }
        }
        var bytes = output.ToArray();
        bytes.Length.ShouldBeLessThan(Maximum);
        if (falseDeclaredLength)
        {
            // Malformed ZIP length is rejected through the existing framework boundary.
            var centralHeader = bytes.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
            centralHeader.ShouldBeGreaterThanOrEqualTo(0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(centralHeader + 24, 4), 1);
        }
        using (var compressed = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            compressed
                .GetEntry("document.json")!
                .Length.ShouldBe(
                    falseDeclaredLength
                        ? 1
                        : JsonSerializer
                            .SerializeToUtf8Bytes(
                                await ReadManifest(package),
                                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                            )
                            .Length
                );
        }
        var existing = Value(
            await fixture.Service.CreateAsync(
                Session(fixture.OtherHostId),
                new("Existing destination", Document("existing source", "existing css")),
                _ct
            )
        );
        var asset = (
            await environment.Media.UploadAssetAsync(
                Session(fixture.OtherHostId),
                "Existing bytes",
                "image/png",
                new MemoryStream([7, 8, 9]),
                _ct
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        var files = Directory.GetFiles(environment.MediaRoot).Order().ToArray();
        var rejected = (
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(bytes), _ct)
        ).ShouldBeOfType<FullOverlayResult<FullOverlayImportApplied>.Rejected>();
        rejected.Reason.Kind.ShouldBe(FullOverlayRejectionKind.Invalid);
        if (falseDeclaredLength)
        {
            rejected.Reason.Diagnostics.Single().Message.ShouldContain("malformed or incomplete");
        }
        else
        {
            rejected.Reason.Diagnostics.Single().Message.ShouldContain("imported document");
            rejected.Reason.Diagnostics.Single().Message.ShouldContain($"{Maximum}-byte limit");
        }
        FullOverlayDocuments
            .Serialize(
                Value(
                    await fixture.Service.GetAsync(
                        Session(fixture.OtherHostId),
                        existing.Overlay.Id,
                        _ct
                    )
                ).Draft
            )
            .ShouldBe(FullOverlayDocuments.Serialize(existing.Overlay.Draft));
        Value(await fixture.Service.GetAsync(fixture.Owner, saved.Overlay.Id, _ct))
            .Draft.Html.ShouldBe(raw.Html);
        var content = (
            await environment.Media.ResolveContentAsync(fixture.OtherHostId, asset.Id, 1, _ct)
        )!;
        (await File.ReadAllBytesAsync(content.Path)).ShouldBe([7, 8, 9]);
        Directory.GetFiles(environment.MediaRoot).Order().ShouldBe(files);
        await using var db = fixture.Database.CreateDbContext();
        (await db.FullOverlays.CountAsync(row => row.HostId == fixture.OtherHostId)).ShouldBe(1);
        (await db.OverlayMediaAssets.CountAsync(row => row.HostId == fixture.OtherHostId)).ShouldBe(
            1
        );
    }

    private static async Task<FullOverlayPackage> ReadManifest(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("document.json")!.Open();
        return (
            await JsonSerializer.DeserializeAsync<FullOverlayPackage>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                _ct
            )
        )!;
    }
}

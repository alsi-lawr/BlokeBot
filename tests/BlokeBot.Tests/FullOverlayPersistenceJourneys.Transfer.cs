using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Tests;

public sealed partial class FullOverlayPersistenceJourneys
{
    private static async Task TransferJourneyAsync(
        Database factory,
        AuthenticatedSession source,
        FullOverlayService documents,
        EventBus<AppEventKind> events,
        string directory
    )
    {
        const int Maximum = 8192;
        var options = Options.Create(
            new BlokeBotOptions
            {
                StateDirectory = directory,
                Overlays = new() { Media = new() { MaximumUploadBytes = Maximum } },
            }
        );
        var authority = new OverlayManagementAuthority(factory, new Moderator());
        var deletion = new SystemOverlayMediaFileDeletion();
        using var maintenance = new OverlayMediaMaintenanceService(
            factory,
            options,
            deletion,
            TimeProvider.System,
            NullLogger<OverlayMediaMaintenanceService>.Instance
        );
        var media = new OverlayCueService(
            factory,
            authority,
            new(new SystemOverlayDnsResolver(), options),
            options,
            events,
            TimeProvider.System,
            deletion,
            maintenance
        );
        var transfer = new FullOverlayTransferService(
            authority,
            media,
            documents,
            new(new PluginFeatureDeclarationRegistry()),
            NullLogger<FullOverlayTransferService>.Instance
        );
        BotHost host;
        await using (var db = factory.CreateDbContext())
        {
            host = new()
            {
                Login = "transfer-destination",
                DisplayName = "Transfer destination",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
        }
        var choice = new BotHostChoice(host.Id, host.Login, host.DisplayName, AuthRole.Streamer);
        var destination = source with
        {
            State = new AuthSessionState.Selected(new BotHostSelection(choice, [choice])),
        };
        var asset = (
            await media.UploadAssetAsync(
                source,
                "Current logo",
                "image/png",
                new MemoryStream([1, 2, 3]),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        _ = (
            await media.ReplaceAssetAsync(
                source,
                new(asset.Id, asset.ContentRevision, "image/png", new MemoryStream([7, 8, 9, 10])),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>();
        var widget = new FullOverlayWidget(
            new(Guid.NewGuid()),
            new("image"),
            JsonSerializer.SerializeToElement(new FullOverlayMediaConfiguration(asset.Id, false)),
            FullOverlayAuthoringMetadata.Default,
            new(true, .37)
        );
        var document = new FullOverlayDocument(
            Guid.NewGuid(),
            $"<!-- verbatim --><script>window.accepted=true</script><section data-blokebot-widget='{widget.Id.Value}'></section>",
            "@future opaque { --unfamiliar: [preserved; }",
            [widget],
            []
        );
        var package = await ExportJourneyAsync(transfer, source, document);
        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            var length = archive.GetEntry("document.json")!.Length;
            document = document with
            {
                Html = document.Html + new string('x', Maximum - (int)length),
            };
        }
        package = await ExportJourneyAsync(transfer, source, document);
        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            archive.GetEntry("document.json")!.Length.ShouldBe(Maximum);
        }
        var imported = Value(
            await transfer.ImportAsync(
                destination,
                new MemoryStream(package),
                CancellationToken.None
            )
        ).Created;
        var reopened = Value(
            await documents.GetAsync(destination, imported.Overlay.Id, CancellationToken.None)
        );
        reopened.PublishedVersion.ShouldBeNull();
        reopened.Id.ShouldNotBe(document.Id);
        reopened.Draft.Html.ShouldBe(document.Html);
        reopened.Draft.Css.ShouldBe(document.Css);
        reopened.Draft.Widgets.Single().Id.ShouldBe(widget.Id);
        reopened.Draft.Widgets.Single().Audio.ShouldBe(widget.Audio);
        var importedAsset = reopened
            .Draft.Widgets.Single()
            .Configuration.GetProperty("assetId")
            .GetGuid();
        importedAsset.ShouldNotBe(asset.Id);
        var content = (
            await media.ResolveContentAsync(host.Id, importedAsset, 1, CancellationToken.None)
        )!;
        (await File.ReadAllBytesAsync(content.Path)).ShouldBe([7, 8, 9, 10]);
        var baseline = FullOverlayDocuments.Serialize(reopened.Draft);
        var files = Directory
            .GetFiles(OverlayMediaDirectory.DocumentDirectory(directory))
            .Order()
            .ToArray();
        var oversized = await ExportJourneyAsync(
            transfer,
            source,
            document with
            {
                Html = "<!--" + new string('z', 200_000) + "-->",
            }
        );
        using var compressed = new MemoryStream();
        using (var original = new ZipArchive(new MemoryStream(oversized), ZipArchiveMode.Read))
        using (var archive = new ZipArchive(compressed, ZipArchiveMode.Create, true))
        {
            foreach (var entry in original.Entries)
            {
                using var input = entry.Open();
                using var output = archive
                    .CreateEntry(entry.FullName, CompressionLevel.SmallestSize)
                    .Open();
                await input.CopyToAsync(output);
            }
        }
        compressed.Length.ShouldBeLessThan(Maximum);
        compressed.Position = 0;
        var rejected = (
            await transfer.ImportAsync(destination, compressed, CancellationToken.None)
        ).ShouldBeOfType<FullOverlayResult<FullOverlayImportApplied>.Rejected>();
        rejected.Reason.Kind.ShouldBe(FullOverlayRejectionKind.Invalid);
        rejected.Reason.Diagnostics.Single().Message.ShouldContain($"{Maximum}-byte limit");
        FullOverlayDocuments
            .Serialize(
                Value(
                    await documents.GetAsync(
                        destination,
                        imported.Overlay.Id,
                        CancellationToken.None
                    )
                ).Draft
            )
            .ShouldBe(baseline);
        (await File.ReadAllBytesAsync(content.Path)).ShouldBe([7, 8, 9, 10]);
        Directory
            .GetFiles(OverlayMediaDirectory.DocumentDirectory(directory))
            .Order()
            .ShouldBe(files);
        await using var final = factory.CreateDbContext();
        (await final.FullOverlays.CountAsync(row => row.HostId == host.Id)).ShouldBe(1);
        (await final.OverlayMediaAssets.CountAsync(row => row.HostId == host.Id)).ShouldBe(1);
        Console.WriteLine(
            $"Current application transfer/remap and compressed document rejection preserve data: {final.Database.ProviderName}."
        );
    }

    private static async Task<byte[]> ExportJourneyAsync(
        FullOverlayTransferService transfer,
        AuthenticatedSession session,
        FullOverlayDocument document
    )
    {
        await using var prepared = Value(
            await transfer.PrepareExportAsync(
                session,
                "Transferred document",
                document,
                CancellationToken.None
            )
        );
        using var output = new MemoryStream();
        await prepared.CopyToAsync(output);
        return output.ToArray();
    }
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Overlays.Full;

namespace BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;

internal static class FullOverlayPackageCodec
{
    private const string _manifest = "document.json";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static async Task<FullOverlayResult<FullOverlayPackage>> ReadAsync(
        ZipArchive archive,
        CancellationToken ct
    )
    {
        if (
            archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count()
                != archive.Entries.Count
            || archive.GetEntry(_manifest) is not { } entry
        )
        {
            return Rejected("The package must contain one document manifest and unique entries.");
        }
        await using var stream = await entry.OpenAsync(ct);
        var package = await JsonSerializer.DeserializeAsync<FullOverlayPackage>(stream, _json, ct);
        if (
            package is null
            || package.FormatVersion != 1
            || package.Document is null
            || !FullOverlayDocuments.HasCoherentIdentity(package.Document)
            || package.Document.Widgets.Any(widget => string.IsNullOrWhiteSpace(widget.Kind.Value))
            || package.Assets.IsDefault
            || package.Contents.IsDefault
            || package.Assets.Any(asset =>
                asset is null
                || asset.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(asset.Name)
                || asset.Name.Trim().Length > 128
            )
            || package.Contents.Any(content =>
                content is null || content.Id == Guid.Empty || content.ByteLength <= 0
            )
            || package.Assets.Select(asset => asset.Id).Distinct().Count() != package.Assets.Length
            || package.Contents.Select(content => content.Id).Distinct().Count()
                != package.Contents.Length
        )
        {
            return Rejected("The document or managed media references are invalid.");
        }
        var referenced = package
            .Document.Widgets.Where(widget => widget.Kind.Value is "image" or "audio" or "video")
            .Select(widget =>
                FullOverlayWidgetConfigurations
                    .Read<FullOverlayMediaConfiguration>(widget.Configuration)
                    ?.AssetId
            )
            .OfType<Guid>()
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var contents = package.Contents.ToDictionary(content => content.Id);
        return
            !referenced.SetEquals(package.Assets.Select(asset => asset.Id))
            || package.Assets.Any(asset => asset.ContentId is { } id && !contents.ContainsKey(id))
            || !package
                .Assets.Where(asset => asset.ContentId is not null)
                .Select(asset => asset.ContentId!.Value)
                .ToHashSet()
                .SetEquals(contents.Keys)
            || archive.Entries.Count != contents.Count + 1
            || package.Contents.Any(content =>
                archive.GetEntry(content.EntryName) is not { } media
                || media.Length != content.ByteLength
            )
            ? Rejected("Package entries do not match the declared managed media.")
            : new FullOverlayResult<FullOverlayPackage>.Succeeded(package);
    }

    internal static async Task WriteManifestAsync(
        ZipArchive archive,
        FullOverlayPackage package,
        CancellationToken ct
    )
    {
        var entry = archive.CreateEntry(_manifest, CompressionLevel.NoCompression);
        await using var stream = await entry.OpenAsync(ct);
        await JsonSerializer.SerializeAsync(stream, package, _json, ct);
    }

    private static FullOverlayResult<FullOverlayPackage> Rejected(string message) =>
        new FullOverlayResult<FullOverlayPackage>.Rejected(
            new(
                FullOverlayRejectionKind.Invalid,
                [new("invalid-package", message, FullOverlayDiagnosticSeverity.Error)]
            )
        );
}

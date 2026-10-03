using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;

internal sealed class FullOverlayTransferService(
    OverlayManagementAuthority authority,
    OverlayCueService media,
    FullOverlayService documents,
    FullOverlayPortability portability,
    ILogger<FullOverlayTransferService> logger
)
{
    internal async Task<FullOverlayResult<FullOverlayImportApplied>> ImportAsync(
        AuthenticatedSession session,
        Stream input,
        CancellationToken ct
    )
    {
        var authorization = await authority.AuthorizeAsync(session, ct);
        if (authorization is not OverlayManagementAuthorization.Granted granted)
        {
            return Rejected<FullOverlayImportApplied>(
                "The selected channel does not grant overlay management access.",
                FullOverlayRejectionKind.Unauthorized
            );
        }
        try
        {
            await using var transfer = await media.BeginTransferAsync(ct);
            await using var file = await transfer.StageArchiveAsync(input, ct);
            await using var archive = await ZipArchive.CreateAsync(
                file,
                ZipArchiveMode.Read,
                true,
                null,
                ct
            );
            var decoded = await FullOverlayPackageCodec.ReadAsync(archive, transfer, ct);
            if (decoded is FullOverlayResult<FullOverlayPackage>.Rejected invalid)
            {
                return new FullOverlayResult<FullOverlayImportApplied>.Rejected(invalid.Reason);
            }
            var package = ((FullOverlayResult<FullOverlayPackage>.Succeeded)decoded).Value;
            foreach (var content in package.Contents)
            {
                await using var stream = await archive.GetEntry(content.EntryName)!.OpenAsync(ct);
                if (await transfer.StageAsync(content, stream, ct) is { } rejected)
                {
                    return Rejected<FullOverlayImportApplied>(rejected.Message);
                }
            }
            return await documents.ApplyImportAsync(
                session,
                granted.Actor,
                package,
                transfer,
                portability,
                logger,
                ct
            );
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Rejected<FullOverlayImportApplied>(
                "The overlay package is malformed or incomplete."
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogWarning(
                "Full overlay import failed before commit ({FailureType}).",
                exception.GetType().FullName
            );
            return Rejected<FullOverlayImportApplied>(
                "The package could not be stored. No overlay or media changes were committed."
            );
        }
    }

    internal async Task<FullOverlayResult<FileStream>> PrepareExportAsync(
        AuthenticatedSession session,
        string name,
        FullOverlayDocument candidate,
        CancellationToken ct
    )
    {
        if (
            !FullOverlayDocuments.HasCoherentIdentity(candidate)
            || string.IsNullOrWhiteSpace(name)
            || name.Trim().Length > 128
        )
        {
            return Rejected<FileStream>("The overlay name or document is invalid.");
        }
        var authorization = await authority.AuthorizeAsync(session, ct);
        if (authorization is not OverlayManagementAuthorization.Granted granted)
        {
            return Rejected<FileStream>(
                "The selected channel does not grant overlay management access.",
                FullOverlayRejectionKind.Unauthorized
            );
        }
        await using var transfer = await media.BeginTransferAsync(ct);
        var declarations = portability.Capture();
        var portable = portability.Map(candidate, declarations);
        var ids = portable
            .Widgets.Where(widget => widget.Kind.Value is "image" or "audio" or "video")
            .Select(widget =>
                FullOverlayWidgetConfigurations
                    .Read<FullOverlayMediaConfiguration>(widget.Configuration)
                    ?.AssetId
            )
            .OfType<Guid>()
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var available = await transfer.ReadAsync(granted.Actor.HostId, ids, ct);
        var contentIds = available
            .Select(asset => asset.DocumentId)
            .Distinct()
            .ToDictionary(id => id, _ => Guid.NewGuid());
        var assets = ids.Select(id =>
                available.FirstOrDefault(asset => asset.AssetId == id) is { } asset
                    ? new FullOverlayPackageAsset(id, asset.Name, contentIds[asset.DocumentId])
                    : new FullOverlayPackageAsset(id, "Unavailable media", null)
            )
            .ToImmutableArray();
        var contents = available
            .DistinctBy(asset => asset.DocumentId)
            .Select(asset => new FullOverlayPackageContent(
                contentIds[asset.DocumentId],
                asset.ContentType,
                asset.ByteLength
            ))
            .ToImmutableArray();
        if (
            !portability.IsCurrent(declarations)
            || await authority.AuthorizeAsync(session, ct)
                is not OverlayManagementAuthorization.Granted current
            || current.Actor != granted.Actor
        )
        {
            return Rejected<FileStream>(
                "The channel authority or plugin declarations changed. Retry the export.",
                FullOverlayRejectionKind.Conflict
            );
        }
        var output = transfer.CreateExportStream();
        try
        {
            await using (
                var archive = await ZipArchive.CreateAsync(
                    output,
                    ZipArchiveMode.Create,
                    true,
                    null,
                    ct
                )
            )
            {
                await FullOverlayPackageCodec.WriteManifestAsync(
                    archive,
                    new(1, name.Trim(), portable, assets, contents),
                    ct
                );
                foreach (var content in contents)
                {
                    var source = available.First(asset =>
                        contentIds[asset.DocumentId] == content.Id
                    );
                    var entry = archive.CreateEntry(
                        content.EntryName,
                        CompressionLevel.NoCompression
                    );
                    await using var destination = await entry.OpenAsync(ct);
                    await using var file = new FileStream(
                        source.Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        FileOptions.Asynchronous
                    );
                    if (file.Length != content.ByteLength)
                    {
                        throw new IOException("Current media length changed.");
                    }
                    await file.CopyToAsync(destination, ct);
                }
            }
            output.Position = 0;
            // Only ZIP creation holds the media gate; HTTP copying owns the open temporary handle.
            return new FullOverlayResult<FileStream>.Succeeded(output);
        }
        catch
        {
            await output.DisposeAsync();
            throw;
        }
    }

    private static FullOverlayResult<T> Rejected<T>(
        string message,
        FullOverlayRejectionKind kind = FullOverlayRejectionKind.Invalid
    ) =>
        new FullOverlayResult<T>.Rejected(
            new(kind, [new("package-transfer", message, FullOverlayDiagnosticSeverity.Error)])
        );
}

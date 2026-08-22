using System.Security.Cryptography;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService
{
    private async Task<OverlayCueResult<OverlayMediaAssetView>> StoreAssetAsync(
        int hostId,
        string name,
        string claimedContentType,
        Stream content,
        (Guid AssetId, int ExpectedRevision)? replacement,
        CancellationToken cancellationToken
    )
    {
        var declaredContentType = OverlayMediaTypes.NormalizeDeclaration(claimedContentType);
        if (declaredContentType is null)
        {
            return Reject<OverlayMediaAssetView>(
                new OverlayCueRejection.Invalid("Unsupported file type")
            );
        }

        var root = DocumentDirectory();
        var tempPath = Path.Combine(root, $".upload-{Guid.NewGuid():N}");
        string? finalPath = null;
        var committed = false;
        var publicationCommitted = false;
        try
        {
            var length = await WriteUploadAsync(content, tempPath, cancellationToken);
            if (length == 0)
            {
                return Reject<OverlayMediaAssetView>(
                    new OverlayCueRejection.Invalid("The uploaded media file is empty.")
                );
            }

            await mediaMaintenance.Gate.WaitAsync(cancellationToken);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                if (!await ParentEnabledAsync(db, hostId, cancellationToken))
                {
                    return Reject<OverlayMediaAssetView>(new OverlayCueRejection.ParentDisabled());
                }

                OverlayMediaAsset? asset = null;
                OverlayMediaDocument? previousDocument = null;
                if (replacement is { } replace)
                {
                    asset = await db
                        .OverlayMediaAssets.Include(value => value.Document)
                        .SingleOrDefaultAsync(
                            value =>
                                value.HostId == hostId
                                && value.PublicId == replace.AssetId
                                && value.ContentRevision == replace.ExpectedRevision,
                            cancellationToken
                        );
                    if (asset is null)
                    {
                        return Reject<OverlayMediaAssetView>(new OverlayCueRejection.Conflict());
                    }
                    previousDocument = asset.Document;
                    if (
                        OverlayMediaTypes.Kind(previousDocument.ContentType)
                            != OverlayMediaTypes.Kind(declaredContentType)
                        && await db.OverlayCueMediaAssetReferences.AnyAsync(
                            value => value.HostId == hostId && value.AssetId == asset.Id,
                            cancellationToken
                        )
                    )
                    {
                        return Reject<OverlayMediaAssetView>(
                            new OverlayCueRejection.Invalid(
                                "Remove this asset from every cue before replacing it with a different media type."
                            )
                        );
                    }
                }

                if (
                    await WouldExceedQuotaAsync(
                        db,
                        hostId,
                        previousDocument,
                        asset?.Id,
                        length,
                        cancellationToken
                    )
                )
                {
                    return Reject<OverlayMediaAssetView>(
                        new OverlayCueRejection.Invalid(
                            "This upload would exceed the channel media storage quota."
                        )
                    );
                }

                var storageKey = Convert
                    .ToHexString(RandomNumberGenerator.GetBytes(16))
                    .ToLowerInvariant();
                finalPath = Path.Combine(root, storageKey);
                var document = new OverlayMediaDocument
                {
                    Id = Guid.NewGuid(),
                    ContentType = declaredContentType,
                    ByteLength = length,
                    StorageKey = storageKey,
                    State = OverlayMediaDocumentState.Publishing,
                    CreatedAtUtc = Now(),
                    UpdatedAtUtc = Now(),
                };

                await using (
                    var publication = await db.Database.BeginTransactionAsync(cancellationToken)
                )
                {
                    _ = db.OverlayMediaDocuments.Add(document);
                    _ = await db.SaveChangesAsync(cancellationToken);
                    await publication.CommitAsync(cancellationToken);
                    publicationCommitted = true;
                }

                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                File.Move(tempPath, finalPath);
                document.State = OverlayMediaDocumentState.Available;
                if (asset is null)
                {
                    asset = new OverlayMediaAsset
                    {
                        PublicId = Guid.NewGuid(),
                        HostId = hostId,
                        Name = name,
                        ContentRevision = 1,
                        DocumentId = document.Id,
                        Document = document,
                        CreatedAtUtc = Now(),
                        UpdatedAtUtc = Now(),
                    };
                    _ = db.OverlayMediaAssets.Add(asset);
                }
                else
                {
                    asset.DocumentId = document.Id;
                    asset.Document = document;
                    asset.ContentRevision++;
                    asset.UpdatedAtUtc = Now();
                }

                _ = await db.SaveChangesAsync(cancellationToken);
                if (previousDocument is not null)
                {
                    await MarkOrphanIfUnreferencedAsync(db, previousDocument.Id, cancellationToken);
                    _ = await db.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                mediaMaintenance.Schedule();
                _ = await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
                return Success(ToView(asset, asset.Document.ByteLength));
            }
            finally
            {
                _ = mediaMaintenance.Gate.Release();
            }
        }
        catch (UploadTooLargeException)
        {
            return Reject<OverlayMediaAssetView>(
                new OverlayCueRejection.Invalid(
                    $"The upload exceeds the {_options.Overlays.Media.MaximumUploadBytes}-byte limit."
                )
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            return Reject<OverlayMediaAssetView>(new OverlayCueRejection.StorageUnavailable());
        }
        finally
        {
            TryDelete(tempPath);
            if (!committed && finalPath is not null)
            {
                TryDelete(finalPath);
            }
            if (publicationCommitted && !committed)
            {
                mediaMaintenance.Schedule();
            }
        }
    }
}

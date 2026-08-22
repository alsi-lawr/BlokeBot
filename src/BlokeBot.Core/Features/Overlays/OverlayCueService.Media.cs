using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService
{
    public async Task<OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>> ListAssetsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken
    )
    {
        var actor = await AuthorizeAsync(session, cancellationToken);
        if (actor is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<IReadOnlyList<OverlayMediaAssetView>>(rejected.Reason);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hostId = ((OverlayCueResult<OverlayManagementActor>.Succeeded)actor).Value.HostId;
        var assets = await db
            .OverlayMediaAssets.AsNoTracking()
            .Include(value => value.Document)
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .ToArrayAsync(cancellationToken);
        var chargedDocuments = new HashSet<Guid>();
        return Success<IReadOnlyList<OverlayMediaAssetView>>(
            assets
                .Select(asset =>
                    ToView(
                        asset,
                        chargedDocuments.Add(asset.DocumentId) ? asset.Document.ByteLength : 0
                    )
                )
                .ToArray()
        );
    }

    public async Task<OverlayCueResult<OverlayMediaAssetView>> UploadAssetAsync(
        AuthenticatedSession session,
        string name,
        string claimedContentType,
        Stream content,
        CancellationToken cancellationToken
    )
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length is < 1 or > 128 || !content.CanRead)
        {
            return Reject<OverlayMediaAssetView>(
                new OverlayCueRejection.Invalid(
                    "A media name from 1 to 128 characters and readable content are required."
                )
            );
        }
        var authorization = await AuthorizeAsync(session, cancellationToken);
        if (authorization is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<OverlayMediaAssetView>(rejected.Reason);
        }
        var actor = ((OverlayCueResult<OverlayManagementActor>.Succeeded)authorization).Value;
        return await StoreAssetAsync(
            actor.HostId,
            trimmedName,
            claimedContentType,
            content,
            null,
            cancellationToken
        );
    }

    public async Task<OverlayCueResult<OverlayMediaAssetView>> ReplaceAssetAsync(
        AuthenticatedSession session,
        ReplaceOverlayMediaAssetCommand command,
        CancellationToken cancellationToken
    )
    {
        if (
            command.AssetId == Guid.Empty
            || command.ExpectedContentRevision <= 0
            || !command.Content.CanRead
        )
        {
            return Reject<OverlayMediaAssetView>(
                new OverlayCueRejection.Invalid(
                    "An asset, content revision, and readable content are required."
                )
            );
        }
        var authorization = await AuthorizeAsync(session, cancellationToken);
        if (authorization is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<OverlayMediaAssetView>(rejected.Reason);
        }
        var actor = ((OverlayCueResult<OverlayManagementActor>.Succeeded)authorization).Value;
        return await StoreAssetAsync(
            actor.HostId,
            string.Empty,
            command.ClaimedContentType,
            command.Content,
            (command.AssetId, command.ExpectedContentRevision),
            cancellationToken
        );
    }

    public async Task<OverlayCueResult<Guid>> DeleteAssetAsync(
        AuthenticatedSession session,
        Guid assetId,
        int expectedContentRevision,
        CancellationToken cancellationToken
    )
    {
        if (assetId == Guid.Empty || expectedContentRevision <= 0)
        {
            return Reject<Guid>(
                new OverlayCueRejection.Invalid("An asset and content revision are required.")
            );
        }
        var authorization = await AuthorizeAsync(session, cancellationToken);
        if (authorization is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<Guid>(rejected.Reason);
        }
        var actor = ((OverlayCueResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await mediaMaintenance.Gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await ParentEnabledAsync(db, actor.HostId, cancellationToken))
            {
                return Reject<Guid>(new OverlayCueRejection.ParentDisabled());
            }
            var asset = await db.OverlayMediaAssets.SingleOrDefaultAsync(
                value =>
                    value.HostId == actor.HostId
                    && value.PublicId == assetId
                    && value.ContentRevision == expectedContentRevision,
                cancellationToken
            );
            if (asset is null)
            {
                return Reject<Guid>(new OverlayCueRejection.Missing());
            }
            if (
                await db.OverlayCueMediaAssetReferences.AnyAsync(
                    value => value.HostId == actor.HostId && value.AssetId == asset.Id,
                    cancellationToken
                )
            )
            {
                return Reject<Guid>(new OverlayCueRejection.InUse());
            }

            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var documentId = asset.DocumentId;
            _ = db.OverlayMediaAssets.Remove(asset);
            _ = await db.SaveChangesAsync(cancellationToken);
            await MarkOrphanIfUnreferencedAsync(db, documentId, cancellationToken);
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _ = mediaMaintenance.Gate.Release();
        }

        mediaMaintenance.Schedule();
        _ = await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
        return Success(assetId);
    }

    internal async Task<OverlayMediaContent?> ResolveContentAsync(
        int hostId,
        Guid assetId,
        int contentRevision,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var value = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(asset =>
                asset.HostId == hostId
                && asset.PublicId == assetId
                && asset.ContentRevision == contentRevision
            )
            .Join(
                db.OverlayMediaDocuments.AsNoTracking(),
                asset => asset.DocumentId,
                document => document.Id,
                (asset, document) => new { Asset = asset, Document = document }
            )
            .Join(
                db.Hosts.AsNoTracking(),
                value => value.Asset.HostId,
                host => host.Id,
                (value, host) =>
                    new
                    {
                        value.Asset,
                        value.Document,
                        host.EnabledFeatures,
                    }
            )
            .SingleOrDefaultAsync(cancellationToken);
        if (
            value is null
            || value.Document.State != OverlayMediaDocumentState.Available
            || (value.EnabledFeatures & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays
        )
        {
            return null;
        }
        var path = DocumentPath(value.Document.StorageKey);
        return File.Exists(path)
            ? new(
                hostId,
                assetId,
                value.Document.ContentType,
                value.Document.ByteLength,
                value.Asset.ContentRevision,
                path
            )
            : null;
    }

    private async Task MarkOrphanIfUnreferencedAsync(
        BlokeBotDbContext db,
        Guid documentId,
        CancellationToken cancellationToken
    )
    {
        if (
            await db.OverlayMediaAssets.AnyAsync(
                reference => reference.DocumentId == documentId,
                cancellationToken
            )
        )
        {
            return;
        }

        var document = await db.OverlayMediaDocuments.SingleAsync(
            value => value.Id == documentId,
            cancellationToken
        );
        document.State = OverlayMediaDocumentState.Orphaned;
        document.OrphanedAtUtc = Now();
        document.UpdatedAtUtc = Now();
    }
}

using System.Collections.Immutable;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService
{
    // Holding the existing gate makes current-byte reads coherent and prevents recovery from
    // deleting active import staging. After interruption these unknown files are reclaimable.
    internal async Task<OverlayMediaTransfer> BeginTransferAsync(CancellationToken ct)
    {
        await mediaMaintenance.Gate.WaitAsync(ct);
        try
        {
            return new(this, DocumentDirectory());
        }
        catch
        {
            _ = mediaMaintenance.Gate.Release();
            throw;
        }
    }

    private Task<BlokeBotDbContext> TransferContextAsync(CancellationToken ct) =>
        dbFactory.CreateDbContextAsync(ct);

    private SemaphoreSlim _transferGate => mediaMaintenance.Gate;

    private void ScheduleTransferMaintenance() => mediaMaintenance.Schedule();

    internal sealed class OverlayMediaTransfer(OverlayCueService owner, string root)
        : IAsyncDisposable
    {
        private readonly List<string> _ownedPaths = [];
        private readonly Dictionary<
            Guid,
            (FullOverlayPackageContent Content, string Path)
        > _staged = [];
        private bool _committed;

        internal FileStream CreateExportStream()
        {
            var path = Path.Combine(root, $".import-export-{Guid.NewGuid():N}");
            _ownedPaths.Add(path);
            // The open handle survives ordinary recovery/unlink; close owns its final cleanup.
            return new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose
            );
        }

        internal async Task<FileStream> StageArchiveAsync(Stream source, CancellationToken ct)
        {
            var path = Path.Combine(root, $".import-package-{Guid.NewGuid():N}");
            _ownedPaths.Add(path);
            await using (
                var output = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous
                )
            )
            {
                await source.CopyToAsync(output, ct);
            }
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous
            );
        }

        internal async Task<OverlayCueResult<FileStream>> StageDocumentAsync(
            Stream source,
            CancellationToken ct
        )
        {
            var path = Path.Combine(root, $".import-document-{Guid.NewGuid():N}");
            _ownedPaths.Add(path);
            var copied = await owner.WriteUploadAsync(source, path, ct);
            return copied is OverlayCueResult<long>.Rejected rejected
                ? Reject<FileStream>(rejected.Reason)
                : Success(
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        FileOptions.Asynchronous
                    )
                );
        }

        internal async Task<ImmutableArray<OverlayMediaTransferSource>> ReadAsync(
            int hostId,
            IReadOnlySet<Guid> ids,
            CancellationToken ct
        )
        {
            await using var db = await owner.TransferContextAsync(ct);
            var rows = await db
                .OverlayMediaAssets.AsNoTracking()
                .Include(asset => asset.Document)
                .Where(asset => asset.HostId == hostId && ids.Contains(asset.PublicId))
                .ToArrayAsync(ct);
            return
            [
                .. rows.Where(asset =>
                        asset.Document.State == OverlayMediaDocumentState.Available
                        && File.Exists(Path.Combine(root, asset.Document.StorageKey))
                    )
                    .Select(asset => new OverlayMediaTransferSource(
                        asset.PublicId,
                        asset.Name,
                        asset.DocumentId,
                        asset.Document.ContentType,
                        asset.Document.ByteLength,
                        Path.Combine(root, asset.Document.StorageKey)
                    )),
            ];
        }

        internal async Task<OverlayCueRejection?> StageAsync(
            FullOverlayPackageContent content,
            Stream input,
            CancellationToken ct
        )
        {
            var contentType = OverlayMediaTypes.NormalizeDeclaration(content.ContentType);
            if (contentType is null || content.ByteLength <= 0 || !input.CanRead)
            {
                return new OverlayCueRejection.Invalid(
                    "A concrete media type and nonempty readable file are required."
                );
            }
            var path = Path.Combine(root, $".import-{Guid.NewGuid():N}");
            _ownedPaths.Add(path);
            var copied = await owner.WriteUploadAsync(input, path, ct);
            if (copied is OverlayCueResult<long>.Rejected rejected)
            {
                return rejected.Reason;
            }
            var length = ((OverlayCueResult<long>.Succeeded)copied).Value;
            if (length != content.ByteLength)
            {
                return new OverlayCueRejection.Invalid(
                    "Packaged media length does not match its declared length."
                );
            }
            _staged.Add(content.Id, (content with { ContentType = contentType }, path));
            return null;
        }

        internal async Task<OverlayCueResult<IReadOnlyDictionary<Guid, Guid>>> AttachAsync(
            BlokeBotDbContext db,
            int hostId,
            ImmutableArray<FullOverlayPackageAsset> assets,
            CancellationToken ct
        )
        {
            if (
                await owner.WouldExceedQuotaAsync(
                    db,
                    hostId,
                    null,
                    null,
                    _staged.Values.Sum(value => value.Content.ByteLength),
                    ct
                )
            )
            {
                return Reject<IReadOnlyDictionary<Guid, Guid>>(
                    new OverlayCueRejection.Invalid(
                        "This import would exceed the channel media storage quota."
                    )
                );
            }
            var documents = new Dictionary<Guid, OverlayMediaDocument>();
            foreach (var (id, staged) in _staged)
            {
                var storageKey = Guid.NewGuid().ToString("N");
                var path = Path.Combine(root, storageKey);
                _ownedPaths.Add(path);
                File.Move(staged.Path, path);
                var document = new OverlayMediaDocument
                {
                    Id = Guid.NewGuid(),
                    StorageKey = storageKey,
                    ContentType = staged.Content.ContentType,
                    ByteLength = staged.Content.ByteLength,
                    State = OverlayMediaDocumentState.Available,
                    CreatedAtUtc = owner.Now(),
                    UpdatedAtUtc = owner.Now(),
                };
                documents.Add(id, document);
                _ = db.OverlayMediaDocuments.Add(document);
            }
            var remap = new Dictionary<Guid, Guid>();
            foreach (var asset in assets.Where(asset => asset.ContentId is not null))
            {
                var document = documents[asset.ContentId!.Value];
                var id = Guid.NewGuid();
                _ = db.OverlayMediaAssets.Add(
                    new OverlayMediaAsset
                    {
                        PublicId = id,
                        HostId = hostId,
                        Name = asset.Name.Trim(),
                        ContentRevision = 1,
                        DocumentId = document.Id,
                        Document = document,
                        CreatedAtUtc = owner.Now(),
                        UpdatedAtUtc = owner.Now(),
                    }
                );
                remap.Add(asset.Id, id);
            }
            return Success<IReadOnlyDictionary<Guid, Guid>>(remap);
        }

        internal void Committed() => _committed = true;

        public ValueTask DisposeAsync()
        {
            foreach (
                var path in _ownedPaths.Where(path =>
                    !_committed
                    || Path.GetFileName(path).StartsWith(".import-", StringComparison.Ordinal)
                )
            )
            {
                owner.TryDelete(path);
            }
            owner.ScheduleTransferMaintenance();
            _ = owner._transferGate.Release();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record OverlayMediaTransferSource(
    Guid AssetId,
    string Name,
    Guid DocumentId,
    string ContentType,
    long ByteLength,
    string Path
);

using System.Collections.Concurrent;
using System.Security.Cryptography;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class OverlayCueService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    OverlayManagementAuthority authority,
    OverlayRemoteUrlPolicy urlPolicy,
    IOptions<BlokeBotOptions> options,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    IOverlayMediaFileDeletion fileDeletion
)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _hostGates = new();
    private readonly BlokeBotOptions _options = options.Value;

    public async Task<OverlayCueResult<IReadOnlyList<OverlayCueView>>> ListCuesAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken
    )
    {
        var actor = await AuthorizeAsync(session, cancellationToken);
        if (actor is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<IReadOnlyList<OverlayCueView>>(rejected.Reason);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hostId = ((OverlayCueResult<OverlayManagementActor>.Succeeded)actor).Value.HostId;
        var cues = await db
            .OverlayCues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .ToArrayAsync(cancellationToken);
        return Success<IReadOnlyList<OverlayCueView>>(cues.Select(ToView).ToArray());
    }

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
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .ToArrayAsync(cancellationToken);
        return Success<IReadOnlyList<OverlayMediaAssetView>>(assets.Select(ToView).ToArray());
    }

    public async Task<OverlayCueResult<OverlayCueView>> SaveCueAsync(
        AuthenticatedSession session,
        SaveOverlayCueCommand command,
        CancellationToken cancellationToken
    )
    {
        var name = command.Name.Trim();
        if (
            name.Length is < 1 or > 128
            || command.DurationMilliseconds is < 100 or > 300000
            || !Enum.IsDefined(command.QueuePolicy)
            || (command.CueId is not null && command.ExpectedRevision.Value <= 0)
        )
        {
            return Reject<OverlayCueView>(
                new OverlayCueRejection.Invalid(
                    "Cue name, duration, queue policy, and revision are invalid."
                )
            );
        }

        var parsed = OverlayCueConfiguration.Parse(command.ConfigurationJson);
        if (parsed is OverlayCueConfigurationResult.Invalid invalid)
        {
            return Reject<OverlayCueView>(new OverlayCueRejection.Invalid(invalid.Message));
        }
        var configuration = ((OverlayCueConfigurationResult.Valid)parsed).Value;
        if (
            configuration.Layers.Any(layer =>
                layer.StartOffsetMilliseconds + layer.DurationMilliseconds
                > command.DurationMilliseconds
            )
        )
        {
            return Reject<OverlayCueView>(
                new OverlayCueRejection.Invalid("Every layer must finish within the cue duration.")
            );
        }

        var authorization = await AuthorizeAsync(session, cancellationToken);
        if (authorization is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<OverlayCueView>(rejected.Reason);
        }
        var actor = ((OverlayCueResult<OverlayManagementActor>.Succeeded)authorization).Value;

        foreach (
            var url in configuration
                .Layers.Select(layer =>
                    layer switch
                    {
                        OverlayCueLayer.RemoteMedia remote => remote.Url,
                        OverlayCueLayer.ExternalWeb web => web.Url,
                        _ => null,
                    }
                )
                .OfType<Uri>()
        )
        {
            var decision = await urlPolicy.ValidateAsync(url, cancellationToken);
            if (decision is OverlayRemoteUrlDecision.Rejected urlRejected)
            {
                return Reject<OverlayCueView>(new OverlayCueRejection.Invalid(urlRejected.Message));
            }
        }

        var gate = _hostGates.GetOrAdd(actor.HostId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await ParentEnabledAsync(db, actor.HostId, cancellationToken))
            {
                return Reject<OverlayCueView>(new OverlayCueRejection.ParentDisabled());
            }
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );

            var assetIds = configuration.ReferencedAssetIds;
            var assets = await db
                .OverlayMediaAssets.Where(value =>
                    value.HostId == actor.HostId && assetIds.Contains(value.PublicId)
                )
                .ToArrayAsync(cancellationToken);
            if (assets.Length != assetIds.Length)
            {
                return Reject<OverlayCueView>(
                    new OverlayCueRejection.Invalid(
                        "Every uploaded-media layer must reference a media asset from this channel."
                    )
                );
            }
            foreach (var layer in configuration.Layers.OfType<OverlayCueLayer.UploadedMedia>())
            {
                var asset = assets.Single(value => value.PublicId == layer.AssetId);
                if (layer.MediaKind != OverlayMediaTypes.Kind(asset.ContentType))
                {
                    return Reject<OverlayCueView>(
                        new OverlayCueRejection.Invalid(
                            "An uploaded-media layer kind must match the detected asset type."
                        )
                    );
                }
            }

            OverlayCue cue;
            if (command.CueId is null)
            {
                var now = Now();
                cue = new OverlayCue
                {
                    PublicId = Guid.NewGuid(),
                    HostId = actor.HostId,
                    Name = name,
                    IsEnabled = command.IsEnabled,
                    DurationMilliseconds = command.DurationMilliseconds,
                    QueuePolicy = command.QueuePolicy,
                    ConfigurationJson = configuration.ToPersistenceJson(),
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.OverlayCues.Add(cue);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                cue =
                    await db.OverlayCues.SingleOrDefaultAsync(
                        value =>
                            value.HostId == actor.HostId && value.PublicId == command.CueId.Value,
                        cancellationToken
                    ) ?? null!;
                if (cue is null)
                {
                    return Reject<OverlayCueView>(new OverlayCueRejection.Missing());
                }
                if (cue.Revision != command.ExpectedRevision.Value)
                {
                    return Reject<OverlayCueView>(new OverlayCueRejection.Conflict());
                }

                cue.Name = name;
                cue.IsEnabled = command.IsEnabled;
                cue.DurationMilliseconds = command.DurationMilliseconds;
                cue.QueuePolicy = command.QueuePolicy;
                cue.ConfigurationJson = configuration.ToPersistenceJson();
                cue.Revision++;
                cue.UpdatedAtUtc = Now();
                var oldReferences = await db
                    .OverlayCueMediaAssetReferences.Where(value => value.CueId == cue.Id)
                    .ToArrayAsync(cancellationToken);
                db.OverlayCueMediaAssetReferences.RemoveRange(oldReferences);
            }

            foreach (var asset in assets)
            {
                db.OverlayCueMediaAssetReferences.Add(
                    new OverlayCueMediaAssetReference
                    {
                        CueId = cue.Id,
                        AssetId = asset.Id,
                        HostId = actor.HostId,
                    }
                );
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
            return Success(ToView(cue));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Reject<OverlayCueView>(new OverlayCueRejection.Conflict());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OverlayCueResult<Guid>> DeleteCueAsync(
        AuthenticatedSession session,
        Guid cueId,
        OverlayCueRevision expectedRevision,
        CancellationToken cancellationToken
    )
    {
        if (cueId == Guid.Empty || expectedRevision.Value <= 0)
        {
            return Reject<Guid>(
                new OverlayCueRejection.Invalid("A cue and revision are required.")
            );
        }
        var authorization = await AuthorizeAsync(session, cancellationToken);
        if (authorization is OverlayCueResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<Guid>(rejected.Reason);
        }
        var actor = ((OverlayCueResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await ParentEnabledAsync(db, actor.HostId, cancellationToken))
        {
            return Reject<Guid>(new OverlayCueRejection.ParentDisabled());
        }
        var deleted = await db
            .OverlayCues.Where(value =>
                value.HostId == actor.HostId
                && value.PublicId == cueId
                && value.Revision == expectedRevision.Value
            )
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return Reject<Guid>(new OverlayCueRejection.Missing());
        }
        await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
        return Success(cueId);
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
        var gate = _hostGates.GetOrAdd(actor.HostId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
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
            var path = ContentPath(actor.HostId, asset.StorageKey);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            db.OverlayMediaAssets.Remove(asset);
            await db.SaveChangesAsync(cancellationToken);
            if (fileDeletion.Delete(path) is OverlayMediaFileDeletionOutcome.Unavailable)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Reject<Guid>(new OverlayCueRejection.StorageUnavailable());
            }
            await transaction.CommitAsync(cancellationToken);
            await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
            return Success(assetId);
        }
        finally
        {
            gate.Release();
        }
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
                db.Hosts.AsNoTracking(),
                asset => asset.HostId,
                host => host.Id,
                (asset, host) => new { Asset = asset, host.EnabledFeatures }
            )
            .SingleOrDefaultAsync(cancellationToken);
        if (
            value is null
            || (value.EnabledFeatures & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays
        )
        {
            return null;
        }
        var path = ContentPath(hostId, value.Asset.StorageKey);
        return File.Exists(path)
            ? new(
                hostId,
                assetId,
                value.Asset.ContentType,
                value.Asset.ByteLength,
                value.Asset.ContentRevision,
                path
            )
            : null;
    }

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

        var root = HostContentDirectory(hostId);
        Directory.CreateDirectory(root);
        var tempPath = Path.Combine(root, $".upload-{Guid.NewGuid():N}");
        string? finalPath = null;
        try
        {
            long length;
            await using (
                var destination = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough
                )
            )
            {
                length = await CopyBoundedAsync(
                    content,
                    destination,
                    _options.Overlays.Media.MaximumUploadBytes,
                    cancellationToken
                );
                await destination.FlushAsync(cancellationToken);
            }
            if (length == 0)
            {
                return Reject<OverlayMediaAssetView>(
                    new OverlayCueRejection.Invalid("The uploaded media file is empty.")
                );
            }
            var gate = _hostGates.GetOrAdd(hostId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                if (!await ParentEnabledAsync(db, hostId, cancellationToken))
                {
                    return Reject<OverlayMediaAssetView>(new OverlayCueRejection.ParentDisabled());
                }
                OverlayMediaAsset asset;
                string? replacedPath = null;
                if (replacement is { } replace)
                {
                    asset =
                        await db.OverlayMediaAssets.SingleOrDefaultAsync(
                            value =>
                                value.HostId == hostId
                                && value.PublicId == replace.AssetId
                                && value.ContentRevision == replace.ExpectedRevision,
                            cancellationToken
                        ) ?? null!;
                    if (asset is null)
                    {
                        return Reject<OverlayMediaAssetView>(new OverlayCueRejection.Conflict());
                    }
                    if (
                        OverlayMediaTypes.Kind(asset.ContentType)
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
                    replacedPath = ContentPath(hostId, asset.StorageKey);
                }
                else
                {
                    asset = new OverlayMediaAsset
                    {
                        PublicId = Guid.NewGuid(),
                        HostId = hostId,
                        Name = name,
                        ContentRevision = 1,
                        CreatedAtUtc = Now(),
                    };
                    db.OverlayMediaAssets.Add(asset);
                }

                var storage = MeasureStoredBytes(root, tempPath);
                if (storage is StoredByteMeasurement.Unavailable)
                {
                    return Reject<OverlayMediaAssetView>(
                        new OverlayCueRejection.StorageUnavailable()
                    );
                }
                var measured = (StoredByteMeasurement.Available)storage;
                var replacedStoredLength =
                    replacedPath is not null
                    && measured.LengthByPath.TryGetValue(replacedPath, out var existingLength)
                        ? existingLength
                        : 0;
                if (
                    measured.TotalBytes - replacedStoredLength + length
                    > _options.Overlays.Media.MaximumHostStorageBytes
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
                finalPath = ContentPath(hostId, storageKey);
                await using var transaction = replacement is not null
                    ? await db.Database.BeginTransactionAsync(cancellationToken)
                    : null;
                File.Move(tempPath, finalPath);
                asset.ContentType = declaredContentType;
                asset.ByteLength = length;
                asset.StorageKey = storageKey;
                asset.UpdatedAtUtc = Now();
                if (replacement is not null)
                {
                    asset.ContentRevision++;
                }
                await db.SaveChangesAsync(cancellationToken);
                if (replacedPath is not null)
                {
                    if (
                        fileDeletion.Delete(replacedPath)
                        is OverlayMediaFileDeletionOutcome.Unavailable
                    )
                    {
                        await transaction!.RollbackAsync(cancellationToken);
                        TryDelete(finalPath);
                        return Reject<OverlayMediaAssetView>(
                            new OverlayCueRejection.StorageUnavailable()
                        );
                    }
                    await transaction!.CommitAsync(cancellationToken);
                }
                await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
                return Success(ToView(asset));
            }
            catch
            {
                if (finalPath is not null)
                {
                    TryDelete(finalPath);
                }
                throw;
            }
            finally
            {
                gate.Release();
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
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task<OverlayCueResult<OverlayManagementActor>> AuthorizeAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken
    )
    {
        var result = await authority.AuthorizeAsync(session, cancellationToken);
        return result switch
        {
            OverlayManagementAuthorization.Granted granted => Success(granted.Actor),
            OverlayManagementAuthorization.Rejected
            {
                Reason: OverlayManagementRejection.ParentDisabled
            } => Reject<OverlayManagementActor>(new OverlayCueRejection.ParentDisabled()),
            _ => Reject<OverlayManagementActor>(new OverlayCueRejection.Unauthorized()),
        };
    }

    private static async Task<bool> ParentEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Hosts.AsNoTracking()
            .Where(host =>
                host.Id == hostId
                && (host.EnabledFeatures & HostFeatureFlags.Overlays) == HostFeatureFlags.Overlays
            )
            .AnyAsync(cancellationToken);

    private string HostContentDirectory(int hostId)
    {
        var databaseDirectory =
            Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath))
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        var directory = Path.Combine(databaseDirectory, "overlay-media", hostId.ToString());
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
        return directory;
    }

    private string ContentPath(int hostId, string storageKey) =>
        Path.Combine(HostContentDirectory(hostId), storageKey);

    private static async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }
            total += read;
            if (total > maximumBytes)
            {
                throw new UploadTooLargeException();
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static OverlayCueView ToView(OverlayCue value)
    {
        var parsed = OverlayCueConfiguration.Parse(value.ConfigurationJson);
        var configuration = parsed is OverlayCueConfigurationResult.Valid valid
            ? valid.Value
            : throw new InvalidOperationException("Persisted cue configuration is invalid.");
        return new(
            value.PublicId,
            value.Name,
            value.IsEnabled,
            value.DurationMilliseconds,
            value.QueuePolicy,
            configuration,
            new OverlayCueRevision(value.Revision),
            AsOffset(value.UpdatedAtUtc)
        );
    }

    private static OverlayMediaAssetView ToView(OverlayMediaAsset value) =>
        new(
            value.PublicId,
            value.Name,
            value.ContentType,
            value.ByteLength,
            value.ContentRevision,
            AsOffset(value.UpdatedAtUtc)
        );

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private StoredByteMeasurement MeasureStoredBytes(string root, string currentUploadPath)
    {
        try
        {
            var lengths = Directory
                .EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(path, currentUploadPath, StringComparison.Ordinal))
                .ToDictionary(path => path, path => new FileInfo(path).Length);
            return new StoredByteMeasurement.Available(lengths.Values.Sum(), lengths);
        }
        catch (IOException)
        {
            return new StoredByteMeasurement.Unavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return new StoredByteMeasurement.Unavailable();
        }
    }

    private void TryDelete(string path) => _ = fileDeletion.Delete(path);

    private static OverlayCueResult<T>.Succeeded Success<T>(T value) => new(value);

    private static OverlayCueResult<T>.Rejected Reject<T>(OverlayCueRejection reason) =>
        new(reason);

    private sealed class UploadTooLargeException : Exception;

    private abstract record StoredByteMeasurement
    {
        private StoredByteMeasurement() { }

        internal sealed record Available(
            long TotalBytes,
            IReadOnlyDictionary<string, long> LengthByPath
        ) : StoredByteMeasurement;

        internal sealed record Unavailable : StoredByteMeasurement;
    }
}

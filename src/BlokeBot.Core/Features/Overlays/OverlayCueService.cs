using System.Collections.Concurrent;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    OverlayManagementAuthority authority,
    OverlayRemoteUrlPolicy urlPolicy,
    IOptions<BlokeBotOptions> options,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    IOverlayMediaFileDeletion fileDeletion,
    OverlayMediaMaintenanceService mediaMaintenance
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
                .OverlayMediaAssets.Include(value => value.Document)
                .Where(value =>
                    value.HostId == actor.HostId
                    && assetIds.Contains(value.PublicId)
                    && value.Document.State == OverlayMediaDocumentState.Available
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
                if (layer.MediaKind != OverlayMediaTypes.Kind(asset.Document.ContentType))
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
                _ = db.OverlayCues.Add(cue);
                _ = await db.SaveChangesAsync(cancellationToken);
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
                _ = db.OverlayCueMediaAssetReferences.Add(
                    new OverlayCueMediaAssetReference
                    {
                        CueId = cue.Id,
                        AssetId = asset.Id,
                        HostId = actor.HostId,
                    }
                );
            }
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _ = await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
            return Success(ToView(cue));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Reject<OverlayCueView>(new OverlayCueRejection.Conflict());
        }
        finally
        {
            _ = gate.Release();
        }
    }
}

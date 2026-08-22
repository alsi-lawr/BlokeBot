using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService
{
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
        _ = await events.PublishAsync(AppEventKind.OverlaysChanged, cancellationToken);
        return Success(cueId);
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

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static OverlayCueResult<T>.Succeeded Success<T>(T value) => new(value);

    private static OverlayCueResult<T>.Rejected Reject<T>(OverlayCueRejection reason) =>
        new(reason);
}

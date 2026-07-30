using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

public sealed class OverlayInstanceService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IModeratorAuthorityService moderatorAuthority,
    IOverlayAccessKeyGenerator accessKeys,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    ILogger<OverlayInstanceService> logger
)
{
    private const int _eventSchemaVersion = 1;
    private const int _maximumNameLength = 128;
    private const int _mutationGateCount = 64;
    private static readonly SemaphoreSlim[] _mutationGates = CreateMutationGates();

    public async Task<OverlayInstanceResult<IReadOnlyList<OverlayInstanceView>>> ListAsync(
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        var authorization = await AuthorizeAsync(session, "list", ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<IReadOnlyList<OverlayInstanceView>>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var overlays = await db
            .OverlayInstances.AsNoTracking()
            .Where(overlay => overlay.HostId == actor.HostId)
            .OrderBy(overlay => overlay.Name)
            .ThenBy(overlay => overlay.PublicId)
            .ToArrayAsync(ct);
        return Succeeded<IReadOnlyList<OverlayInstanceView>>(overlays.Select(ToView).ToArray());
    }

    public async Task<OverlayInstanceResult<OverlayInstanceView>> GetAsync(
        AuthenticatedSession session,
        Guid overlayId,
        CancellationToken ct
    )
    {
        if (overlayId == Guid.Empty)
        {
            return Rejected<OverlayInstanceView>(
                new OverlayInstanceRejection.Invalid("An overlay ID is required.")
            );
        }

        var authorization = await AuthorizeAsync(session, "get", ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<OverlayInstanceView>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var overlay = await db
            .OverlayInstances.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == actor.HostId && value.PublicId == overlayId,
                ct
            );
        return overlay is null
            ? Rejected<OverlayInstanceView>(new OverlayInstanceRejection.NotFound())
            : Succeeded(ToView(overlay));
    }

    public async Task<OverlayInstanceResult<OverlayInstanceCreation>> CreateAsync(
        AuthenticatedSession session,
        CreateOverlayInstanceCommand command,
        CancellationToken ct
    )
    {
        var validation = ValidateCreate(command);
        if (validation is not null)
        {
            return Rejected<OverlayInstanceCreation>(validation);
        }

        var authorization = await AuthorizeAsync(session, "create", ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<OverlayInstanceCreation>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;
        var accessKey = GenerateAccessKey();
        var now = Now();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.NewGuid(),
            HostId = actor.HostId,
            Name = command.Name.Trim(),
            Type = command.Type,
            IsEnabled = true,
            ConfigurationJson = command.Configuration.ToPersistenceJson(),
            AccessKeyDigest = OverlayAccessKeyDigest.Compute(accessKey),
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Hosts.AnyAsync(host => host.Id == actor.HostId, ct))
        {
            return Rejected<OverlayInstanceCreation>(new OverlayInstanceRejection.NotFound());
        }

        db.OverlayInstances.Add(overlay);
        db.OverlayInstanceEvents.Add(
            DomainEvent(actor, overlay, OverlayInstanceEventKind.Created, now)
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await NotifyAsync(
            actor,
            overlay.PublicId,
            OverlayInstanceEventKind.Created,
            overlay.Revision,
            ct
        );
        return Succeeded(
            new OverlayInstanceCreation(ToView(overlay), new OverlayPrivateAccess(accessKey))
        );
    }

    public Task<OverlayInstanceResult<OverlayInstanceView>> RenameAsync(
        AuthenticatedSession session,
        RenameOverlayInstanceCommand command,
        CancellationToken ct
    )
    {
        var name = command.Name.Trim();
        if (!ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision))
        {
            return Task.FromResult(
                Rejected<OverlayInstanceView>(
                    new OverlayInstanceRejection.Invalid(
                        "An overlay ID and positive expected revision are required."
                    )
                )
            );
        }
        if (name.Length is < 1 or > _maximumNameLength)
        {
            return Task.FromResult(
                Rejected<OverlayInstanceView>(
                    new OverlayInstanceRejection.Invalid(
                        "The overlay name must be from 1 to 128 characters."
                    )
                )
            );
        }

        return UpdateAsync(
            session,
            command.OverlayId,
            command.ExpectedRevision,
            OverlayInstanceEventKind.Renamed,
            (query, now) =>
                query.ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(value => value.Name, name)
                            .SetProperty(value => value.UpdatedAtUtc, now)
                            .SetProperty(value => value.Revision, value => value.Revision + 1),
                    ct
                ),
            ct
        );
    }

    public async Task<OverlayInstanceResult<OverlayInstanceView>> ConfigureAsync(
        AuthenticatedSession session,
        ConfigureOverlayInstanceCommand command,
        CancellationToken ct
    )
    {
        if (
            !ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision)
            || command.Configuration is null
        )
        {
            return Rejected<OverlayInstanceView>(
                new OverlayInstanceRejection.Invalid(
                    "An overlay ID, positive expected revision, and configuration are required."
                )
            );
        }

        var authorization = await AuthorizeAsync(session, "configured", ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<OverlayInstanceView>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;
        var gate = MutationGate(command.OverlayId);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await LoadForMutationAsync(
                db,
                actor.HostId,
                command.OverlayId,
                command.ExpectedRevision,
                ct
            );
            if (existing is MutationTarget.Rejected targetRejected)
            {
                return Rejected<OverlayInstanceView>(targetRejected.Reason);
            }
            var overlay = ((MutationTarget.Found)existing).Overlay;
            if (overlay.Type != command.Configuration.Type)
            {
                return Rejected<OverlayInstanceView>(
                    new OverlayInstanceRejection.Invalid(
                        "The configuration type must match the overlay type."
                    )
                );
            }

            var now = Now();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var updated = await db
                .OverlayInstances.Where(value =>
                    value.HostId == actor.HostId
                    && value.PublicId == command.OverlayId
                    && value.Revision == command.ExpectedRevision.Value
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                value => value.ConfigurationJson,
                                command.Configuration.ToPersistenceJson()
                            )
                            .SetProperty(value => value.UpdatedAtUtc, now)
                            .SetProperty(value => value.Revision, value => value.Revision + 1),
                    ct
                );
            if (updated != 1)
            {
                return Rejected<OverlayInstanceView>(new OverlayInstanceRejection.Conflict());
            }

            var changed = overlay;
            changed.ConfigurationJson = command.Configuration.ToPersistenceJson();
            changed.UpdatedAtUtc = now;
            changed.Revision++;
            db.OverlayInstanceEvents.Add(
                DomainEvent(actor, changed, OverlayInstanceEventKind.Configured, now)
            );
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await NotifyAsync(
                actor,
                changed.PublicId,
                OverlayInstanceEventKind.Configured,
                changed.Revision,
                ct
            );
            return Succeeded(ToView(changed));
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<OverlayInstanceResult<OverlayInstanceView>> EnableAsync(
        AuthenticatedSession session,
        ChangeOverlayInstanceAvailabilityCommand command,
        CancellationToken ct
    )
    {
        return SetEnabledAsync(session, command, true, ct);
    }

    public Task<OverlayInstanceResult<OverlayInstanceView>> DisableAsync(
        AuthenticatedSession session,
        ChangeOverlayInstanceAvailabilityCommand command,
        CancellationToken ct
    )
    {
        return SetEnabledAsync(session, command, false, ct);
    }

    public async Task<OverlayInstanceResult<OverlayInstanceKeyRotation>> RotateKeyAsync(
        AuthenticatedSession session,
        RotateOverlayInstanceKeyCommand command,
        CancellationToken ct
    )
    {
        if (!ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision))
        {
            return Rejected<OverlayInstanceKeyRotation>(
                new OverlayInstanceRejection.Invalid(
                    "An overlay ID and positive expected revision are required."
                )
            );
        }

        var authorization = await AuthorizeAsync(session, "key-rotated", ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<OverlayInstanceKeyRotation>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;
        var gate = MutationGate(command.OverlayId);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await LoadForMutationAsync(
                db,
                actor.HostId,
                command.OverlayId,
                command.ExpectedRevision,
                ct
            );
            if (existing is MutationTarget.Rejected targetRejected)
            {
                return Rejected<OverlayInstanceKeyRotation>(targetRejected.Reason);
            }
            var overlay = ((MutationTarget.Found)existing).Overlay;
            var accessKey = GenerateAccessKey();
            var digest = OverlayAccessKeyDigest.Compute(accessKey);
            var now = Now();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var updated = await db
                .OverlayInstances.Where(value =>
                    value.HostId == actor.HostId
                    && value.PublicId == command.OverlayId
                    && value.Revision == command.ExpectedRevision.Value
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(value => value.AccessKeyDigest, digest)
                            .SetProperty(value => value.KeyVersion, value => value.KeyVersion + 1)
                            .SetProperty(value => value.UpdatedAtUtc, now)
                            .SetProperty(value => value.Revision, value => value.Revision + 1),
                    ct
                );
            if (updated != 1)
            {
                return Rejected<OverlayInstanceKeyRotation>(
                    new OverlayInstanceRejection.Conflict()
                );
            }

            overlay.AccessKeyDigest = digest;
            overlay.KeyVersion++;
            overlay.UpdatedAtUtc = now;
            overlay.Revision++;
            db.OverlayInstanceEvents.Add(
                DomainEvent(actor, overlay, OverlayInstanceEventKind.KeyRotated, now)
            );
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await NotifyAsync(
                actor,
                overlay.PublicId,
                OverlayInstanceEventKind.KeyRotated,
                overlay.Revision,
                ct
            );
            return Succeeded(
                new OverlayInstanceKeyRotation(ToView(overlay), new OverlayPrivateAccess(accessKey))
            );
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OverlayInstanceResult<Guid>> DeleteAsync(
        AuthenticatedSession session,
        DeleteOverlayInstanceCommand command,
        CancellationToken ct
    )
    {
        if (!ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision))
        {
            return Rejected<Guid>(
                new OverlayInstanceRejection.Invalid(
                    "An overlay ID and positive expected revision are required."
                )
            );
        }

        var authorization = await AuthorizeAsync(session, "deleted", ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<Guid>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;
        var gate = MutationGate(command.OverlayId);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await LoadForMutationAsync(
                db,
                actor.HostId,
                command.OverlayId,
                command.ExpectedRevision,
                ct
            );
            if (existing is MutationTarget.Rejected targetRejected)
            {
                return Rejected<Guid>(targetRejected.Reason);
            }
            var overlay = ((MutationTarget.Found)existing).Overlay;
            var now = Now();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var deleted = await db
                .OverlayInstances.Where(value =>
                    value.HostId == actor.HostId
                    && value.PublicId == command.OverlayId
                    && value.Revision == command.ExpectedRevision.Value
                )
                .ExecuteDeleteAsync(ct);
            if (deleted != 1)
            {
                return Rejected<Guid>(new OverlayInstanceRejection.Conflict());
            }

            overlay.Revision++;
            db.OverlayInstanceEvents.Add(
                DomainEvent(actor, overlay, OverlayInstanceEventKind.Deleted, now)
            );
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await NotifyAsync(
                actor,
                overlay.PublicId,
                OverlayInstanceEventKind.Deleted,
                overlay.Revision,
                ct
            );
            return Succeeded(command.OverlayId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<OverlayInstanceResult<OverlayInstanceView>> SetEnabledAsync(
        AuthenticatedSession session,
        ChangeOverlayInstanceAvailabilityCommand command,
        bool enabled,
        CancellationToken ct
    )
    {
        if (!ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision))
        {
            return Rejected<OverlayInstanceView>(
                new OverlayInstanceRejection.Invalid(
                    "An overlay ID and positive expected revision are required."
                )
            );
        }

        var kind = enabled ? OverlayInstanceEventKind.Enabled : OverlayInstanceEventKind.Disabled;
        return await UpdateAsync(
            session,
            command.OverlayId,
            command.ExpectedRevision,
            kind,
            (query, now) =>
                query.ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(value => value.IsEnabled, enabled)
                            .SetProperty(value => value.UpdatedAtUtc, now)
                            .SetProperty(value => value.Revision, value => value.Revision + 1),
                    ct
                ),
            ct
        );
    }

    private async Task<OverlayInstanceResult<OverlayInstanceView>> UpdateAsync(
        AuthenticatedSession session,
        Guid overlayId,
        OverlayRevision expectedRevision,
        OverlayInstanceEventKind kind,
        Func<IQueryable<OverlayInstance>, DateTime, Task<int>> update,
        CancellationToken ct
    )
    {
        var authorization = await AuthorizeAsync(session, PersistedEventName(kind), ct);
        if (authorization is AuthorizationDecision.Rejected rejected)
        {
            return Rejected<OverlayInstanceView>(rejected.Reason);
        }
        var actor = ((AuthorizationDecision.Granted)authorization).Actor;
        var gate = MutationGate(overlayId);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await LoadForMutationAsync(
                db,
                actor.HostId,
                overlayId,
                expectedRevision,
                ct
            );
            if (existing is MutationTarget.Rejected targetRejected)
            {
                return Rejected<OverlayInstanceView>(targetRejected.Reason);
            }
            var overlay = ((MutationTarget.Found)existing).Overlay;
            var now = Now();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var query = db.OverlayInstances.Where(value =>
                value.HostId == actor.HostId
                && value.PublicId == overlayId
                && value.Revision == expectedRevision.Value
            );
            if (await update(query, now) != 1)
            {
                return Rejected<OverlayInstanceView>(new OverlayInstanceRejection.Conflict());
            }

            ApplyUpdateToSnapshot(overlay, kind, now);
            db.OverlayInstanceEvents.Add(DomainEvent(actor, overlay, kind, now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await NotifyAsync(actor, overlay.PublicId, kind, overlay.Revision, ct);
            var changed = await db
                .OverlayInstances.AsNoTracking()
                .SingleAsync(
                    value => value.HostId == actor.HostId && value.PublicId == overlayId,
                    ct
                );
            return Succeeded(ToView(changed));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AuthorizationDecision> AuthorizeAsync(
        AuthenticatedSession session,
        string operation,
        CancellationToken ct
    )
    {
        var selectedHost = SelectedHost(session);
        if (
            !session.IsAuthenticated
            || session.IsBotAccount
            || string.IsNullOrWhiteSpace(session.UserId)
            || selectedHost is null
            || selectedHost.Role == AuthRole.Bot
        )
        {
            LogDenied(operation, session.UserId, selectedHost?.Id, "unauthorized");
            return new AuthorizationDecision.Rejected(new OverlayInstanceRejection.Unauthorized());
        }

        var actor = new AuthorizedActor(selectedHost.Id, session.UserId, session.Login.Trim());
        if (selectedHost.Role is AuthRole.Streamer or AuthRole.Admin)
        {
            return new AuthorizationDecision.Granted(actor);
        }
        if (selectedHost.Role != AuthRole.Moderator)
        {
            LogDenied(operation, session.UserId, selectedHost.Id, "unauthorized");
            return new AuthorizationDecision.Rejected(new OverlayInstanceRejection.Unauthorized());
        }

        var authority = await moderatorAuthority.AuthorizeAsync(session, selectedHost.Id, ct);
        return authority.Match<AuthorizationDecision>(
            _ => new AuthorizationDecision.Granted(actor),
            _ =>
            {
                LogDenied(operation, session.UserId, selectedHost.Id, "revoked");
                return new AuthorizationDecision.Rejected(
                    new OverlayInstanceRejection.Unauthorized()
                );
            },
            _ =>
            {
                LogDenied(operation, session.UserId, selectedHost.Id, "host-mismatch");
                return new AuthorizationDecision.Rejected(
                    new OverlayInstanceRejection.Unauthorized()
                );
            },
            _ =>
            {
                LogDenied(operation, session.UserId, selectedHost.Id, "unavailable");
                return new AuthorizationDecision.Rejected(
                    new OverlayInstanceRejection.AuthorityUnavailable()
                );
            }
        );
    }

    private async Task<MutationTarget> LoadForMutationAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid overlayId,
        OverlayRevision expectedRevision,
        CancellationToken ct
    )
    {
        var overlay = await db
            .OverlayInstances.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.PublicId == overlayId,
                ct
            );
        if (overlay is null)
        {
            return new MutationTarget.Rejected(new OverlayInstanceRejection.NotFound());
        }
        return overlay.Revision == expectedRevision.Value
            ? new MutationTarget.Found(overlay)
            : new MutationTarget.Rejected(new OverlayInstanceRejection.Conflict());
    }

    private async Task NotifyAsync(
        AuthorizedActor actor,
        Guid overlayId,
        OverlayInstanceEventKind kind,
        long revision,
        CancellationToken ct
    )
    {
        logger.LogInformation(
            "Overlay instance {Operation} for host {HostId} and overlay {OverlayId} at revision {Revision} by actor {ActorUserId}.",
            PersistedEventName(kind),
            actor.HostId,
            overlayId,
            revision,
            actor.UserId
        );
        await events.PublishAsync(AppEventKind.OverlaysChanged, ct);
    }

    private void LogDenied(string operation, string actorUserId, int? hostId, string reason)
    {
        logger.LogWarning(
            "Overlay instance operation {Operation} was denied for actor {ActorUserId} on selected host {HostId}: {Reason}.",
            operation,
            actorUserId,
            hostId,
            reason
        );
    }

    private static OverlayInstanceDomainEvent DomainEvent(
        AuthorizedActor actor,
        OverlayInstance overlay,
        OverlayInstanceEventKind kind,
        DateTime occurredAtUtc
    )
    {
        return new()
        {
            HostId = actor.HostId,
            OverlayPublicId = overlay.PublicId,
            SchemaVersion = _eventSchemaVersion,
            Kind = kind,
            ActorUserId = actor.UserId,
            ActorLogin = actor.Login,
            OverlayRevision = overlay.Revision,
            KeyVersion = overlay.KeyVersion,
            OccurredAtUtc = occurredAtUtc,
        };
    }

    private static void ApplyUpdateToSnapshot(
        OverlayInstance overlay,
        OverlayInstanceEventKind kind,
        DateTime updatedAtUtc
    )
    {
        if (kind == OverlayInstanceEventKind.Enabled)
        {
            overlay.IsEnabled = true;
        }
        else if (kind == OverlayInstanceEventKind.Disabled)
        {
            overlay.IsEnabled = false;
        }

        overlay.UpdatedAtUtc = updatedAtUtc;
        overlay.Revision++;
    }

    private static OverlayInstanceView ToView(OverlayInstance overlay)
    {
        return new(
            overlay.PublicId,
            overlay.Name,
            overlay.Type,
            overlay.IsEnabled,
            OverlayConfiguration.FromPersistence(overlay.Type, overlay.ConfigurationJson),
            new DateTimeOffset(DateTime.SpecifyKind(overlay.CreatedAtUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(overlay.UpdatedAtUtc, DateTimeKind.Utc)),
            new OverlayRevision(overlay.Revision)
        );
    }

    private static OverlayInstanceRejection? ValidateCreate(CreateOverlayInstanceCommand command)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > _maximumNameLength)
        {
            return new OverlayInstanceRejection.Invalid(
                "The overlay name must be from 1 to 128 characters."
            );
        }
        if (!Enum.IsDefined(command.Type))
        {
            return new OverlayInstanceRejection.Invalid("The overlay type is not supported.");
        }
        if (command.Configuration is null || command.Configuration.Type != command.Type)
        {
            return new OverlayInstanceRejection.Invalid(
                "The configuration type must match the overlay type."
            );
        }
        return null;
    }

    private string GenerateAccessKey()
    {
        var accessKey = accessKeys.Generate();
        return OverlayAccessKeyDigest.HasCanonicalShape(accessKey)
            ? accessKey
            : throw new InvalidOperationException(
                "The overlay access-key generator returned a non-canonical key."
            );
    }

    private static BotHostChoice? SelectedHost(AuthenticatedSession session)
    {
        return session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
    }

    private static bool ValidOverlayIdAndRevision(Guid overlayId, OverlayRevision expectedRevision)
    {
        return overlayId != Guid.Empty && expectedRevision.Value > 0;
    }

    private static SemaphoreSlim MutationGate(Guid overlayId)
    {
        return _mutationGates[(int)((uint)overlayId.GetHashCode() % _mutationGates.Length)];
    }

    private static SemaphoreSlim[] CreateMutationGates()
    {
        return Enumerable
            .Range(0, _mutationGateCount)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();
    }

    private static string PersistedEventName(OverlayInstanceEventKind kind)
    {
        return PersistedEnumTokens<OverlayInstanceEventKind>.Format(kind);
    }

    private DateTime Now()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }

    private static OverlayInstanceResult<T> Succeeded<T>(T value)
    {
        return new OverlayInstanceResult<T>.Succeeded(value);
    }

    private static OverlayInstanceResult<T> Rejected<T>(OverlayInstanceRejection rejection)
    {
        return new OverlayInstanceResult<T>.Rejected(rejection);
    }

    private sealed record AuthorizedActor(int HostId, string UserId, string Login);

    private abstract record AuthorizationDecision
    {
        private AuthorizationDecision() { }

        internal sealed record Granted(AuthorizedActor Actor) : AuthorizationDecision;

        internal sealed record Rejected(OverlayInstanceRejection Reason) : AuthorizationDecision;
    }

    private abstract record MutationTarget
    {
        private MutationTarget() { }

        internal sealed record Found(OverlayInstance Overlay) : MutationTarget;

        internal sealed record Rejected(OverlayInstanceRejection Reason) : MutationTarget;
    }
}

using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

public enum FullOverlayCollection
{
    Active,
    Archived,
}

internal sealed partial class FullOverlayService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    OverlayManagementAuthority authority,
    IOverlayAccessKeyGenerator accessKeys,
    IFullOverlayPublicationAdmission admission,
    EventBus<AppEventKind> events,
    TimeProvider clock
)
{
    public async Task<FullOverlayResult<IReadOnlyList<FullOverlayView>>> ListAsync(
        AuthenticatedSession session,
        FullOverlayCollection collection,
        CancellationToken ct
    )
    {
        var authorization = await AuthorizeAsync(session, ct);
        if (authorization is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<IReadOnlyList<FullOverlayView>>(rejected.Reason);
        }
        var actor = ((FullOverlayResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var archived = collection == FullOverlayCollection.Archived;
        var rows = await db
            .FullOverlays.AsNoTracking()
            .Where(row => row.HostId == actor.HostId && row.IsArchived == archived)
            .OrderBy(row => row.Name)
            .ThenBy(row => row.PublicId)
            .ToArrayAsync(ct);
        return Success<IReadOnlyList<FullOverlayView>>(rows.Select(ToView).ToArray());
    }

    public async Task<FullOverlayResult<FullOverlayView>> GetAsync(
        AuthenticatedSession session,
        Guid overlayId,
        CancellationToken ct
    )
    {
        var authorization = await AuthorizeAsync(session, ct);
        if (authorization is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<FullOverlayView>(rejected.Reason);
        }
        var actor = ((FullOverlayResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db
            .FullOverlays.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.HostId == actor.HostId && row.PublicId == overlayId,
                ct
            );
        return row is null
            ? Reject<FullOverlayView>(FullOverlayRejectionKind.NotFound)
            : Success(ToView(row));
    }

    public async Task<FullOverlayResult<FullOverlayCreation>> CreateAsync(
        AuthenticatedSession session,
        CreateFullOverlayCommand command,
        CancellationToken ct
    )
    {
        if (!ValidName(command.Name) || !FullOverlayDocuments.HasCoherentIdentity(command.Document))
        {
            return Reject<FullOverlayCreation>(FullOverlayRejectionKind.Invalid);
        }
        var authorization = await AuthorizeAsync(session, ct);
        if (authorization is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<FullOverlayCreation>(rejected.Reason);
        }
        var actor = ((FullOverlayResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await LockEnabledHostAsync(db, actor.HostId, ct))
        {
            return Reject<FullOverlayCreation>(FullOverlayRejectionKind.FeatureDisabled);
        }
        var created = NewOverlay(actor.HostId, command.Name, command.Document);
        _ = db.FullOverlays.Add(created.Row);
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await NotifyAsync();
        return Success(new FullOverlayCreation(ToView(created.Row), created.Access));
    }

    public Task<FullOverlayResult<FullOverlayView>> SaveAsync(
        AuthenticatedSession session,
        SaveFullOverlayCommand command,
        CancellationToken ct
    )
    {
        if (!ValidSave(command))
        {
            return Task.FromResult(Reject<FullOverlayView>(FullOverlayRejectionKind.Invalid));
        }
        var json = FullOverlayDocuments.Serialize(command.Document);
        return MutateAsync(
            session,
            new(command.OverlayId, command.ExpectedRevision),
            async (db, _, row) =>
            {
                row.Name = command.Name.Trim();
                row.DraftDocumentJson = json;
                return await CommitAsync(db, row, ToView, ct);
            },
            ct
        );
    }

    private CreatedOverlay NewOverlay(int hostId, string name, FullOverlayDocument document)
    {
        var id = Guid.NewGuid();
        var generatedKey = accessKeys.Generate();
        var key = OverlayAccessKeyDigest.HasCanonicalShape(generatedKey)
            ? generatedKey
            : throw new InvalidOperationException(
                "The overlay access-key generator returned a non-canonical key."
            );
        return new(
            new FullOverlay
            {
                PublicId = id,
                HostId = hostId,
                Name = name.Trim(),
                DraftDocumentJson = FullOverlayDocuments.Serialize(document with { Id = id }),
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(key),
                Revision = 1,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime,
            },
            new FullOverlayPrivateAccess(key)
        );
    }

    private static FullOverlayView ToView(FullOverlay row) =>
        new(
            row.PublicId,
            row.Name,
            FullOverlayDocuments.Deserialize(row.DraftDocumentJson),
            row.IsArchived,
            new(row.Revision),
            row.PublishedVersion is { } version ? new(version) : null,
            Utc(row.CreatedAtUtc),
            Utc(row.UpdatedAtUtc)
        );

    private static DateTimeOffset Utc(DateTime time) =>
        new(DateTime.SpecifyKind(time, DateTimeKind.Utc));

    private static bool ValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 128;

    private static bool ValidSave(SaveFullOverlayCommand command) =>
        ValidName(command.Name)
        && FullOverlayDocuments.HasCoherentIdentity(command.Document)
        && command.Document.Id == command.OverlayId;

    private Task NotifyAsync() =>
        events.PublishAsync(AppEventKind.OverlaysChanged, CancellationToken.None).AsTask();

    private static FullOverlayResult<T> Success<T>(T value) =>
        new FullOverlayResult<T>.Succeeded(value);

    private static FullOverlayResult<T> Reject<T>(FullOverlayRejectionKind kind) =>
        Reject<T>(new FullOverlayRejection(kind, []));

    private static FullOverlayResult<T> Reject<T>(FullOverlayRejection reason) =>
        new FullOverlayResult<T>.Rejected(reason);

    private sealed record CreatedOverlay(FullOverlay Row, FullOverlayPrivateAccess Access);
}

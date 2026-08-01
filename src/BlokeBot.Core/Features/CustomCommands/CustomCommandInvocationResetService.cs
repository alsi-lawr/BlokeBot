using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandInvocationResetService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ICustomCommandViewerResolver viewers,
    EventBus<AppEventKind> events,
    TimeProvider clock
)
{
    public async Task<CustomCommandInvocationResetOutcome> ResetViewerAsync(
        int hostId,
        int commandId,
        CustomCommandResetActor actor,
        string viewerLogin,
        CancellationToken ct
    )
    {
        var resolution = await viewers.ResolveAsync(viewerLogin, ct);
        if (resolution is CustomCommandViewerResolution.NotFound)
        {
            return new CustomCommandInvocationResetOutcome.ViewerNotFound();
        }

        var viewer = ((CustomCommandViewerResolution.Found)resolution).Viewer;
        return await ResetAsync(hostId, commandId, actor, new ResetTarget.OneViewer(viewer), ct);
    }

    public Task<CustomCommandInvocationResetOutcome> ResetAllViewersAsync(
        int hostId,
        int commandId,
        CustomCommandResetActor actor,
        CancellationToken ct
    ) => ResetAsync(hostId, commandId, actor, new ResetTarget.AllViewers(), ct);

    private async Task<CustomCommandInvocationResetOutcome> ResetAsync(
        int hostId,
        int commandId,
        CustomCommandResetActor actor,
        ResetTarget target,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var command = await db
            .CustomCommands.AsNoTracking()
            .Where(stored => stored.HostId == hostId && stored.Id == commandId)
            .Select(stored => new { stored.Id, stored.Name })
            .SingleOrDefaultAsync(ct);
        if (command is null)
        {
            return new CustomCommandInvocationResetOutcome.CommandNotFound();
        }

        var claims = db.CustomCommandInvocationClaims.Where(claim =>
            claim.HostId == hostId
            && claim.CustomCommandId == commandId
            && claim.TwitchStreamId == null
        );
        if (target is ResetTarget.OneViewer oneViewer)
        {
            claims = claims.Where(claim => claim.TwitchUserId == oneViewer.Viewer.TwitchUserId);
        }

        var affected = await claims.ExecuteDeleteAsync(ct);
        db.CustomCommandInvocationResetAudits.Add(
            new CustomCommandInvocationResetAudit
            {
                HostId = hostId,
                CustomCommandId = command.Id,
                CommandName = command.Name,
                ActorTwitchUserId = actor.TwitchUserId,
                ActorLogin = Login.Normalize(actor.Login),
                Scope =
                    target is ResetTarget.OneViewer
                        ? CustomCommandInvocationResetScope.OneViewer
                        : CustomCommandInvocationResetScope.AllViewers,
                TargetTwitchUserId = target is ResetTarget.OneViewer viewer
                    ? viewer.Viewer.TwitchUserId
                    : null,
                TargetLogin = target is ResetTarget.OneViewer targetViewer
                    ? targetViewer.Viewer.Login
                    : null,
                AffectedClaimCount = affected,
                ResetAtUtc = clock.GetUtcNow().UtcDateTime,
            }
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(AppEventKind.CustomCommandsChanged, ct);
        return new CustomCommandInvocationResetOutcome.Reset(affected);
    }

    private abstract record ResetTarget
    {
        private ResetTarget() { }

        public sealed record OneViewer(CustomCommandViewer Viewer) : ResetTarget;

        public sealed record AllViewers : ResetTarget;
    }
}

public sealed record CustomCommandResetActor(string TwitchUserId, string Login);

public abstract record CustomCommandInvocationResetOutcome
{
    private CustomCommandInvocationResetOutcome() { }

    public sealed record Reset(int AffectedClaimCount) : CustomCommandInvocationResetOutcome;

    public sealed record ViewerNotFound : CustomCommandInvocationResetOutcome;

    public sealed record CommandNotFound : CustomCommandInvocationResetOutcome;
}

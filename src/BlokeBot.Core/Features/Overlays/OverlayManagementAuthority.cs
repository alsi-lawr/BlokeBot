using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class OverlayManagementAuthority(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IModeratorAuthorityService moderatorAuthority
)
{
    internal async Task<OverlayManagementAuthorization> AuthorizeAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken
    )
    {
        var selected = await AuthorizedHostAsync(session, cancellationToken);
        if (selected is null)
        {
            return Denied();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await ReadHostAsync(session, selected, db, cancellationToken);
    }

    internal async Task<OverlayManagementAuthorization> AuthorizeAsync(
        AuthenticatedSession session,
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        var selected = await AuthorizedHostAsync(session, cancellationToken);
        return selected is null
            ? Denied()
            : await ReadHostAsync(session, selected, db, cancellationToken);
    }

    private async Task<BotHostChoice?> AuthorizedHostAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken
    )
    {
        var selected = session.State.Match<BotHostChoice?>(
            _ => null,
            value => value.Selection.Current,
            _ => null
        );
        if (
            !session.IsAuthenticated
            || session.IsBotAccount
            || string.IsNullOrWhiteSpace(session.UserId)
            || selected is null
            || selected.Role == AuthRole.Bot
        )
        {
            return null;
        }
        if (selected.Role is AuthRole.Streamer or AuthRole.Admin)
        {
            return selected;
        }
        if (selected.Role != AuthRole.Moderator)
        {
            return null;
        }
        var authority = await moderatorAuthority.AuthorizeAsync(
            session,
            selected.Id,
            cancellationToken
        );
        return authority.Match(_ => true, _ => false, _ => false, _ => false) ? selected : null;
    }

    private static OverlayManagementAuthorization Denied() =>
        new OverlayManagementAuthorization.Rejected(OverlayManagementRejection.Unauthorized);

    private static async Task<OverlayManagementAuthorization> ReadHostAsync(
        AuthenticatedSession session,
        BotHostChoice selectedHost,
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        var enabled = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == selectedHost.Id)
            .Select(host => (HostFeatureFlags?)host.EnabledFeatures)
            .SingleOrDefaultAsync(cancellationToken);
        return enabled switch
        {
            null => new OverlayManagementAuthorization.Rejected(OverlayManagementRejection.Missing),
            { } value when (value & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays =>
                new OverlayManagementAuthorization.Rejected(
                    OverlayManagementRejection.ParentDisabled
                ),
            _ => new OverlayManagementAuthorization.Granted(
                new OverlayManagementActor(selectedHost.Id, session.UserId, session.Login.Trim())
            ),
        };
    }
}

internal sealed record OverlayManagementActor(int HostId, string UserId, string Login);

internal enum OverlayManagementRejection
{
    Unauthorized,
    Missing,
    ParentDisabled,
}

internal abstract record OverlayManagementAuthorization
{
    private OverlayManagementAuthorization() { }

    internal sealed record Granted(OverlayManagementActor Actor) : OverlayManagementAuthorization;

    internal sealed record Rejected(OverlayManagementRejection Reason)
        : OverlayManagementAuthorization;
}

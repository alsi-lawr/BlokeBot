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
        var selectedHost = session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        if (
            !session.IsAuthenticated
            || session.IsBotAccount
            || string.IsNullOrWhiteSpace(session.UserId)
            || selectedHost is null
            || selectedHost.Role == AuthRole.Bot
        )
        {
            return new OverlayManagementAuthorization.Rejected(
                OverlayManagementRejection.Unauthorized
            );
        }

        if (selectedHost.Role is not (AuthRole.Streamer or AuthRole.Admin))
        {
            if (selectedHost.Role != AuthRole.Moderator)
            {
                return new OverlayManagementAuthorization.Rejected(
                    OverlayManagementRejection.Unauthorized
                );
            }
            var authority = await moderatorAuthority.AuthorizeAsync(
                session,
                selectedHost.Id,
                cancellationToken
            );
            var granted = authority.Match(_ => true, _ => false, _ => false, _ => false);
            if (!granted)
            {
                return new OverlayManagementAuthorization.Rejected(
                    OverlayManagementRejection.Unauthorized
                );
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
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

using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Auth.Moderation;

public interface IModeratorAuthorityService
{
    Task<ModeratorAuthorityOutcome> AuthorizeAsync(
        AuthenticatedSession session,
        int requestedHostId,
        CancellationToken ct
    );
}

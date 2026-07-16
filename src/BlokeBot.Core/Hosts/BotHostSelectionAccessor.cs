using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Hosts;

internal sealed class BotHostSelectionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public AuthSessionState Current => FromPrincipal(httpContextAccessor.HttpContext?.User);

    public static AuthSessionState FromPrincipal(ClaimsPrincipal? user)
    {
        return AuthenticatedSession.FromPrincipal(user).State;
    }
}

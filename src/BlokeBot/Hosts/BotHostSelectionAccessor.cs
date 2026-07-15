using System.Security.Claims;
using BlokeBot.Auth.Sessions;

namespace BlokeBot.Hosts;

internal sealed class BotHostSelectionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public AuthSessionState Current => FromPrincipal(httpContextAccessor.HttpContext?.User);

    public static AuthSessionState FromPrincipal(ClaimsPrincipal? user)
    {
        return AuthenticatedSession.FromPrincipal(user).State;
    }
}

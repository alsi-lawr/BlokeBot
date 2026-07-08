using System.Security.Claims;
using BlokeBot.Auth.Sessions;

namespace BlokeBot.Hosts;

internal sealed class BotHostSelectionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public BotHostSelection? Current => FromPrincipal(httpContextAccessor.HttpContext?.User);

    public static BotHostSelection? FromPrincipal(ClaimsPrincipal? user) =>
        AuthenticatedSession.FromPrincipal(user).HostSelection;
}

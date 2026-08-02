using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Auth.Sessions;

public sealed record BlokeBotPageContext(
    AuthenticatedSession Session,
    string ActorLogin,
    bool IsBotAccount
)
{
    public static BlokeBotPageContext Anonymous { get; } =
        new(AuthenticatedSession.Anonymous, string.Empty, false);
}

public sealed class BlokeBotPageContextAccessor
{
    public BlokeBotPageContext FromPrincipal(ClaimsPrincipal? user) =>
        FromSession(AuthenticatedSession.FromPrincipal(user));

    public async Task<BlokeBotPageContext> FromAsync(
        Task<AuthenticationState> authenticationStateTask
    )
    {
        var authState = await authenticationStateTask;
        return FromPrincipal(authState.User);
    }

    private static BlokeBotPageContext FromSession(AuthenticatedSession session) =>
        new BlokeBotPageContext(session, session.Login, session.IsBotAccount);
}

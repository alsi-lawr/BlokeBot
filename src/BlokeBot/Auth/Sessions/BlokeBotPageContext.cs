using System.Security.Claims;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Auth.Sessions;

public sealed record BlokeBotPageContext(
    AuthenticatedSession Session,
    string ActorLogin,
    bool IsBotAccount,
    BotHostSelection? HostSelection,
    BotHostChoice? SelectedHost
)
{
    public static BlokeBotPageContext Anonymous { get; } =
        new(AuthenticatedSession.Anonymous, string.Empty, false, null, null);

    public bool HasSelectedHost => SelectedHost is not null;
}

public sealed class BlokeBotPageContextAccessor
{
    public BlokeBotPageContext FromPrincipal(ClaimsPrincipal? user)
    {
        return FromSession(AuthenticatedSession.FromPrincipal(user));
    }

    public async Task<BlokeBotPageContext> FromAsync(
        Task<AuthenticationState> authenticationStateTask
    )
    {
        var authState = await authenticationStateTask;
        return FromPrincipal(authState.User);
    }

    private static BlokeBotPageContext FromSession(AuthenticatedSession session)
    {
        return new BlokeBotPageContext(
            session,
            session.Login,
            session.IsBotAccount,
            session.HostSelection,
            session.HostSelection?.Current
        );
    }
}

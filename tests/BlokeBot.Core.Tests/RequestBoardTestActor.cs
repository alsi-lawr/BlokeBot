using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.RequestBoards;

namespace BlokeBot.Core.Tests;

internal static class RequestBoardTestActor
{
    public static RequestActor ForLogin(string login) => Identified("fixture-" + login, login);

    public static RequestActor Identified(string userId, string login) =>
        RequestActor.FromSession(
            new AuthenticatedSession
            {
                IsAuthenticated = true,
                UserId = userId,
                Login = login,
            }
        )!;
}

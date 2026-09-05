using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Features.RequestBoards;

public sealed class RequestActor
{
    private RequestActor(string twitchUserId, string login)
    {
        TwitchUserId = twitchUserId;
        Login = login;
    }

    public string TwitchUserId { get; }
    public string Login { get; }

    public static RequestActor? FromSession(AuthenticatedSession session) =>
        session.IsAuthenticated ? Create(session.UserId, session.Login) : null;

    public static RequestActor? FromChatMessage(ChatMessage message) =>
        message.Tags.TryGetValue("user-id", out var userId) ? Create(userId, message.Login) : null;

    private static RequestActor? Create(string userId, string login)
    {
        var normalizedLogin = CommunityInput.NormalizeLogin(login);
        return
            string.IsNullOrWhiteSpace(userId)
            || userId.Length > 128
            || !CommunityInput.IsValidLogin(normalizedLogin)
            ? null
            : new RequestActor(userId, normalizedLogin);
    }
}

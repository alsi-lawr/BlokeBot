using BlokeBot.Auth.Sessions;

namespace BlokeBot.Auth.Web;

internal sealed record AuthResult(bool IsAuthorized, AuthenticatedUser? User, string? Error);

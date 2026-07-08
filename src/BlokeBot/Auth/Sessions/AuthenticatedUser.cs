using BlokeBot.Hosts;

namespace BlokeBot.Auth.Sessions;

internal sealed record AuthenticatedUser(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    IReadOnlyList<BotHostChoice> Hosts,
    bool CanCreateHost
);

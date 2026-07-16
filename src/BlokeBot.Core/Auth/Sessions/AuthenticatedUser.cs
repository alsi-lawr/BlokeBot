using BlokeBot.Core.Hosts;

namespace BlokeBot.Core.Auth.Sessions;

internal sealed record AuthenticatedUser(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    IReadOnlyList<BotHostChoice> Hosts,
    bool CanCreateHost
);

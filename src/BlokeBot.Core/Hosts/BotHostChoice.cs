using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Hosts;

public sealed record BotHostChoice(
    int Id,
    string Login,
    string DisplayName,
    AuthRole Role,
    string? ProfileImageUrl = null
);

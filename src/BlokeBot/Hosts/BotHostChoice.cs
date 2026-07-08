using BlokeBot.Auth.Sessions;

namespace BlokeBot.Hosts;

public sealed record BotHostChoice(
    int Id,
    string Login,
    string DisplayName,
    AuthRole Role,
    string? ProfileImageUrl = null
);

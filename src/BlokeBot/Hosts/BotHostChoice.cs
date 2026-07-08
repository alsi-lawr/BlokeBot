namespace BlokeBot.Hosts;

public sealed record BotHostChoice(
    int Id,
    string Login,
    string DisplayName,
    string Role,
    string? ProfileImageUrl = null
);

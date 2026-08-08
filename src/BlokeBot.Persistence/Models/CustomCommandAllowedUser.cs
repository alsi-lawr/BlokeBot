namespace BlokeBot.Persistence.Models;

public sealed class CustomCommandAllowedUser
{
    public int HostId { get; set; }

    public int CustomCommandId { get; set; }

    public string TwitchUserId { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public CustomCommand Command { get; set; } = null!;
}

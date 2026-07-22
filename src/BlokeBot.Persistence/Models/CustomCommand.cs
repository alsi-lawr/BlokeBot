namespace BlokeBot.Persistence.Models;

public sealed class CustomCommand
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool ModeratorOnly { get; set; }

    public int CooldownSeconds { get; set; }

    public CustomCommandCooldownScope CooldownScope { get; set; } =
        CustomCommandCooldownScope.Global;

    public CustomCommandInvocationLimit InvocationLimit { get; set; } =
        CustomCommandInvocationLimit.Unlimited;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public CustomCommandAction Action { get; set; } = null!;

    public List<CustomCommandAlias> Aliases { get; set; } = [];
}

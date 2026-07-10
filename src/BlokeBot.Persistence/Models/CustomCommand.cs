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

    public CustomCommandActionType ActionType { get; set; } = CustomCommandActionType.Message;

    public int MessageLibraryEntryId { get; set; }

    public int? CounterId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public CustomMessageLibraryEntry? MessageLibraryEntry { get; set; }

    public CustomCounter? Counter { get; set; }

    public List<CustomCommandAlias> Aliases { get; set; } = [];
}

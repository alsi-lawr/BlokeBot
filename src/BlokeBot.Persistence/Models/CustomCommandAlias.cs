namespace BlokeBot.Persistence.Models;

public sealed class CustomCommandAlias
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public int CustomCommandId { get; set; }

    public string Alias { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public CustomCommand? Command { get; set; }
}

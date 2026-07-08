namespace BlokeBot.Persistence.Models;

public sealed class CommandAlias
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
}

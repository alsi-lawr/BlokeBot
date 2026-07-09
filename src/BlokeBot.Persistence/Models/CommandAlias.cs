namespace BlokeBot.Persistence.Models;

public sealed class CommandAlias
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public int? GuessRoundProfileId { get; set; }
    public GuessRoundProfile? GuessRoundProfile { get; set; }
    public AppCommandKind Kind { get; set; }
    public string Alias { get; set; } = string.Empty;
}

namespace BlokeBot.Persistence.Models;

public sealed class GuessOption
{
    public int Id { get; set; }
    public int GuessRoundProfileId { get; set; }
    public GuessRoundProfile? GuessRoundProfile { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public string ReplyTarget { get; set; } = "chat";
}

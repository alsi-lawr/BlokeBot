namespace BlokeBot.Persistence.Models;

public sealed class GuessRoundProfile
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public long Revision { get; set; }
    public string WinningGuessPointReward { get; set; } = "0";
    public BotReplySettings? ReplySettings { get; set; }
    public List<CommandAlias> CommandAliases { get; set; } = [];
    public List<GuessOption> Options { get; set; } = [];
    public List<GuessRound> Rounds { get; set; } = [];
}

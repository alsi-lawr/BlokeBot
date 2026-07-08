namespace BlokeBot.Persistence.Models;

public sealed class BotReplySettings
{
    public int Id { get; set; }
    public int GuessRoundProfileId { get; set; }
    public GuessRoundProfile? GuessRoundProfile { get; set; }
    public string RoundStartedReply { get; set; } = string.Empty;
    public string RoundAlreadyOpenReply { get; set; } = string.Empty;
    public string NoOpenRoundReply { get; set; } = string.Empty;
    public string GuessingStoppedReply { get; set; } = string.Empty;
    public string GuessingAlreadyStoppedReply { get; set; } = string.Empty;
    public string GuessingClosedReply { get; set; } = string.Empty;
    public string InvalidGuessReply { get; set; } = string.Empty;
    public string GuessUsageReply { get; set; } = string.Empty;
    public string AvailableGuessesReply { get; set; } = string.Empty;
    public string WinUsageReply { get; set; } = string.Empty;
    public string ModeratorOnlyReply { get; set; } = string.Empty;
    public string WinnerReply { get; set; } = string.Empty;
    public string NoWinnersReply { get; set; } = string.Empty;
}

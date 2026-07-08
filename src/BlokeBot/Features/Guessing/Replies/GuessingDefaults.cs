using BlokeBot.Features.Guessing.Commands;

namespace BlokeBot.Features.Guessing.Replies;

public static class GuessingDefaults
{
    public static readonly IReadOnlyDictionary<GuessCommandKind, string[]> Aliases = new Dictionary<
        GuessCommandKind,
        string[]
    >
    {
        [GuessCommandKind.Start] = ["startguessing"],
        [GuessCommandKind.Stop] = ["stopguessing"],
        [GuessCommandKind.Win] = ["win"],
        [GuessCommandKind.Guess] = ["guess"],
        [GuessCommandKind.Guesses] = ["guesses"],
    };

    public static ReplySettingsEditor Replies() =>
        new()
        {
            RoundStartedReply =
                "{round} guessing is open. Vote with !guess <name>. Options: {options}.",
            RoundAlreadyOpenReply = "A guessing round is already open.",
            NoOpenRoundReply = "No guessing round is open.",
            GuessingStoppedReply = "Guessing is now closed.",
            GuessingAlreadyStoppedReply = "Guessing is already closed.",
            GuessingClosedReply = "Guessing is closed.",
            InvalidGuessReply = "{name} is not a valid guess.",
            GuessUsageReply = "Usage: !{command} <name>",
            AvailableGuessesReply = "Available guesses: {options}.",
            WinUsageReply = "Usage: !{command} <name>",
            ModeratorOnlyReply = "Only moderators can use that command.",
            WinnerReply = "{name} wins. Correct guesses: {winners}.",
            NoWinnersReply = "{name} wins. Nobody guessed correctly.",
        };
}

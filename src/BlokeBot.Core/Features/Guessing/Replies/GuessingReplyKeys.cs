namespace BlokeBot.Core.Features.Guessing.Replies;

public static class GuessingReplyKeys
{
    public const string RoundAlreadyOpen = "round_already_open";
    public const string NoOpenRound = "no_open_round";
    public const string GuessingAlreadyStopped = "guessing_already_stopped";
    public const string GuessingClosed = "guessing_closed";
    public const string InvalidGuess = "invalid_guess";
    public const string GuessUsage = "guess_usage";
    public const string AvailableGuesses = "available_guesses";
    public const string WinUsage = "win_usage";
    public const string ModeratorOnly = "moderator_only";

    public static readonly IReadOnlyList<string> WhisperableKeys =
    [
        RoundAlreadyOpen,
        NoOpenRound,
        GuessingAlreadyStopped,
        GuessingClosed,
        InvalidGuess,
        GuessUsage,
        AvailableGuesses,
        WinUsage,
        ModeratorOnly,
    ];
}

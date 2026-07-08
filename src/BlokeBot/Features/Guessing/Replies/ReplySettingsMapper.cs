using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Guessing.Replies;

internal static class ReplySettingsMapper
{
    public static BotReplySettings ToEntity(ReplySettingsEditor editor) =>
        new()
        {
            RoundStartedReply = editor.RoundStartedReply,
            RoundAlreadyOpenReply = editor.RoundAlreadyOpenReply,
            NoOpenRoundReply = editor.NoOpenRoundReply,
            GuessingStoppedReply = editor.GuessingStoppedReply,
            GuessingAlreadyStoppedReply = editor.GuessingAlreadyStoppedReply,
            GuessingClosedReply = editor.GuessingClosedReply,
            InvalidGuessReply = editor.InvalidGuessReply,
            GuessUsageReply = editor.GuessUsageReply,
            AvailableGuessesReply = editor.AvailableGuessesReply,
            WinUsageReply = editor.WinUsageReply,
            ModeratorOnlyReply = editor.ModeratorOnlyReply,
            WinnerReply = editor.WinnerReply,
            NoWinnersReply = editor.NoWinnersReply,
        };

    public static ReplySettingsEditor ToEditor(BotReplySettings settings) =>
        new()
        {
            RoundStartedReply = settings.RoundStartedReply,
            RoundAlreadyOpenReply = settings.RoundAlreadyOpenReply,
            NoOpenRoundReply = settings.NoOpenRoundReply,
            GuessingStoppedReply = settings.GuessingStoppedReply,
            GuessingAlreadyStoppedReply = settings.GuessingAlreadyStoppedReply,
            GuessingClosedReply = settings.GuessingClosedReply,
            InvalidGuessReply = settings.InvalidGuessReply,
            GuessUsageReply = settings.GuessUsageReply,
            AvailableGuessesReply = string.IsNullOrWhiteSpace(settings.AvailableGuessesReply)
                ? GuessingDefaults.Replies().AvailableGuessesReply
                : settings.AvailableGuessesReply,
            WinUsageReply = settings.WinUsageReply,
            ModeratorOnlyReply = settings.ModeratorOnlyReply,
            WinnerReply = settings.WinnerReply,
            NoWinnersReply = settings.NoWinnersReply,
        };
}

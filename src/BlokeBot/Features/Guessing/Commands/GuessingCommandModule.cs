using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Rounds;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandModule(
    GuessingCommandService commands,
    GuessingRoundService rounds,
    GuessingVoteService votes
)
{
    public async Task HandleAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        GuessCommandKind kind,
        CancellationToken ct
    )
    {
        var hostLogin = context.Message.Channel;
        if (kind is GuessCommandKind.Start or GuessCommandKind.Stop or GuessCommandKind.Win)
        {
            if (!TwitchModeratorPolicy.IsModerator(context.Message))
            {
                await context.ReplyAsync(await commands.ModeratorOnlyReplyAsync(hostLogin, ct), ct);
                return;
            }
        }

        var result = kind switch
        {
            GuessCommandKind.Start => args.Count <= 1
                ? await rounds.StartRoundAsync(hostLogin, args.Count == 0 ? null : args[0], ct)
                : new GuessingOperationResult(
                    false,
                    await commands.UsageReplyAsync(
                        hostLogin,
                        GuessCommandKind.Start,
                        context.CommandName,
                        ct
                    )
                ),
            GuessCommandKind.Stop => await rounds.StopGuessingAsync(hostLogin, ct),
            GuessCommandKind.Win => args.Count == 1
                ? await rounds.DeclareWinnerAsync(hostLogin, args[0], ct)
                : new GuessingOperationResult(
                    false,
                    await commands.UsageReplyAsync(
                        hostLogin,
                        GuessCommandKind.Win,
                        context.CommandName,
                        ct
                    )
                ),
            GuessCommandKind.Guess => args.Count == 1
                ? await votes.RecordGuessAsync(hostLogin, context.Message.Login, args[0], ct)
                : new GuessingOperationResult(
                    false,
                    await commands.UsageReplyAsync(
                        hostLogin,
                        GuessCommandKind.Guess,
                        context.CommandName,
                        ct
                    )
                ),
            GuessCommandKind.Guesses => new GuessingOperationResult(
                true,
                await commands.AvailableGuessesReplyAsync(hostLogin, ct)
            ),
            _ => null,
        };

        if (result is not null && !string.IsNullOrWhiteSpace(result.Message))
            await context.ReplyAsync(result.Message, ct);
    }
}

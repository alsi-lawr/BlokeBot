using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Rounds;

namespace BlokeBot.Features.Guessing.Commands;

public abstract class GuessingCommandStrategy(GuessingCommandService commands)
    : ICommandStrategy<GuessCommandKind, AppCommandRouteState>
{
    protected GuessingCommandService Commands { get; } = commands;

    public abstract GuessCommandKind Kind { get; }

    public abstract IReadOnlyList<string> DefaultAliases { get; }

    public abstract bool RequiresModerator { get; }

    public async ValueTask<string> ModeratorOnlyReplyAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    ) => await Commands.ModeratorOnlyReplyAsync(context.Command.Message.Channel, cancellationToken);

    public abstract ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    );

    protected async ValueTask ReplyAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        GuessingOperationResult result,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            await context.Command.ReplyAsync(result.Message, cancellationToken);
    }

    protected async Task<GuessingOperationResult> UsageAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    ) =>
        new(
            false,
            await Commands.UsageReplyAsync(
                context.Command.Message.Channel,
                Kind,
                context.Command.CommandName,
                cancellationToken
            )
        );
}

public sealed class StartGuessingCommandStrategy(
    GuessingCommandService commands,
    GuessingRoundService rounds
) : GuessingCommandStrategy(commands)
{
    public override GuessCommandKind Kind => GuessCommandKind.Start;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["startguessing"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var result =
            context.Args.Count <= 1
                ? await rounds.StartRoundAsync(
                    context.Command.Message.Channel,
                    context.Args.Count == 0 ? null : context.Args[0],
                    cancellationToken
                )
                : await UsageAsync(context, cancellationToken);

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class StopGuessingCommandStrategy(
    GuessingCommandService commands,
    GuessingRoundService rounds
) : GuessingCommandStrategy(commands)
{
    public override GuessCommandKind Kind => GuessCommandKind.Stop;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["stopguessing"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        await ReplyAsync(
            context,
            await rounds.StopGuessingAsync(context.Command.Message.Channel, cancellationToken),
            cancellationToken
        );
    }
}

public sealed class WinGuessingCommandStrategy(
    GuessingCommandService commands,
    GuessingRoundService rounds
) : GuessingCommandStrategy(commands)
{
    public override GuessCommandKind Kind => GuessCommandKind.Win;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["win"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var result =
            context.Args.Count == 1
                ? await rounds.DeclareWinnerAsync(
                    context.Command.Message.Channel,
                    context.Args[0],
                    cancellationToken
                )
                : await UsageAsync(context, cancellationToken);

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class GuessCommandStrategy(GuessingCommandService commands, GuessingVoteService votes)
    : GuessingCommandStrategy(commands)
{
    public override GuessCommandKind Kind => GuessCommandKind.Guess;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["guess"];

    public override bool RequiresModerator => false;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var result =
            context.Args.Count == 1
                ? await votes.RecordGuessAsync(
                    context.Command.Message.Channel,
                    context.Command.Message.Login,
                    context.Args[0],
                    cancellationToken
                )
                : await UsageAsync(context, cancellationToken);

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class AvailableGuessesCommandStrategy(GuessingCommandService commands)
    : GuessingCommandStrategy(commands)
{
    public override GuessCommandKind Kind => GuessCommandKind.Guesses;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["guesses"];

    public override bool RequiresModerator => false;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var reply = await Commands.AvailableGuessesReplyAsync(
            context.Command.Message.Channel,
            cancellationToken
        );
        if (!string.IsNullOrWhiteSpace(reply))
            await context.Command.ReplyAsync(reply, cancellationToken);
    }
}

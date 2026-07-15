using System.Diagnostics;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Functional;

namespace BlokeBot.Features.Guessing.Commands;

public abstract class GuessingCommandStrategy(GuessingCommandService commands)
    : ICommandStrategy<GuessCommandKind, AppCommandRouteState>
{
    protected GuessingCommandService Commands { get; } = commands;

    public abstract GuessCommandKind Kind { get; }

    public abstract IReadOnlyList<string> DefaultAliases { get; }

    public abstract CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access { get; }

    public async ValueTask<CommandResponse> ModeratorOnlyResponseAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        return await Commands.ModeratorOnlyResponseAsync(
            context.Command.Message.Channel,
            context.State,
            cancellationToken
        );
    }

    public abstract ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    );

    protected async ValueTask ReplyAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        GuessingOperationOutcome result,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            await context.Command.RespondAsync(
                new CommandResponse(result.Target, result.Message),
                cancellationToken
            );
        }
    }

    protected async Task<GuessingOperationOutcome> UsageAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var response = await Commands.UsageResponseAsync(
            context.Command.Message.Channel,
            Kind,
            context.Command.CommandName,
            context.State,
            cancellationToken
        );
        return new GuessingOperationOutcome.Rejected(response.Message, response.Target);
    }

    protected static async Task<TValue> ExecuteAsync<TValue>(
        IO<TValue, Never> operation,
        CancellationToken cancellationToken
    )
    {
        var result = await operation.ExecuteAsync(cancellationToken);
        return result.Match(value => value, _ => throw new UnreachableException());
    }
}

public sealed class StartGuessingCommandStrategy(
    GuessingCommandService commands,
    GuessingRoundService rounds
) : GuessingCommandStrategy(commands)
{
    public override GuessCommandKind Kind => GuessCommandKind.Start;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["startguessing"];

    public override CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<GuessCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        GuessingOperationOutcome result;
        if (context.Args.Count == 0)
        {
            var operation = context.State.Match(
                _ => rounds.StartRound(context.Command.Message.Channel, null),
                guessingProfile =>
                    rounds.StartRound(guessingProfile.HostId, guessingProfile.ProfileId)
            );
            result = await ExecuteAsync(operation, cancellationToken);
        }
        else if (context.Args.Count == 1)
        {
            result = await ExecuteAsync(
                rounds.StartRound(context.Command.Message.Channel, context.Args[0]),
                cancellationToken
            );
        }
        else
        {
            result = await UsageAsync(context, cancellationToken);
        }

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

    public override CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<GuessCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        await ReplyAsync(
            context,
            await ExecuteAsync(
                rounds.StopGuessing(context.Command.Message.Channel),
                cancellationToken
            ),
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

    public override CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<GuessCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var result =
            context.Args.Count == 1
                ? (
                    await ExecuteAsync(
                        rounds.DeclareWinner(context.Command.Message.Channel, context.Args[0]),
                        cancellationToken
                    )
                ).Match(
                    completed => completed.Result,
                    failed => new GuessingOperationOutcome.Rejected(failed.Message, failed.Target)
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

    public override CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<GuessCommandKind, AppCommandRouteState>.Everyone();

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var result =
            context.Args.Count == 1
                ? await ExecuteAsync(
                    votes.RecordGuess(
                        context.Command.Message.Channel,
                        context.Command.Message.Login,
                        context.Args[0]
                    ),
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

    public override CommandStrategyAccess<GuessCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<GuessCommandKind, AppCommandRouteState>.Everyone();

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<GuessCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var response = await Commands.AvailableGuessesResponseAsync(
            context.Command.Message.Channel,
            context.State,
            cancellationToken
        );
        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            await context.Command.RespondAsync(response, cancellationToken);
        }
    }
}

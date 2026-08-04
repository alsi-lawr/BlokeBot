namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandModule(CustomCommandExecutionService execution)
    : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) => commands.MapDynamic(ExecuteAsync);

    private async ValueTask<CommandHandlingOutcome> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var outcome = await execution.ExecuteAsync(context, args, cancellationToken);
        return outcome.Match<CommandHandlingOutcome>(
            _ => new CommandHandlingOutcome.Unhandled(),
            _ => new CommandHandlingOutcome.Handled(),
            _ => new CommandHandlingOutcome.Handled(),
            _ => new CommandHandlingOutcome.Handled(),
            _ => new CommandHandlingOutcome.Handled(),
            _ => new CommandHandlingOutcome.Handled(),
            _ => new CommandHandlingOutcome.Handled(),
            _ => new CommandHandlingOutcome.Handled()
        );
    }
}

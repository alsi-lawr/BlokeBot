using BlokeBot.Commands;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandModule(CustomCommandExecutionService execution)
    : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        commands.MapDynamic(ExecuteAsync);
    }

    private async ValueTask<bool> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        return await execution.TryExecuteAsync(context, args, cancellationToken);
    }
}

using BlokeBot.Commands;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandModule(CustomCommandExecutionService execution) : ITwitchCommandModule
{
    public void AddCommands(ITwitchCommandBuilder commands)
    {
        commands.MapDynamic(ExecuteAsync);
    }

    private async ValueTask<bool> ExecuteAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    ) => await execution.TryExecuteAsync(context, args, cancellationToken);
}

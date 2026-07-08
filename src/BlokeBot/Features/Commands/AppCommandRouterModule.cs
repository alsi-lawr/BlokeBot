using Alsi.TwitchBot;

namespace BlokeBot.Features.Commands;

public sealed class AppCommandRouterModule(IEnumerable<AppChatCommandHandler> handlers)
    : ITwitchCommandModule
{
    public void AddCommands(ITwitchCommandBuilder commands)
    {
        commands.MapFallback(RouteAsync);
    }

    private async ValueTask RouteAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        foreach (var handler in handlers)
        {
            if (await handler.TryHandleAsync(context, args, ct))
                return;
        }
    }
}

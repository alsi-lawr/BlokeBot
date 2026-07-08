using Alsi.TwitchBot;

namespace BlokeBot.Features.Commands;

public sealed class AppCommandRouterModule(AppCommandDispatcher dispatcher) : ITwitchCommandModule
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
        _ = await dispatcher.DispatchAsync(context, args, ct);
    }
}

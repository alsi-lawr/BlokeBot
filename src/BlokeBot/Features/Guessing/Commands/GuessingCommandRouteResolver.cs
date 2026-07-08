using BlokeBot.Features.Commands;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandRouteResolver(AppCommandAliasResolver aliases)
    : ICommandRouteResolver<GuessCommandKind, AppCommandRouteState>
{
    public async ValueTask<CommandRoute<GuessCommandKind, AppCommandRouteState>?> ResolveAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await aliases.ResolveAsync(
            context.Message.Channel,
            context.CommandName,
            cancellationToken
        );
        if (
            resolution is null
            || !GuessingAppCommandKindMap.TryFromAppKind(resolution.Kind, out var kind)
        )
        {
            return null;
        }

        return new CommandRoute<GuessCommandKind, AppCommandRouteState>(
            kind,
            new AppCommandRouteState(resolution.HostId)
        );
    }
}

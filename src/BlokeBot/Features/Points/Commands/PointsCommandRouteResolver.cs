using BlokeBot.Features.Commands;

namespace BlokeBot.Features.Points.Commands;

public sealed class PointsCommandRouteResolver(AppCommandAliasResolver aliases)
    : ICommandRouteResolver<PointsCommandKind, AppCommandRouteState>
{
    public async ValueTask<CommandRoute<PointsCommandKind, AppCommandRouteState>?> ResolveAsync(
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
            || !PointsAppCommandKindMap.TryFromAppKind(resolution.Kind, out var kind)
        )
        {
            return null;
        }

        return new CommandRoute<PointsCommandKind, AppCommandRouteState>(
            kind,
            new AppCommandRouteState(resolution.HostId)
        );
    }
}

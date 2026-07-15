using BlokeBot.Features.Commands;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Commands;

public sealed class PointsCommandRouteResolver(
    AppCommandAliasResolver aliases,
    HostFeatureService features
) : ICommandRouteResolver<PointsCommandKind, AppCommandRouteState>
{
    public async ValueTask<CommandRoute<PointsCommandKind, AppCommandRouteState>?> ResolveAsync(
        ChatCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await aliases.ResolveAsync(
            context.Message.Channel,
            context.CommandName,
            cancellationToken
        );
        if (resolution is null)
        {
            return null;
        }

        var kind = PointsAppCommandKindMap
            .FromAppKind(resolution.Kind)
            .Match<PointsCommandKind?>(value => value, () => null);
        if (kind is not { } mappedKind)
        {
            return null;
        }

        if (
            !await features.IsEnabledAsync(
                resolution.HostId,
                HostFeatureFlags.Points,
                cancellationToken
            )
        )
        {
            return null;
        }

        return new CommandRoute<PointsCommandKind, AppCommandRouteState>(
            mappedKind,
            new AppCommandRouteState.Host(resolution.HostId)
        );
    }
}

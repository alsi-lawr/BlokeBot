using BlokeBot.Features.Commands;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Commands;

public sealed class PointsCommandRouteResolver(
    AppCommandAliasResolver aliases,
    HostFeatureService features
) : ICommandRouteResolver<PointsCommandKind, AppCommandRouteState>
{
    public async ValueTask<
        CommandRouteResolution<PointsCommandKind, AppCommandRouteState>
    > ResolveAsync(ChatCommandContext context, CancellationToken cancellationToken)
    {
        var resolution = await aliases.ResolveAsync(
            context.Message.Channel,
            context.CommandName,
            cancellationToken
        );
        if (resolution is null)
        {
            return new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved();
        }

        var kind = PointsAppCommandKindMap
            .FromAppKind(resolution.Kind)
            .Match<PointsCommandKind?>(value => value, () => null);
        if (kind is not { } mappedKind)
        {
            return new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved();
        }

        if (
            !await features.IsEnabledAsync(
                resolution.HostId,
                HostFeatureFlags.Points,
                cancellationToken
            )
        )
        {
            return new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved();
        }

        return new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Resolved(
            new CommandRoute<PointsCommandKind, AppCommandRouteState>(
                mappedKind,
                new AppCommandRouteState.Host(resolution.HostId)
            )
        );
    }
}

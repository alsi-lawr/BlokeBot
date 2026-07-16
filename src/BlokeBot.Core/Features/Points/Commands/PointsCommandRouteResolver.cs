using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.Commands;

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

        return await PointsAppCommandKindMap
            .FromAppKind(resolution.Kind)
            .Match(ResolveKindAsync, Unresolved);

        async ValueTask<
            CommandRouteResolution<PointsCommandKind, AppCommandRouteState>
        > ResolveKindAsync(PointsCommandKind kind)
        {
            if (
                !await features.IsEnabledAsync(
                    resolution.HostId,
                    HostFeatureFlags.Points,
                    cancellationToken
                )
            )
            {
                return new CommandRouteResolution<
                    PointsCommandKind,
                    AppCommandRouteState
                >.Unresolved();
            }

            return new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Resolved(
                new CommandRoute<PointsCommandKind, AppCommandRouteState>(
                    kind,
                    new AppCommandRouteState.Host(resolution.HostId)
                )
            );
        }

        static ValueTask<
            CommandRouteResolution<PointsCommandKind, AppCommandRouteState>
        > Unresolved()
        {
            return ValueTask.FromResult<
                CommandRouteResolution<PointsCommandKind, AppCommandRouteState>
            >(new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved());
        }
    }
}

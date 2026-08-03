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
        return resolution is null
            ? new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved()
            : await PointsAppCommandKindMap
                .FromAppKind(resolution.Kind)
                .Match(ResolveKindAsync, Unresolved);

        async ValueTask<
            CommandRouteResolution<PointsCommandKind, AppCommandRouteState>
        > ResolveKindAsync(PointsCommandKind kind) =>
            !await features.IsEnabledAsync(
                resolution.HostId,
                HostFeatureFlags.Points,
                cancellationToken
            )
                ? new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved()
                : new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Resolved(
                    new CommandRoute<PointsCommandKind, AppCommandRouteState>(
                        kind,
                        new AppCommandRouteState.Host(resolution.HostId)
                    )
                );

        static ValueTask<
            CommandRouteResolution<PointsCommandKind, AppCommandRouteState>
        > Unresolved() =>
            ValueTask.FromResult<CommandRouteResolution<PointsCommandKind, AppCommandRouteState>>(
                new CommandRouteResolution<PointsCommandKind, AppCommandRouteState>.Unresolved()
            );
    }
}

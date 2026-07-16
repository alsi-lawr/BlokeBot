using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Commands;

public sealed class GuessingCommandRouteResolver(
    AppCommandAliasResolver aliases,
    HostFeatureService features
) : ICommandRouteResolver<GuessCommandKind, AppCommandRouteState>
{
    public async ValueTask<
        CommandRouteResolution<GuessCommandKind, AppCommandRouteState>
    > ResolveAsync(ChatCommandContext context, CancellationToken cancellationToken)
    {
        var resolution = await aliases.ResolveAsync(
            context.Message.Channel,
            context.CommandName,
            cancellationToken
        );
        if (resolution is null)
        {
            return new CommandRouteResolution<GuessCommandKind, AppCommandRouteState>.Unresolved();
        }

        return await GuessingAppCommandKindMap
            .FromAppKind(resolution.Kind)
            .Match(ResolveKindAsync, Unresolved);

        async ValueTask<
            CommandRouteResolution<GuessCommandKind, AppCommandRouteState>
        > ResolveKindAsync(GuessCommandKind kind)
        {
            if (
                !await features.IsEnabledAsync(
                    resolution.HostId,
                    HostFeatureFlags.Guessing,
                    cancellationToken
                )
            )
            {
                return new CommandRouteResolution<
                    GuessCommandKind,
                    AppCommandRouteState
                >.Unresolved();
            }

            var state = resolution.Scope.Match<AppCommandRouteState>(
                _ => new AppCommandRouteState.Host(resolution.HostId),
                profile => new AppCommandRouteState.GuessingProfile(
                    resolution.HostId,
                    profile.ProfileId
                )
            );
            return new CommandRouteResolution<GuessCommandKind, AppCommandRouteState>.Resolved(
                new CommandRoute<GuessCommandKind, AppCommandRouteState>(kind, state)
            );
        }

        static ValueTask<
            CommandRouteResolution<GuessCommandKind, AppCommandRouteState>
        > Unresolved()
        {
            return ValueTask.FromResult<
                CommandRouteResolution<GuessCommandKind, AppCommandRouteState>
            >(new CommandRouteResolution<GuessCommandKind, AppCommandRouteState>.Unresolved());
        }
    }
}

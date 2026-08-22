using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Commands;

public sealed class GuessingCommandRouteResolver(
    AppCommandAliasResolver aliases,
    HostFeatureService features,
    IDbContextFactory<BlokeBotDbContext> dbFactory
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
        return resolution is null
            ? new CommandRouteResolution<GuessCommandKind, AppCommandRouteState>.Unresolved()
            : await GuessingAppCommandKindMap
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

            var state = await ResolveStateAsync(kind);
            return new CommandRouteResolution<GuessCommandKind, AppCommandRouteState>.Resolved(
                new CommandRoute<GuessCommandKind, AppCommandRouteState>(kind, state)
            );
        }

        async Task<AppCommandRouteState> ResolveStateAsync(GuessCommandKind kind)
        {
            if (kind is not GuessCommandKind.Start)
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                var round = await GuessingRoundQueries.LoadUnresolvedAsync(
                    db,
                    resolution.HostId,
                    cancellationToken
                );
                if (round is not null)
                {
                    return new AppCommandRouteState.GuessingProfile(
                        resolution.HostId,
                        round.ProfileId
                    );
                }
            }

            return resolution.Scope.Match<AppCommandRouteState>(
                _ => new AppCommandRouteState.Host(resolution.HostId),
                profile => new AppCommandRouteState.GuessingProfile(
                    resolution.HostId,
                    profile.ProfileId
                )
            );
        }

        static ValueTask<
            CommandRouteResolution<GuessCommandKind, AppCommandRouteState>
        > Unresolved() =>
            ValueTask.FromResult<CommandRouteResolution<GuessCommandKind, AppCommandRouteState>>(
                new CommandRouteResolution<GuessCommandKind, AppCommandRouteState>.Unresolved()
            );
    }
}

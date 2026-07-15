using BlokeBot.Features.Commands;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandRouteResolver(
    AppCommandAliasResolver aliases,
    HostFeatureService features
) : ICommandRouteResolver<GuessCommandKind, AppCommandRouteState>
{
    public async ValueTask<CommandRoute<GuessCommandKind, AppCommandRouteState>?> ResolveAsync(
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

        var kind = GuessingAppCommandKindMap
            .FromAppKind(resolution.Kind)
            .Match<GuessCommandKind?>(value => value, () => null);
        if (kind is not { } mappedKind)
        {
            return null;
        }

        if (
            !await features.IsEnabledAsync(
                resolution.HostId,
                HostFeatureFlags.Guessing,
                cancellationToken
            )
        )
        {
            return null;
        }

        var state = resolution.Scope.Match<AppCommandRouteState>(
            _ => new AppCommandRouteState.Host(resolution.HostId),
            profile => new AppCommandRouteState.GuessingProfile(
                resolution.HostId,
                profile.ProfileId
            )
        );
        return new CommandRoute<GuessCommandKind, AppCommandRouteState>(mappedKind, state);
    }
}

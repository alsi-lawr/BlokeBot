using Alsi.TwitchBot;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Commands;

public sealed class AppCommandDispatcher(
    AppCommandCatalog catalog,
    GuessingCommandModule guessing,
    PointsCommandModule points,
    PointsCommandService pointCommands
)
{
    public async ValueTask<AppCommandDispatchResult> DispatchAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var resolution = await catalog.ResolveAsync(
            context.Message.Channel,
            context.CommandName,
            ct
        );
        if (resolution is null)
            return AppCommandDispatchResult.Unknown();

        if (AppCommandCatalog.IsGuessing(resolution.Kind))
        {
            await guessing.HandleAsync(
                context,
                args,
                AppCommandCatalog.ToGuessingKind(resolution.Kind),
                ct
            );
            return AppCommandDispatchResult.Handled(resolution.Kind);
        }

        if (AppCommandCatalog.IsPoints(resolution.Kind))
        {
            var pointsResolution = await pointCommands.CreateResolutionAsync(
                resolution.HostId,
                AppCommandCatalog.ToPointsKind(resolution.Kind),
                ct
            );
            await points.HandleAsync(context, args, pointsResolution, ct);
            return AppCommandDispatchResult.Handled(resolution.Kind);
        }

        return AppCommandDispatchResult.Unknown();
    }
}

public enum AppCommandDispatchStatus
{
    Unknown,
    Handled,
}

public sealed record AppCommandDispatchResult(
    AppCommandDispatchStatus Status,
    AppCommandKind? Kind
)
{
    public static AppCommandDispatchResult Unknown() => new(AppCommandDispatchStatus.Unknown, null);

    public static AppCommandDispatchResult Handled(AppCommandKind kind) =>
        new(AppCommandDispatchStatus.Handled, kind);
}

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

        var descriptor = AppCommandCatalog.Describe(resolution.Kind);
        if (descriptor.GuessingKind is { } guessingKind)
        {
            await guessing.HandleAsync(
                context,
                args,
                guessingKind,
                ct
            );
            return AppCommandDispatchResult.Handled(resolution.Kind);
        }

        if (descriptor.PointsKind is { } pointsKind)
        {
            var pointsResolution = await pointCommands.CreateResolutionAsync(
                resolution.HostId,
                pointsKind,
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

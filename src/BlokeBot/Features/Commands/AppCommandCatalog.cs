using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Commands;

public sealed class AppCommandCatalog(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<AppCommandResolution?> ResolveAsync(
        string hostLogin,
        string alias,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedHost = LoginName.Parse(hostLogin).Value;
        var hostId = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == normalizedHost)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is null)
            return null;

        var normalizedAlias = CommandAliasNormalizer.Normalize(alias);
        var kind = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId.Value && x.Alias == normalizedAlias)
            .Select(x => (AppCommandKind?)x.Kind)
            .FirstOrDefaultAsync(ct);

        return kind is null ? null : new AppCommandResolution(hostId.Value, kind.Value);
    }

    public static bool IsGuessing(AppCommandKind kind) =>
        kind
            is AppCommandKind.Start
                or AppCommandKind.Stop
                or AppCommandKind.Win
                or AppCommandKind.Guess
                or AppCommandKind.Guesses;

    public static bool IsPoints(AppCommandKind kind) =>
        kind
            is AppCommandKind.Points
                or AppCommandKind.GivePoints
                or AppCommandKind.AddPoints
                or AppCommandKind.RemovePoints
                or AppCommandKind.Gamble
                or AppCommandKind.Giveaway
                or AppCommandKind.Join
                or AppCommandKind.EndGiveaway
                or AppCommandKind.CancelGiveaway;

    public static bool RequiresModerator(AppCommandKind kind) =>
        kind
            is AppCommandKind.Start
                or AppCommandKind.Stop
                or AppCommandKind.Win
                or AppCommandKind.AddPoints
                or AppCommandKind.RemovePoints
                or AppCommandKind.Giveaway
                or AppCommandKind.EndGiveaway
                or AppCommandKind.CancelGiveaway;

    public static GuessCommandKind ToGuessingKind(AppCommandKind kind) =>
        kind switch
        {
            AppCommandKind.Start => GuessCommandKind.Start,
            AppCommandKind.Stop => GuessCommandKind.Stop,
            AppCommandKind.Win => GuessCommandKind.Win,
            AppCommandKind.Guess => GuessCommandKind.Guess,
            AppCommandKind.Guesses => GuessCommandKind.Guesses,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static PointsCommandKind ToPointsKind(AppCommandKind kind) =>
        kind switch
        {
            AppCommandKind.Points => PointsCommandKind.Points,
            AppCommandKind.GivePoints => PointsCommandKind.GivePoints,
            AppCommandKind.AddPoints => PointsCommandKind.AddPoints,
            AppCommandKind.RemovePoints => PointsCommandKind.RemovePoints,
            AppCommandKind.Gamble => PointsCommandKind.Gamble,
            AppCommandKind.Giveaway => PointsCommandKind.Giveaway,
            AppCommandKind.Join => PointsCommandKind.Join,
            AppCommandKind.EndGiveaway => PointsCommandKind.EndGiveaway,
            AppCommandKind.CancelGiveaway => PointsCommandKind.CancelGiveaway,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static AppCommandKind FromGuessingKind(GuessCommandKind kind) =>
        kind switch
        {
            GuessCommandKind.Start => AppCommandKind.Start,
            GuessCommandKind.Stop => AppCommandKind.Stop,
            GuessCommandKind.Win => AppCommandKind.Win,
            GuessCommandKind.Guess => AppCommandKind.Guess,
            GuessCommandKind.Guesses => AppCommandKind.Guesses,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static AppCommandKind FromPointsKind(PointsCommandKind kind) =>
        kind switch
        {
            PointsCommandKind.Points => AppCommandKind.Points,
            PointsCommandKind.GivePoints => AppCommandKind.GivePoints,
            PointsCommandKind.AddPoints => AppCommandKind.AddPoints,
            PointsCommandKind.RemovePoints => AppCommandKind.RemovePoints,
            PointsCommandKind.Gamble => AppCommandKind.Gamble,
            PointsCommandKind.Giveaway => AppCommandKind.Giveaway,
            PointsCommandKind.Join => AppCommandKind.Join,
            PointsCommandKind.EndGiveaway => AppCommandKind.EndGiveaway,
            PointsCommandKind.CancelGiveaway => AppCommandKind.CancelGiveaway,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}

public sealed record AppCommandResolution(int HostId, AppCommandKind Kind);

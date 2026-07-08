using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Commands;

public sealed class AppCommandCatalog(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    private static readonly AppCommandDescriptor[] AllDescriptors = [.. CreateDescriptors()];

    private static readonly IReadOnlyDictionary<AppCommandKind, AppCommandDescriptor>
        DescriptorByKind = AllDescriptors.ToDictionary(x => x.Kind);

    private static readonly IReadOnlyDictionary<GuessCommandKind, AppCommandKind>
        AppKindByGuessingKind = DescriptorByKind.Values
            .Where(x => x.GuessingKind is not null)
            .ToDictionary(x => x.GuessingKind!.Value, x => x.Kind);

    private static readonly IReadOnlyDictionary<PointsCommandKind, AppCommandKind>
        AppKindByPointsKind = DescriptorByKind.Values
            .Where(x => x.PointsKind is not null)
            .ToDictionary(x => x.PointsKind!.Value, x => x.Kind);

    public static IReadOnlyList<AppCommandDescriptor> Descriptors => AllDescriptors;

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

    public static AppCommandDescriptor Describe(AppCommandKind kind) => DescriptorByKind[kind];

    public static IReadOnlyList<AppCommandDescriptor> ForFeature(AppCommandFeature feature) =>
        Descriptors.Where(x => x.Feature == feature).ToArray();

    public static bool RequiresModerator(AppCommandKind kind) => Describe(kind).RequiresModerator;

    public static GuessCommandKind ToGuessingKind(AppCommandKind kind) =>
        Describe(kind).GuessingKind
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    public static PointsCommandKind ToPointsKind(AppCommandKind kind) =>
        Describe(kind).PointsKind
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    public static AppCommandKind FromGuessingKind(GuessCommandKind kind) =>
        AppKindByGuessingKind.TryGetValue(kind, out var appKind)
            ? appKind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    public static AppCommandKind FromPointsKind(PointsCommandKind kind) =>
        AppKindByPointsKind.TryGetValue(kind, out var appKind)
            ? appKind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    private static IReadOnlyList<AppCommandDescriptor> CreateDescriptors() =>
        [
            Guessing(
                AppCommandKind.Start,
                GuessCommandKind.Start,
                requiresModerator: true,
                "startguessing"
            ),
            Guessing(
                AppCommandKind.Stop,
                GuessCommandKind.Stop,
                requiresModerator: true,
                "stopguessing"
            ),
            Guessing(AppCommandKind.Win, GuessCommandKind.Win, requiresModerator: true, "win"),
            Guessing(
                AppCommandKind.Guess,
                GuessCommandKind.Guess,
                requiresModerator: false,
                "guess"
            ),
            Guessing(
                AppCommandKind.Guesses,
                GuessCommandKind.Guesses,
                requiresModerator: false,
                "guesses"
            ),
            Points(
                AppCommandKind.Points,
                PointsCommandKind.Points,
                requiresModerator: false,
                "points"
            ),
            Points(
                AppCommandKind.GivePoints,
                PointsCommandKind.GivePoints,
                requiresModerator: false,
                "givepoints"
            ),
            Points(
                AppCommandKind.AddPoints,
                PointsCommandKind.AddPoints,
                requiresModerator: true,
                "addpoints"
            ),
            Points(
                AppCommandKind.RemovePoints,
                PointsCommandKind.RemovePoints,
                requiresModerator: true,
                "removepoints"
            ),
            Points(
                AppCommandKind.Gamble,
                PointsCommandKind.Gamble,
                requiresModerator: false,
                "gamble"
            ),
            Points(
                AppCommandKind.Giveaway,
                PointsCommandKind.Giveaway,
                requiresModerator: true,
                "giveaway"
            ),
            Points(
                AppCommandKind.Join,
                PointsCommandKind.Join,
                requiresModerator: false,
                "join"
            ),
            Points(
                AppCommandKind.EndGiveaway,
                PointsCommandKind.EndGiveaway,
                requiresModerator: true,
                "endgiveaway"
            ),
            Points(
                AppCommandKind.CancelGiveaway,
                PointsCommandKind.CancelGiveaway,
                requiresModerator: true,
                "cancelgiveaway"
            ),
        ];

    private static AppCommandDescriptor Guessing(
        AppCommandKind kind,
        GuessCommandKind guessingKind,
        bool requiresModerator,
        params string[] defaultAliases
    ) =>
        new(
            kind,
            AppCommandFeature.Guessing,
            requiresModerator,
            CommandAliasNormalizer.NormalizeMany(defaultAliases),
            guessingKind,
            null
        );

    private static AppCommandDescriptor Points(
        AppCommandKind kind,
        PointsCommandKind pointsKind,
        bool requiresModerator,
        params string[] defaultAliases
    ) =>
        new(
            kind,
            AppCommandFeature.Points,
            requiresModerator,
            CommandAliasNormalizer.NormalizeMany(defaultAliases),
            null,
            pointsKind
        );
}

public sealed record AppCommandResolution(int HostId, AppCommandKind Kind);

public enum AppCommandFeature
{
    Guessing,
    Points,
}

public sealed record AppCommandDescriptor(
    AppCommandKind Kind,
    AppCommandFeature Feature,
    bool RequiresModerator,
    IReadOnlyList<string> DefaultAliases,
    GuessCommandKind? GuessingKind,
    PointsCommandKind? PointsKind
);

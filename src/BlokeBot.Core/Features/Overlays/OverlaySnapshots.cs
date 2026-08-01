using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

public interface IOverlayStateProvider
{
    Task<OverlaySnapshotProjection> ProjectAsync(
        ResolvedOverlayInstance instance,
        CancellationToken cancellationToken
    );

    Task<OverlaySnapshotProjection> ProjectSampleAsync(
        ResolvedOverlayInstance instance,
        GuessingOverlaySampleState sample,
        CancellationToken cancellationToken
    ) => Task.FromResult<OverlaySnapshotProjection>(new OverlaySnapshotProjection.Unavailable());

    Task<OverlaySnapshotProjection> ProjectSampleAsync(
        ResolvedOverlayInstance instance,
        GiveawayOverlaySampleState sample,
        CancellationToken cancellationToken
    ) => Task.FromResult<OverlaySnapshotProjection>(new OverlaySnapshotProjection.Unavailable());
}

public enum GuessingOverlaySampleState
{
    NoRound,
    Open,
    Closed,
    Completed,
}

public enum GiveawayOverlaySampleState
{
    Idle,
    Open,
    Ending,
    Completed,
    Cancelled,
}

public abstract record OverlaySnapshotProjection
{
    private OverlaySnapshotProjection() { }

    public sealed record EmptyV1(EmptyV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record GuessingV1(GuessingV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record CuePlayerV1(CuePlayerV1OverlaySnapshot Snapshot)
        : OverlaySnapshotProjection;

    public sealed record GiveawayV1(GiveawayV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record Unavailable : OverlaySnapshotProjection;
}

public sealed record EmptyV1OverlaySnapshot
{
    public string OverlayType => "empty";

    public int SchemaVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public EmptyV1OverlayPresentationState State { get; } = new();
}

public sealed record EmptyV1OverlayPresentationState;

public sealed record CuePlayerV1OverlaySnapshot
{
    public string OverlayType => "cuePlayer";

    public int SchemaVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public CuePlayerV1OverlayPresentationState State { get; } = new();
}

public sealed record CuePlayerV1OverlayPresentationState;

public sealed record GuessingV1OverlaySnapshot
{
    public string OverlayType => "guessing";

    public int SchemaVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required int ResultDurationMilliseconds { get; init; }

    public required GuessingV1OverlayPresentationState State { get; init; }
}

public sealed record GiveawayV1OverlaySnapshot
{
    public string OverlayType => "giveaway";

    public int SchemaVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public int WinnerAnimationDurationMilliseconds => 5000;

    public required GiveawayV1OverlayPresentationState State { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "phase")]
[JsonDerivedType(typeof(GiveawayV1OverlayPresentationState.Idle), "idle")]
[JsonDerivedType(typeof(GiveawayV1OverlayPresentationState.Open), "open")]
[JsonDerivedType(typeof(GiveawayV1OverlayPresentationState.Ending), "ending")]
[JsonDerivedType(typeof(GiveawayV1OverlayPresentationState.Completed), "completed")]
[JsonDerivedType(typeof(GiveawayV1OverlayPresentationState.Cancelled), "cancelled")]
public abstract record GiveawayV1OverlayPresentationState
{
    private GiveawayV1OverlayPresentationState() { }

    public required string Title { get; init; }

    internal abstract GiveawayOverlayPhase Phase { get; }

    public sealed record Idle : GiveawayV1OverlayPresentationState
    {
        internal override GiveawayOverlayPhase Phase => GiveawayOverlayPhase.Idle;
    }

    public sealed record Open : GiveawayV1OverlayPresentationState
    {
        public int? EntrantCount { get; init; }

        public DateTimeOffset? ClosesAtUtc { get; init; }

        public string? JoinCommand { get; init; }

        internal override GiveawayOverlayPhase Phase => GiveawayOverlayPhase.Open;
    }

    public sealed record Ending : GiveawayV1OverlayPresentationState
    {
        public int? EntrantCount { get; init; }

        internal override GiveawayOverlayPhase Phase => GiveawayOverlayPhase.Ending;
    }

    public sealed record Completed : GiveawayV1OverlayPresentationState
    {
        public ImmutableArray<GiveawayWinnerPresentation> Winners { get; init; } = [];

        public string? PointLabel { get; init; }

        public required DateTimeOffset CompletedAtUtc { get; init; }

        internal override GiveawayOverlayPhase Phase => GiveawayOverlayPhase.Completed;
    }

    public sealed record Cancelled : GiveawayV1OverlayPresentationState
    {
        public required string Message { get; init; }

        public required DateTimeOffset CompletedAtUtc { get; init; }

        internal override GiveawayOverlayPhase Phase => GiveawayOverlayPhase.Cancelled;
    }
}

public sealed record GiveawayWinnerPresentation
{
    public required string Login { get; init; }

    public required string AwardedPoints { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "phase")]
[JsonDerivedType(typeof(GuessingV1OverlayPresentationState.NoRound), "noRound")]
[JsonDerivedType(typeof(GuessingV1OverlayPresentationState.Open), "open")]
[JsonDerivedType(typeof(GuessingV1OverlayPresentationState.Closed), "closed")]
[JsonDerivedType(typeof(GuessingV1OverlayPresentationState.Completed), "completed")]
public abstract record GuessingV1OverlayPresentationState
{
    private GuessingV1OverlayPresentationState() { }

    internal abstract GuessingOverlayPhase Phase { get; }

    public sealed record NoRound : GuessingV1OverlayPresentationState
    {
        internal override GuessingOverlayPhase Phase => GuessingOverlayPhase.NoRound;
    }

    public sealed record Open : GuessingV1OverlayPresentationState
    {
        public required string RoundName { get; init; }

        public int? GuessCount { get; init; }

        public DateTimeOffset? ClosesAtUtc { get; init; }

        internal override GuessingOverlayPhase Phase => GuessingOverlayPhase.Open;
    }

    public sealed record Closed : GuessingV1OverlayPresentationState
    {
        public required string RoundName { get; init; }

        public int? GuessCount { get; init; }

        public required DateTimeOffset ClosedAtUtc { get; init; }

        internal override GuessingOverlayPhase Phase => GuessingOverlayPhase.Closed;
    }

    public sealed record Completed : GuessingV1OverlayPresentationState
    {
        public required string RoundName { get; init; }

        public int? GuessCount { get; init; }

        public required string WinningAnswer { get; init; }

        public ImmutableArray<string> Winners { get; init; } = [];

        public string? AwardedPointsPerWinner { get; init; }

        public string? PointLabel { get; init; }

        public required DateTimeOffset CompletedAtUtc { get; init; }

        internal override GuessingOverlayPhase Phase => GuessingOverlayPhase.Completed;
    }
}

internal enum GuessingOverlayPhase
{
    NoRound,
    Open,
    Closed,
    Completed,
}

internal enum GiveawayOverlayPhase
{
    Idle,
    Open,
    Ending,
    Completed,
    Cancelled,
}

internal sealed class OverlayServerEpoch
{
    internal Guid Value { get; } = Guid.NewGuid();
}

internal sealed class OverlayStateProvider(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    OverlayServerEpoch serverEpoch,
    TimeProvider timeProvider
) : IOverlayStateProvider
{
    public async Task<OverlaySnapshotProjection> ProjectAsync(
        ResolvedOverlayInstance instance,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (instance is { Type: OverlayType.Empty, Configuration: OverlayConfiguration.EmptyV1 })
        {
            return Empty(instance);
        }

        if (
            instance
                is { Type: OverlayType.CuePlayer, Configuration: OverlayConfiguration.CuePlayerV1 }
            && await OverlayParentEnabledAsync(instance.HostId, cancellationToken)
        )
        {
            return new OverlaySnapshotProjection.CuePlayerV1(
                new CuePlayerV1OverlaySnapshot
                {
                    ServerEpoch = serverEpoch.Value,
                    Sequence = instance.Revision.Value,
                    GeneratedAtUtc = timeProvider.GetUtcNow(),
                }
            );
        }

        if (
            instance
                is {
                    Type: OverlayType.Giveaway,
                    Configuration: OverlayConfiguration.GiveawayV1 giveawayConfiguration,
                }
            && await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.Giveaway,
                cancellationToken
            )
        )
        {
            return await GiveawayAsync(instance, giveawayConfiguration, cancellationToken);
        }

        if (
            instance
                is not {
                    Type: OverlayType.Guessing,
                    Configuration: OverlayConfiguration.GuessingV1 configuration,
                }
            || !await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.Guessing,
                cancellationToken
            )
        )
        {
            return new OverlaySnapshotProjection.Unavailable();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var round = await db
            .Rounds.AsNoTracking()
            .Where(value => value.HostId == instance.HostId)
            .OrderBy(value =>
                value.Status == GuessRoundStatus.Open || value.Status == GuessRoundStatus.Closed
                    ? 0
                    : 1
            )
            .ThenByDescending(value => value.StartedAtUtc)
            .Select(value => new GuessingRoundProjectionRow(
                value.Status,
                value.StartedAtUtc,
                value.ClosedAtUtc,
                value.WinningName,
                value.GuessRoundProfile == null ? string.Empty : value.GuessRoundProfile.Name,
                value.GuessRoundProfile == null
                    ? "0"
                    : value.GuessRoundProfile.WinningGuessPointReward,
                value.Votes.Count,
                value
                    .Votes.Where(vote => vote.GuessName == value.WinningName)
                    .OrderBy(vote => vote.GuessedAtUtc)
                    .Select(vote => vote.Login)
                    .ToArray()
            ))
            .FirstOrDefaultAsync(cancellationToken);
        var pointLabel =
            await db
                .PointsSettings.AsNoTracking()
                .Where(value => value.HostId == instance.HostId)
                .Select(value => value.PointLabel)
                .SingleOrDefaultAsync(cancellationToken)
            ?? "points";

        return Guessing(
            instance,
            configuration,
            round is null
                ? new GuessingV1OverlayPresentationState.NoRound()
                : ToPresentation(round, configuration, pointLabel)
        );
    }

    private async Task<bool> OverlayParentEnabledAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                value =>
                    value.Id == hostId
                    && (value.EnabledFeatures & HostFeatureFlags.Overlays)
                        == HostFeatureFlags.Overlays,
                cancellationToken
            );
    }

    public async Task<OverlaySnapshotProjection> ProjectSampleAsync(
        ResolvedOverlayInstance instance,
        GuessingOverlaySampleState sample,
        CancellationToken cancellationToken
    )
    {
        if (
            instance
                is not {
                    Type: OverlayType.Guessing,
                    Configuration: OverlayConfiguration.GuessingV1 configuration,
                }
            || !await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.Guessing,
                cancellationToken
            )
        )
        {
            return new OverlaySnapshotProjection.Unavailable();
        }

        int? count = configuration.ShowGuessCount ? 42 : null;
        GuessingV1OverlayPresentationState state = sample switch
        {
            GuessingOverlaySampleState.NoRound => new GuessingV1OverlayPresentationState.NoRound(),
            GuessingOverlaySampleState.Open => new GuessingV1OverlayPresentationState.Open
            {
                RoundName = "Which team wins?",
                GuessCount = count,
                ClosesAtUtc = null,
            },
            GuessingOverlaySampleState.Closed => new GuessingV1OverlayPresentationState.Closed
            {
                RoundName = "Which team wins?",
                GuessCount = count,
                ClosedAtUtc = timeProvider.GetUtcNow(),
            },
            GuessingOverlaySampleState.Completed => new GuessingV1OverlayPresentationState.Completed
            {
                RoundName = "Which team wins?",
                GuessCount = count,
                WinningAnswer = "Blue",
                Winners = ["nightowl", "newviewer"],
                AwardedPointsPerWinner = "250",
                PointLabel = "points",
                CompletedAtUtc = timeProvider.GetUtcNow(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };
        return Guessing(instance, configuration, state);
    }

    public async Task<OverlaySnapshotProjection> ProjectSampleAsync(
        ResolvedOverlayInstance instance,
        GiveawayOverlaySampleState sample,
        CancellationToken cancellationToken
    )
    {
        if (
            instance
                is not {
                    Type: OverlayType.Giveaway,
                    Configuration: OverlayConfiguration.GiveawayV1 configuration,
                }
            || !await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.Giveaway,
                cancellationToken
            )
        )
        {
            return new OverlaySnapshotProjection.Unavailable();
        }

        var now = timeProvider.GetUtcNow();
        GiveawayV1OverlayPresentationState state = sample switch
        {
            GiveawayOverlaySampleState.Idle => new GiveawayV1OverlayPresentationState.Idle
            {
                Title = configuration.Title,
            },
            GiveawayOverlaySampleState.Open => new GiveawayV1OverlayPresentationState.Open
            {
                Title = configuration.Title,
                EntrantCount = configuration.ShowEntrantCount ? 42 : null,
                ClosesAtUtc = configuration.ShowCountdown ? now.AddMinutes(3) : null,
                JoinCommand = configuration.ShowJoinCommand ? "!join" : null,
            },
            GiveawayOverlaySampleState.Ending => new GiveawayV1OverlayPresentationState.Ending
            {
                Title = configuration.Title,
                EntrantCount = configuration.ShowEntrantCount ? 42 : null,
            },
            GiveawayOverlaySampleState.Completed => new GiveawayV1OverlayPresentationState.Completed
            {
                Title = configuration.Title,
                Winners =
                [
                    new GiveawayWinnerPresentation { Login = "nightowl", AwardedPoints = "500" },
                    new GiveawayWinnerPresentation { Login = "newviewer", AwardedPoints = "250" },
                ],
                PointLabel = "points",
                CompletedAtUtc = now,
            },
            GiveawayOverlaySampleState.Cancelled => new GiveawayV1OverlayPresentationState.Cancelled
            {
                Title = configuration.Title,
                Message = "Giveaway closed without a winner",
                CompletedAtUtc = now,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };
        return Giveaway(instance, state);
    }

    private OverlaySnapshotProjection Empty(ResolvedOverlayInstance instance) =>
        new OverlaySnapshotProjection.EmptyV1(
            new EmptyV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
            }
        );

    private OverlaySnapshotProjection Guessing(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.GuessingV1 configuration,
        GuessingV1OverlayPresentationState state
    ) =>
        new OverlaySnapshotProjection.GuessingV1(
            new GuessingV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                ResultDurationMilliseconds = checked(configuration.ResultDurationSeconds * 1000),
                State = state,
            }
        );

    private async Task<bool> RequiredFeaturesEnabledAsync(
        int hostId,
        OverlayType type,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value =>
                (value.EnabledFeatures & OverlayRequiredFeatures.For(type))
                == OverlayRequiredFeatures.For(type)
            )
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<OverlaySnapshotProjection> GiveawayAsync(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.GiveawayV1 configuration,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db
            .PointsGiveaways.AsNoTracking()
            .Where(value => value.HostId == instance.HostId)
            .OrderBy(value => value.Status == PointsGiveawayStatus.Active ? 0 : 1)
            .ThenByDescending(value => value.StartedAtUtc)
            .Select(value => new GiveawayProjectionRow(
                value.Status,
                value.EndsAtUtc,
                value.CompletedAtUtc,
                value.Entrants.Count,
                value
                    .Winners.OrderBy(winner => winner.Id)
                    .Select(winner => new GiveawayWinnerPresentation
                    {
                        Login = winner.Login,
                        AwardedPoints = winner.Payout,
                    })
                    .ToArray()
            ))
            .FirstOrDefaultAsync(cancellationToken);
        var joinAlias = configuration.ShowJoinCommand
            ? (
                await db
                    .CommandAliases.AsNoTracking()
                    .Where(value =>
                        value.HostId == instance.HostId
                        && value.Kind == AppCommandKind.Join
                        && value.GuessRoundProfileId == null
                    )
                    .Select(value => value.Alias)
                    .ToArrayAsync(cancellationToken)
            )
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
        var pointLabel =
            await db
                .PointsSettings.AsNoTracking()
                .Where(value => value.HostId == instance.HostId)
                .Select(value => value.PointLabel)
                .SingleOrDefaultAsync(cancellationToken)
            ?? "points";

        var now = timeProvider.GetUtcNow();
        GiveawayV1OverlayPresentationState state = row switch
        {
            null => new GiveawayV1OverlayPresentationState.Idle { Title = configuration.Title },
            { Status: PointsGiveawayStatus.Active } when Utc(row.EndsAtUtc) > now =>
                new GiveawayV1OverlayPresentationState.Open
                {
                    Title = configuration.Title,
                    EntrantCount = configuration.ShowEntrantCount ? row.EntrantCount : null,
                    ClosesAtUtc = configuration.ShowCountdown ? Utc(row.EndsAtUtc) : null,
                    JoinCommand =
                        configuration.ShowJoinCommand && !string.IsNullOrWhiteSpace(joinAlias)
                            ? $"!{joinAlias}"
                            : null,
                },
            { Status: PointsGiveawayStatus.Active } => new GiveawayV1OverlayPresentationState.Ending
            {
                Title = configuration.Title,
                EntrantCount = configuration.ShowEntrantCount ? row.EntrantCount : null,
            },
            { Status: PointsGiveawayStatus.Completed, CompletedAtUtc: { } completedAtUtc } =>
                new GiveawayV1OverlayPresentationState.Completed
                {
                    Title = configuration.Title,
                    Winners = row.Winners.ToImmutableArray(),
                    PointLabel = pointLabel,
                    CompletedAtUtc = Utc(completedAtUtc),
                },
            {
                Status: PointsGiveawayStatus.Cancelled or PointsGiveawayStatus.Expired,
                CompletedAtUtc: { } completedAtUtc,
            } => new GiveawayV1OverlayPresentationState.Cancelled
            {
                Title = configuration.Title,
                Message =
                    row.Status is PointsGiveawayStatus.Cancelled
                        ? "Giveaway cancelled"
                        : "Giveaway closed without a winner",
                CompletedAtUtc = Utc(completedAtUtc),
            },
            _ => throw new PersistenceDataIntegrityException(typeof(PointsGiveaway)),
        };
        return Giveaway(instance, state);
    }

    private OverlaySnapshotProjection Giveaway(
        ResolvedOverlayInstance instance,
        GiveawayV1OverlayPresentationState state
    ) =>
        new OverlaySnapshotProjection.GiveawayV1(
            new GiveawayV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                State = state,
            }
        );

    private static GuessingV1OverlayPresentationState ToPresentation(
        GuessingRoundProjectionRow row,
        OverlayConfiguration.GuessingV1 configuration,
        string pointLabel
    )
    {
        int? count = configuration.ShowGuessCount ? row.GuessCount : null;
        return row.Status switch
        {
            GuessRoundStatus.Open when row.ClosedAtUtc is null && row.WinningName is null =>
                new GuessingV1OverlayPresentationState.Open
                {
                    RoundName = row.RoundName,
                    GuessCount = count,
                    ClosesAtUtc = null,
                },
            GuessRoundStatus.Closed
                when row.ClosedAtUtc is { } closedAtUtc && row.WinningName is null =>
                new GuessingV1OverlayPresentationState.Closed
                {
                    RoundName = row.RoundName,
                    GuessCount = count,
                    ClosedAtUtc = Utc(closedAtUtc),
                },
            GuessRoundStatus.Completed
                when row.ClosedAtUtc is { } completedAtUtc
                    && !string.IsNullOrWhiteSpace(row.WinningName) => Completed(
                row,
                count,
                pointLabel,
                completedAtUtc
            ),
            _ => throw new PersistenceDataIntegrityException(typeof(GuessRound)),
        };
    }

    private static GuessingV1OverlayPresentationState.Completed Completed(
        GuessingRoundProjectionRow row,
        int? guessCount,
        string pointLabel,
        DateTime completedAtUtc
    )
    {
        var reward = PointAmount.ParseAbsolute(row.WinningGuessPointReward);
        var awardedPoints = reward.IsZero ? null : reward.ToDisplayString();
        return new GuessingV1OverlayPresentationState.Completed
        {
            RoundName = row.RoundName,
            GuessCount = guessCount,
            WinningAnswer = row.WinningName!,
            Winners = row.Winners.ToImmutableArray(),
            AwardedPointsPerWinner = awardedPoints,
            PointLabel = awardedPoints is null ? null : pointLabel,
            CompletedAtUtc = Utc(completedAtUtc),
        };
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record GuessingRoundProjectionRow(
        GuessRoundStatus Status,
        DateTime StartedAtUtc,
        DateTime? ClosedAtUtc,
        string? WinningName,
        string RoundName,
        string WinningGuessPointReward,
        int GuessCount,
        string[] Winners
    );

    private sealed record GiveawayProjectionRow(
        PointsGiveawayStatus Status,
        DateTime EndsAtUtc,
        DateTime? CompletedAtUtc,
        int EntrantCount,
        GiveawayWinnerPresentation[] Winners
    );
}

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
    )
    {
        return Task.FromResult<OverlaySnapshotProjection>(
            new OverlaySnapshotProjection.Unavailable()
        );
    }
}

public enum GuessingOverlaySampleState
{
    NoRound,
    Open,
    Closed,
    Completed,
}

public abstract record OverlaySnapshotProjection
{
    private OverlaySnapshotProjection() { }

    public sealed record EmptyV1(EmptyV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record GuessingV1(GuessingV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record CuePlayerV1(CuePlayerV1OverlaySnapshot Snapshot)
        : OverlaySnapshotProjection;

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
                is not {
                    Type: OverlayType.Guessing,
                    Configuration: OverlayConfiguration.GuessingV1 configuration,
                }
            || !await RequiredFeaturesEnabledAsync(instance.HostId, cancellationToken)
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
            || !await RequiredFeaturesEnabledAsync(instance.HostId, cancellationToken)
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

    private OverlaySnapshotProjection Empty(ResolvedOverlayInstance instance)
    {
        return new OverlaySnapshotProjection.EmptyV1(
            new EmptyV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
            }
        );
    }

    private OverlaySnapshotProjection Guessing(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.GuessingV1 configuration,
        GuessingV1OverlayPresentationState state
    )
    {
        return new OverlaySnapshotProjection.GuessingV1(
            new GuessingV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                ResultDurationMilliseconds = checked(configuration.ResultDurationSeconds * 1000),
                State = state,
            }
        );
    }

    private async Task<bool> RequiredFeaturesEnabledAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        const HostFeatureFlags Required = HostFeatureFlags.Overlays | HostFeatureFlags.Guessing;
        return await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value => (value.EnabledFeatures & Required) == Required)
            .SingleOrDefaultAsync(cancellationToken);
    }

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

    private static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

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
}

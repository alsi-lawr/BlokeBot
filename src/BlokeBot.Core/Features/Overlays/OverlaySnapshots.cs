using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.PlayWithViewers;
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

    Task<OverlaySnapshotProjection> ProjectEventFeedSampleAsync(
        ResolvedOverlayInstance instance,
        OverlayEventFeedKind kind,
        CancellationToken cancellationToken
    ) => Task.FromResult<OverlaySnapshotProjection>(new OverlaySnapshotProjection.Unavailable());

    Task<OverlaySnapshotProjection> ProjectSampleAsync(
        ResolvedOverlayInstance instance,
        GiveawayOverlaySampleState sample,
        CancellationToken cancellationToken
    ) => Task.FromResult<OverlaySnapshotProjection>(new OverlaySnapshotProjection.Unavailable());

    Task<OverlaySnapshotProjection> ProjectViewerQueueSampleAsync(
        ResolvedOverlayInstance instance,
        ViewerQueueOverlaySampleState sample,
        CancellationToken cancellationToken
    ) => Task.FromResult<OverlaySnapshotProjection>(new OverlaySnapshotProjection.Unavailable());

    Task<OverlaySnapshotProjection> ProjectProgressSampleAsync(
        ResolvedOverlayInstance instance,
        ProgressOverlaySampleState sample,
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

public enum ViewerQueueOverlaySampleState
{
    Open,
    Closed,
    PartyChanged,
    ReadyOutcome,
    SelectedNext,
}

public enum ProgressOverlaySampleState
{
    Active,
    ProgressUpdate,
    Completed,
    Failed,
    Expired,
    Empty,
}

public abstract record OverlaySnapshotProjection
{
    private OverlaySnapshotProjection() { }

    public sealed record EmptyV1(EmptyV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record GuessingV1(GuessingV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record CuePlayerV1(CuePlayerV1OverlaySnapshot Snapshot)
        : OverlaySnapshotProjection;

    public sealed record GiveawayV1(GiveawayV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record EventFeedV1(EventFeedV1OverlaySnapshot Snapshot)
        : OverlaySnapshotProjection;

    public sealed record ViewerQueueV1(ViewerQueueV1OverlaySnapshot Snapshot)
        : OverlaySnapshotProjection;

    public sealed record CommunityGoalV1(CommunityGoalV1OverlaySnapshot Snapshot)
        : OverlaySnapshotProjection;

    public sealed record ViewerFundedBountyV1(ViewerFundedBountyV1OverlaySnapshot Snapshot)
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

    public OverlayAppearance Appearance { get; init; } = OverlayAppearance.GuessingDefault;

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

    public OverlayAppearance Appearance { get; init; } = OverlayAppearance.GiveawayDefault;

    public required GiveawayV1OverlayPresentationState State { get; init; }
}

public sealed record EventFeedV1OverlaySnapshot
{
    public string OverlayType => "eventFeed";
    public int SchemaVersion => 1;
    public required Guid ServerEpoch { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required string Animation { get; init; }
    public OverlayAppearance Appearance { get; init; } = OverlayAppearance.EventFeedDefault;
    public required EventFeedStatePresentation State { get; init; }
}

public sealed record ViewerQueueV1OverlaySnapshot
{
    public string OverlayType => "viewerQueue";

    public int SchemaVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required string Animation { get; init; }

    public OverlayAppearance Appearance { get; init; } = OverlayAppearance.ViewerQueueDefault;

    public required PlayQueueOverlayState State { get; init; }
}

public sealed record CommunityGoalV1OverlaySnapshot
{
    public string OverlayType => "communityGoal";
    public int SchemaVersion => 1;
    public required Guid ServerEpoch { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required int RotationSeconds { get; init; }
    public required string Animation { get; init; }
    public OverlayAppearance Appearance { get; init; } = OverlayAppearance.CommunityGoalDefault;
    public required ProgressOverlayPresentationState State { get; init; }
}

public sealed record ViewerFundedBountyV1OverlaySnapshot
{
    public string OverlayType => "viewerFundedBounty";
    public int SchemaVersion => 1;
    public required Guid ServerEpoch { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required int RotationSeconds { get; init; }
    public required string Animation { get; init; }
    public OverlayAppearance Appearance { get; init; } =
        OverlayAppearance.ViewerFundedBountyDefault;
    public required ProgressOverlayPresentationState State { get; init; }
}

public sealed record ProgressOverlayPresentationState(
    IReadOnlyList<ProgressOverlayItemPresentation> Items
);

public sealed record ProgressOverlayItemPresentation(
    Guid Id,
    string Context,
    string Title,
    string Current,
    string Target,
    int Percentage,
    DateTimeOffset ExpiresAtUtc,
    ProgressOverlayItemState State,
    int CompletionCount,
    IReadOnlyList<ProgressOverlayContributorPresentation> RecentContributors
);

[JsonConverter(typeof(JsonStringEnumConverter<ProgressOverlayItemState>))]
public enum ProgressOverlayItemState
{
    Active,
    Accepted,
    Completed,
    Failed,
    Expired,
}

public sealed record ProgressOverlayContributorPresentation(string Login, string Amount);

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
    TimeProvider timeProvider,
    OverlayEventFeedService? eventFeed = null,
    IPlayQueueProjectionReader? playQueues = null
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
            instance is
            {
                Type: OverlayType.EventFeed,
                Configuration: OverlayConfiguration.EventFeedV1 eventFeedConfiguration,
            }
        )
        {
            if (eventFeed is null)
            {
                return new OverlaySnapshotProjection.Unavailable();
            }
            var state = await eventFeed.ReadAsync(instance, cancellationToken);
            return state is null
                ? new OverlaySnapshotProjection.Unavailable()
                : new OverlaySnapshotProjection.EventFeedV1(
                    new EventFeedV1OverlaySnapshot
                    {
                        ServerEpoch = serverEpoch.Value,
                        Sequence = instance.Revision.Value,
                        GeneratedAtUtc = timeProvider.GetUtcNow(),
                        Animation = "none",
                        Appearance = eventFeedConfiguration.Appearance,
                        State = state,
                    }
                );
        }

        if (
            instance
                is {
                    Type: OverlayType.ViewerQueue,
                    Configuration: OverlayConfiguration.ViewerQueueV1 viewerQueueConfiguration,
                }
            && playQueues is not null
            && await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.ViewerQueue,
                cancellationToken
            )
        )
        {
            var state = await playQueues.ReadOverlayStateAsync(
                instance.HostId,
                viewerQueueConfiguration.QueueId,
                viewerQueueConfiguration.CurrentRows,
                viewerQueueConfiguration.NextRows,
                cancellationToken
            );
            return state is null
                ? new OverlaySnapshotProjection.Unavailable()
                : ViewerQueue(instance, viewerQueueConfiguration, state, "none");
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
                is {
                    Type: OverlayType.CommunityGoal,
                    Configuration: OverlayConfiguration.CommunityGoalV1 communityGoalConfiguration,
                }
            && await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.CommunityGoal,
                cancellationToken
            )
        )
        {
            return await CommunityGoalAsync(
                instance,
                communityGoalConfiguration,
                cancellationToken
            );
        }

        if (
            instance
                is {
                    Type: OverlayType.ViewerFundedBounty,
                    Configuration: OverlayConfiguration.ViewerFundedBountyV1 bountyConfiguration,
                }
            && await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.ViewerFundedBounty,
                cancellationToken
            )
        )
        {
            return await ViewerFundedBountyAsync(instance, bountyConfiguration, cancellationToken);
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

    public async Task<OverlaySnapshotProjection> ProjectEventFeedSampleAsync(
        ResolvedOverlayInstance instance,
        OverlayEventFeedKind kind,
        CancellationToken cancellationToken
    )
    {
        if (
            instance
                is not {
                    Type: OverlayType.EventFeed,
                    Configuration: OverlayConfiguration.EventFeedV1 configuration
                }
            || !await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayRequiredFeatures.For(OverlayType.EventFeed)
                    | (
                        kind == OverlayEventFeedKind.AchievementCompletion
                            ? HostFeatureFlags.CommunityProgression
                            : HostFeatureFlags.None
                    ),
                cancellationToken
            )
        )
        {
            return new OverlaySnapshotProjection.Unavailable();
        }
        OverlayEventPresentation sample = kind switch
        {
            OverlayEventFeedKind.PointAward => new OverlayEventPresentation.PointAward
            {
                HostId = instance.HostId,
                SourceKey = "sample-point",
                Recipient = "nightowl",
                Amount = "250",
                PointLabel = "points",
            },
            OverlayEventFeedKind.GuessingWinner => new OverlayEventPresentation.GuessingWinner
            {
                HostId = instance.HostId,
                SourceKey = "sample-guess",
                RoundName = "Which team wins?",
                WinningAnswer = "Blue",
                Winners = ["nightowl", "newviewer"],
                Amount = "250",
                PointLabel = "points",
            },
            OverlayEventFeedKind.GiveawayWinner => new OverlayEventPresentation.GiveawayWinner
            {
                HostId = instance.HostId,
                SourceKey = "sample-giveaway",
                Winners = ["nightowl", "newviewer"],
                Prizes = ["500 points", "250 points"],
                PointLabel = "points",
            },
            OverlayEventFeedKind.BingoEvent => new OverlayEventPresentation.BingoEvent
            {
                HostId = instance.HostId,
                SourceKey = "sample-bingo",
                Summary = "Team Nebula completed row 2",
            },
            OverlayEventFeedKind.AchievementCompletion =>
                new OverlayEventPresentation.AchievementCompletion
                {
                    HostId = instance.HostId,
                    SourceKey = "sample-achievement",
                    Viewer = "NightOwl",
                    Achievement = "Community trailblazer",
                    Rewards = "250 points, Trailblazer",
                },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var kindConfiguration = configuration.Kinds[kind];
        var now = timeProvider.GetUtcNow();
        return new OverlaySnapshotProjection.EventFeedV1(
            new EventFeedV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = now,
                Animation = "sample",
                Appearance = configuration.Appearance,
                State = new EventFeedStatePresentation(
                    new EventFeedCardPresentation(
                        0,
                        PersistedEnumTokens<OverlayEventFeedKind>.Format(kind),
                        PersistedEnumTokens<OverlayEventFeedPriority>.Format(
                            kindConfiguration.Priority
                        ),
                        kind switch
                        {
                            OverlayEventFeedKind.PointAward => "Points awarded",
                            OverlayEventFeedKind.GuessingWinner => "Guessing winner",
                            OverlayEventFeedKind.GiveawayWinner => "Giveaway winner",
                            OverlayEventFeedKind.BingoEvent => "Bingo",
                            _ => "Achievement unlocked",
                        },
                        EventFeedProjectionText.DecodeOnce(
                            EventFeedTemplateRenderer.Render(kindConfiguration, sample)
                        ),
                        now,
                        now.AddSeconds(kindConfiguration.DurationSeconds)
                    ),
                    []
                ),
            }
        );
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
        return Giveaway(instance, configuration, state);
    }

    public async Task<OverlaySnapshotProjection> ProjectViewerQueueSampleAsync(
        ResolvedOverlayInstance instance,
        ViewerQueueOverlaySampleState sample,
        CancellationToken cancellationToken
    )
    {
        if (
            instance
                is not {
                    Type: OverlayType.ViewerQueue,
                    Configuration: OverlayConfiguration.ViewerQueueV1 configuration,
                }
            || !await RequiredFeaturesEnabledAsync(
                instance.HostId,
                OverlayType.ViewerQueue,
                cancellationToken
            )
        )
        {
            return new OverlaySnapshotProjection.Unavailable();
        }

        var fieldValues = new[]
        {
            new PlayQueueEntryFieldView("platform", "Platform", "PC"),
            new PlayQueueEntryFieldView("preferred-role", "Preferred role", "Support"),
        };
        var state = new PlayQueueOverlayState(
            "Community games",
            "Co-op night",
            sample is not ViewerQueueOverlaySampleState.Closed,
            18,
            new[]
            {
                new PlayQueueOverlayEntry("nightowl", fieldValues),
                new PlayQueueOverlayEntry("newviewer", fieldValues),
            }
                .Take(configuration.CurrentRows)
                .ToArray(),
            new[]
            {
                new PlayQueueOverlayEntry("playerthree", fieldValues),
                new PlayQueueOverlayEntry("playerfour", fieldValues),
                new PlayQueueOverlayEntry("playerfive", fieldValues),
            }
                .Take(configuration.NextRows)
                .ToArray()
        );
        return ViewerQueue(instance, configuration, state, "none");
    }

    public async Task<OverlaySnapshotProjection> ProjectProgressSampleAsync(
        ResolvedOverlayInstance instance,
        ProgressOverlaySampleState sample,
        CancellationToken cancellationToken
    )
    {
        if (!await RequiredFeaturesEnabledAsync(instance.HostId, instance.Type, cancellationToken))
        {
            return new OverlaySnapshotProjection.Unavailable();
        }

        var now = timeProvider.GetUtcNow();
        var item =
            sample is ProgressOverlaySampleState.Empty
                ? Array.Empty<ProgressOverlayItemPresentation>()
                :
                [
                    new ProgressOverlayItemPresentation(
                        Guid.Parse("a43d6ff0-7f88-4c37-8118-a09f9ee795d8"),
                        instance.Type is OverlayType.CommunityGoal
                            ? "Season 3"
                            : "Viewer challenge",
                        instance.Type is OverlayType.CommunityGoal
                            ? "Unlock the community showcase"
                            : "Beat the midnight gauntlet",
                        sample is ProgressOverlaySampleState.Completed ? "20000" : "13640",
                        "20000",
                        sample is ProgressOverlaySampleState.Completed ? 100 : 68,
                        now.AddDays(sample is ProgressOverlaySampleState.Expired ? -1 : 12),
                        SampleState(sample),
                        sample is ProgressOverlaySampleState.Completed ? 1 : 0,
                        instance.Type is OverlayType.ViewerFundedBounty
                            ? new[]
                            {
                                new ProgressOverlayContributorPresentation("pixeljay", "500"),
                                new ProgressOverlayContributorPresentation("mossybyte", "250"),
                                new ProgressOverlayContributorPresentation("lumen", "100"),
                            }
                            : []
                    ),
                ];

        return instance.Configuration switch
        {
            OverlayConfiguration.CommunityGoalV1 configuration => CommunityGoal(
                instance,
                configuration,
                item,
                sample is ProgressOverlaySampleState.ProgressUpdate
                    ? "progress"
                    : SampleAnimation(sample)
            ),
            OverlayConfiguration.ViewerFundedBountyV1 configuration => ViewerFundedBounty(
                instance,
                configuration,
                item,
                sample is ProgressOverlaySampleState.ProgressUpdate
                    ? "progress"
                    : SampleAnimation(sample)
            ),
            _ => new OverlaySnapshotProjection.Unavailable(),
        };
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
                Appearance = configuration.Appearance,
                State = state,
            }
        );

    private async Task<OverlaySnapshotProjection> CommunityGoalAsync(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.CommunityGoalV1 configuration,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var definitions = await db
            .CommunityDefinitions.AsNoTracking()
            .Where(value =>
                value.HostId == instance.HostId
                && value.Scope == CommunityProgressScope.Communal
                && value.Season.Visibility == CommunityVisibility.Public
                && (
                    configuration.SelectedItemId == null
                        ? value.Season.Status == CommunitySeasonStatus.Open
                        : value.PublicId == configuration.SelectedItemId
                            && value.Season.Status != CommunitySeasonStatus.Draft
                )
            )
            .OrderBy(value => value.Season.EndsAtUtc)
            .ThenBy(value => value.Id)
            .Take(12)
            .Select(value => new CommunityGoalProjectionRow(
                value.Id,
                value.PublicId,
                value.Season.Name,
                value.Name,
                value.Target,
                value.Season.Status,
                value.Season.EndsAtUtc
            ))
            .ToArrayAsync(cancellationToken);
        var definitionIds = definitions.Select(value => value.Id).ToArray();
        var progress = await db
            .CommunityProgress.AsNoTracking()
            .Where(value =>
                value.HostId == instance.HostId
                && definitionIds.Contains(value.DefinitionId)
                && value.ViewerTwitchUserId == null
            )
            .ToDictionaryAsync(
                value => value.DefinitionId,
                value => new { value.Amount, value.CompletionCount },
                cancellationToken
            );
        var now = timeProvider.GetUtcNow();
        var items = definitions
            .Select(value =>
            {
                var progressValue = progress.GetValueOrDefault(value.Id);
                var amount = progressValue?.Amount ?? 0;
                var state =
                    amount >= value.Target ? ProgressOverlayItemState.Completed
                    : value.SeasonStatus == CommunitySeasonStatus.Open
                    && value.ExpiresAtUtc > now.UtcDateTime
                        ? ProgressOverlayItemState.Active
                    : ProgressOverlayItemState.Expired;
                return new ProgressOverlayItemPresentation(
                    value.PublicId,
                    value.SeasonName,
                    value.Title,
                    amount.ToString(CultureInfo.InvariantCulture),
                    value.Target.ToString(CultureInfo.InvariantCulture),
                    Percentage(amount, value.Target),
                    Utc(value.ExpiresAtUtc),
                    state,
                    progressValue?.CompletionCount ?? 0,
                    []
                );
            })
            .ToArray();
        return CommunityGoal(instance, configuration, items, "none");
    }

    private async Task<OverlaySnapshotProjection> ViewerFundedBountyAsync(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.ViewerFundedBountyV1 configuration,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var bounties = await db
            .Bounties.AsNoTracking()
            .Where(value =>
                value.HostId == instance.HostId
                && value.Visibility == BountyVisibility.Public
                && (
                    configuration.SelectedItemId == null
                        ? value.Status != BountyStatus.Proposed
                            && value.Status != BountyStatus.Cancelled
                        : value.PublicId == configuration.SelectedItemId
                            && value.Status != BountyStatus.Proposed
                            && value.Status != BountyStatus.Cancelled
                )
            )
            .OrderBy(value =>
                value.Status == BountyStatus.Funding || value.Status == BountyStatus.Accepted
                    ? 0
                    : 1
            )
            .ThenBy(value => value.ExpiresAtUtc)
            .ThenByDescending(value => value.UpdatedAtUtc)
            .Take(12)
            .Select(value => new BountyOverlayProjectionRow(
                value.Id,
                value.PublicId,
                value.Title,
                value.PledgedAmount,
                value.FundingTarget,
                value.Status,
                value.ExpiresAtUtc
            ))
            .ToArrayAsync(cancellationToken);
        var items = new List<ProgressOverlayItemPresentation>(bounties.Length);
        foreach (var bounty in bounties)
        {
            var recent =
                configuration.RecentContributorCount == 0
                    ? []
                    : await db
                        .BountyPledges.AsNoTracking()
                        .Where(value =>
                            value.HostId == instance.HostId && value.BountyId == bounty.Id
                        )
                        .OrderByDescending(value => value.CreatedAtUtc)
                        .ThenByDescending(value => value.Id)
                        .Take(configuration.RecentContributorCount)
                        .Select(value => new ProgressOverlayContributorPresentation(
                            value.ContributorLogin,
                            value.Amount
                        ))
                        .ToArrayAsync(cancellationToken);
            var current = BigInteger.Parse(bounty.Current, CultureInfo.InvariantCulture);
            var target = BigInteger.Parse(bounty.Target, CultureInfo.InvariantCulture);
            items.Add(
                new ProgressOverlayItemPresentation(
                    bounty.PublicId,
                    "Viewer-funded bounty",
                    bounty.Title,
                    bounty.Current,
                    bounty.Target,
                    Percentage(current, target),
                    Utc(bounty.ExpiresAtUtc),
                    BountyState(bounty.Status),
                    0,
                    recent
                )
            );
        }
        return ViewerFundedBounty(instance, configuration, items, "none");
    }

    private OverlaySnapshotProjection CommunityGoal(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.CommunityGoalV1 configuration,
        IReadOnlyList<ProgressOverlayItemPresentation> items,
        string animation
    ) =>
        new OverlaySnapshotProjection.CommunityGoalV1(
            new CommunityGoalV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                RotationSeconds = configuration.RotationSeconds,
                Animation = animation,
                Appearance = configuration.Appearance,
                State = new ProgressOverlayPresentationState(items),
            }
        );

    private OverlaySnapshotProjection ViewerFundedBounty(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.ViewerFundedBountyV1 configuration,
        IReadOnlyList<ProgressOverlayItemPresentation> items,
        string animation
    ) =>
        new OverlaySnapshotProjection.ViewerFundedBountyV1(
            new ViewerFundedBountyV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                RotationSeconds = configuration.RotationSeconds,
                Animation = animation,
                Appearance = configuration.Appearance,
                State = new ProgressOverlayPresentationState(items),
            }
        );

    private static ProgressOverlayItemState SampleState(ProgressOverlaySampleState sample) =>
        sample switch
        {
            ProgressOverlaySampleState.Completed => ProgressOverlayItemState.Completed,
            ProgressOverlaySampleState.Failed => ProgressOverlayItemState.Failed,
            ProgressOverlaySampleState.Expired => ProgressOverlayItemState.Expired,
            _ => ProgressOverlayItemState.Active,
        };

    private static string SampleAnimation(ProgressOverlaySampleState sample) =>
        sample switch
        {
            ProgressOverlaySampleState.Completed => "complete",
            ProgressOverlaySampleState.Failed or ProgressOverlaySampleState.Expired =>
                "statusChange",
            _ => "none",
        };

    private static ProgressOverlayItemState BountyState(BountyStatus status) =>
        status switch
        {
            BountyStatus.Accepted => ProgressOverlayItemState.Accepted,
            BountyStatus.Completed => ProgressOverlayItemState.Completed,
            BountyStatus.Failed => ProgressOverlayItemState.Failed,
            BountyStatus.Expired => ProgressOverlayItemState.Expired,
            _ => ProgressOverlayItemState.Active,
        };

    private static int Percentage(long current, long target) =>
        Percentage(new BigInteger(current), new BigInteger(target));

    private static int Percentage(BigInteger current, BigInteger target) =>
        target <= 0
            ? current > 0
                ? 100
                : 0
            : (int)BigInteger.Min(100, BigInteger.Max(0, current * 100 / target));

    private async Task<bool> RequiredFeaturesEnabledAsync(
        int hostId,
        OverlayType type,
        CancellationToken cancellationToken
    ) =>
        await RequiredFeaturesEnabledAsync(
            hostId,
            OverlayRequiredFeatures.For(type),
            cancellationToken
        );

    private async Task<bool> RequiredFeaturesEnabledAsync(
        int hostId,
        HostFeatureFlags requiredFeatures,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value => (value.EnabledFeatures & requiredFeatures) == requiredFeatures)
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
        return Giveaway(instance, configuration, state);
    }

    private OverlaySnapshotProjection Giveaway(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.GiveawayV1 configuration,
        GiveawayV1OverlayPresentationState state
    ) =>
        new OverlaySnapshotProjection.GiveawayV1(
            new GiveawayV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                Appearance = configuration.Appearance,
                State = state,
            }
        );

    private OverlaySnapshotProjection ViewerQueue(
        ResolvedOverlayInstance instance,
        OverlayConfiguration.ViewerQueueV1 configuration,
        PlayQueueOverlayState state,
        string animation
    ) =>
        new OverlaySnapshotProjection.ViewerQueueV1(
            new ViewerQueueV1OverlaySnapshot
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = timeProvider.GetUtcNow(),
                Animation = animation,
                Appearance = configuration.Appearance,
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

    private sealed record CommunityGoalProjectionRow(
        long Id,
        Guid PublicId,
        string SeasonName,
        string Title,
        long Target,
        CommunitySeasonStatus SeasonStatus,
        DateTime ExpiresAtUtc
    );

    private sealed record BountyOverlayProjectionRow(
        long Id,
        Guid PublicId,
        string Title,
        string Current,
        string Target,
        BountyStatus Status,
        DateTime ExpiresAtUtc
    );
}

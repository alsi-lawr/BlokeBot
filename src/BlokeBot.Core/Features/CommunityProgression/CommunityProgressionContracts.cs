using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CommunityProgression;

public readonly record struct CommunitySeasonId(Guid Value);

public readonly record struct CommunityDefinitionId(Guid Value);

public readonly record struct CommunityRewardId(Guid Value);

public readonly record struct CommunityDefinitionKey
{
    public CommunityDefinitionKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }
}

public sealed record CommunityActor(string TwitchUserId, string Login);

public sealed record CommunityViewer(string TwitchUserId, string Login, string DisplayName);

public sealed record CommunitySeasonDraft(
    Guid OperationId,
    string Name,
    string Description,
    string ModeratorNotes,
    CommunityVisibility Visibility,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    CommunityActor Actor
);

public sealed record CommunityRewardDraft(
    Guid OperationId,
    CommunitySeasonId SeasonId,
    string Key,
    CommunityRewardKind Kind,
    string Name,
    string PresentationToken,
    CommunityActor Actor
);

public sealed record CommunityResetSchedule(
    CommunityResetCadence Cadence,
    TimeOnly LocalTime,
    DayOfWeek? Weekday
)
{
    public static CommunityResetSchedule None { get; } =
        new(CommunityResetCadence.None, TimeOnly.MinValue, null);
}

public sealed record CommunityDefinitionDraft(
    Guid OperationId,
    CommunitySeasonId SeasonId,
    string Key,
    string Name,
    string Description,
    CommunityDefinitionKind Kind,
    CommunityProgressScope Scope,
    CommunityCompletionMode CompletionMode,
    CommunityEventRuleKind EventRule,
    CommunityProgressIncrement Increment,
    string? FilterToken,
    long Target,
    PointAmount PointsReward,
    CommunityResetSchedule ResetSchedule,
    IReadOnlyList<CommunityRewardId> Rewards,
    CommunityActor Actor
);

public enum CommunitySeasonTransition
{
    Open,
    Close,
    Archive,
}

public sealed record CommunitySeasonTransitionCommand(
    Guid OperationId,
    CommunitySeasonId SeasonId,
    long ExpectedRevision,
    CommunitySeasonTransition Transition,
    CommunityActor Actor,
    string PrivateNote
);

public sealed record CommunityScheduleEditCommand(
    Guid OperationId,
    CommunityDefinitionId DefinitionId,
    CommunityResetSchedule Schedule,
    bool ConfirmActiveProgressReset,
    CommunityActor Actor,
    string PrivateNote
);

public abstract record CommunitySourceEvent
{
    private CommunitySourceEvent() { }

    public abstract CommunityEventRuleKind Kind { get; }
    public abstract string SourceEventId { get; }
    public abstract CommunityViewer? Viewer { get; }
    public abstract long Value { get; }
    public abstract string? FilterToken { get; }
    public abstract DateTimeOffset OccurredAtUtc { get; }

    public sealed record ChatMessage(
        string MessageId,
        CommunityViewer Chatter,
        DateTimeOffset OccurredAt
    ) : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.ChatMessage;
        public override string SourceEventId => MessageId;
        public override CommunityViewer Viewer => Chatter;
        public override long Value => 1;
        public override string? FilterToken => null;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }

    public sealed record Follow(
        string MessageId,
        CommunityViewer Follower,
        DateTimeOffset OccurredAt
    ) : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.Follow;
        public override string SourceEventId => MessageId;
        public override CommunityViewer Viewer => Follower;
        public override long Value => 1;
        public override string? FilterToken => null;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }

    public sealed record Subscription(
        string MessageId,
        CommunityViewer Subscriber,
        string Tier,
        DateTimeOffset OccurredAt
    ) : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.Subscription;
        public override string SourceEventId => MessageId;
        public override CommunityViewer Viewer => Subscriber;
        public override long Value => 1;
        public override string FilterToken => Tier;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }

    public sealed record Cheer(
        string MessageId,
        CommunityViewer? Cheerer,
        int Bits,
        DateTimeOffset OccurredAt
    ) : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.Cheer;
        public override string SourceEventId => MessageId;
        public override CommunityViewer? Viewer => Cheerer;
        public override long Value => Bits;
        public override string? FilterToken => null;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }

    public sealed record IncomingRaid(
        string MessageId,
        CommunityViewer Raider,
        int ViewerCount,
        DateTimeOffset OccurredAt
    ) : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.IncomingRaid;
        public override string SourceEventId => MessageId;
        public override CommunityViewer Viewer => Raider;
        public override long Value => ViewerCount;
        public override string? FilterToken => null;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }

    public sealed record RewardRedemption(
        string MessageId,
        CommunityViewer Redeemer,
        string RewardId,
        DateTimeOffset OccurredAt
    ) : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.RewardRedemption;
        public override string SourceEventId => MessageId;
        public override CommunityViewer Viewer => Redeemer;
        public override long Value => 1;
        public override string FilterToken => RewardId;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }

    public sealed record BountyCompleted(string BountyId, DateTimeOffset OccurredAt)
        : CommunitySourceEvent
    {
        public override CommunityEventRuleKind Kind => CommunityEventRuleKind.BountyCompleted;
        public override string SourceEventId => BountyId;
        public override CommunityViewer? Viewer => null;
        public override long Value => 1;
        public override string? FilterToken => null;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
    }
}

public sealed record CommunityExternalGrantRequest(
    int HostId,
    string Source,
    string IdempotencyKey,
    CommunityDefinitionKey AchievementKey,
    CommunityViewer Viewer,
    DateTimeOffset OccurredAtUtc
);

public interface ICommunityAchievementGrantService
{
    Task<CommunityExternalGrantOutcome> GrantAsync(
        CommunityExternalGrantRequest request,
        CancellationToken cancellationToken
    );
}

public abstract record CommunityExternalGrantOutcome
{
    private CommunityExternalGrantOutcome() { }

    public sealed record Granted(Guid CompletionId, bool WasIdempotent)
        : CommunityExternalGrantOutcome;

    public sealed record FeatureDisabled : CommunityExternalGrantOutcome;

    public sealed record AchievementNotFound : CommunityExternalGrantOutcome;

    public sealed record AchievementUnavailable : CommunityExternalGrantOutcome;

    public sealed record Conflict : CommunityExternalGrantOutcome;

    public sealed record Invalid(string Message) : CommunityExternalGrantOutcome;
}

public abstract record CommunityOperationOutcome
{
    private CommunityOperationOutcome() { }

    public sealed record Succeeded(bool WasIdempotent = false) : CommunityOperationOutcome;

    public sealed record FeatureDisabled : CommunityOperationOutcome;

    public sealed record NotFound : CommunityOperationOutcome;

    public sealed record Conflict(string Message) : CommunityOperationOutcome;

    public sealed record Invalid(string Message) : CommunityOperationOutcome;
}

public sealed record CommunitySeasonView(
    CommunitySeasonId Id,
    string Name,
    string Description,
    CommunitySeasonStatus Status,
    CommunityVisibility Visibility,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    long Revision,
    IReadOnlyList<CommunityDefinitionView> Definitions,
    IReadOnlyList<CommunityRewardView> Rewards
);

public sealed record CommunityDefinitionView(
    CommunityDefinitionId Id,
    string Key,
    string Name,
    CommunityDefinitionKind Kind,
    CommunityProgressScope Scope,
    CommunityCompletionMode CompletionMode,
    CommunityEventRuleKind EventRule,
    CommunityProgressIncrement Increment,
    long Target,
    PointAmount PointsReward,
    CommunityResetSchedule Schedule,
    string TimeZoneId,
    DateTimeOffset? NextResetUtc
);

public sealed record CommunityRewardView(
    CommunityRewardId Id,
    string Key,
    CommunityRewardKind Kind,
    string Name,
    string PresentationToken
);

public sealed record CommunityStandingView(
    int Rank,
    string TwitchUserId,
    string Login,
    string DisplayName,
    int CompletedCount,
    long ProgressAmount
);

public sealed record CommunityViewerProgressView(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string DefinitionName,
    CommunityDefinitionKind DefinitionKind,
    long Amount,
    long Target,
    int CompletionCount,
    string? PeriodKey
);

public sealed record CommunityCompletionView(
    Guid Id,
    string? TwitchUserId,
    string? Login,
    string? DisplayName,
    string DefinitionName,
    CommunityDefinitionKind DefinitionKind,
    DateTime CompletedAtUtc,
    string RewardSnapshot
);

public sealed record CommunityUnlockView(
    string TwitchUserId,
    string Login,
    CommunityRewardKind Kind,
    string Name,
    string PresentationToken,
    DateTime GrantedAtUtc,
    bool Equipped
);

public sealed record CommunityPublicSeasonView(
    CommunitySeasonId Id,
    string Name,
    string Description,
    CommunitySeasonStatus Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    IReadOnlyList<CommunityStandingView> Standings,
    IReadOnlyList<CommunityViewerProgressView> Progress,
    IReadOnlyList<CommunityCompletionView> Completions,
    IReadOnlyList<CommunityUnlockView> Unlocks
);

public sealed record CommunityPublicView(
    string HostLogin,
    IReadOnlyList<CommunityPublicSeasonView> Seasons
);

public sealed record CommunityEquipCommand(
    Guid OperationId,
    int HostId,
    CommunityViewer Viewer,
    CommunityRewardKind Kind,
    string RewardKey
);

public static class CommunityPresentationCatalog
{
    public static IReadOnlySet<string> BadgeIcons { get; } =
        new HashSet<string>(["star", "crown", "spark", "shield"], StringComparer.Ordinal);

    public static IReadOnlySet<string> CosmeticAccents { get; } =
        new HashSet<string>(["sky", "violet", "amber", "emerald"], StringComparer.Ordinal);

    public static bool Supports(CommunityRewardKind kind, string token) =>
        kind switch
        {
            CommunityRewardKind.Title => !string.IsNullOrWhiteSpace(token),
            CommunityRewardKind.Badge => BadgeIcons.Contains(token),
            CommunityRewardKind.CosmeticAccent => CosmeticAccents.Contains(token),
            _ => false,
        };
}

public sealed record CommunityEventRuleDescriptor(
    CommunityEventRuleKind Kind,
    bool SupportsViewerProgress,
    bool SupportsCommunalProgress,
    bool SupportsEventValue,
    bool SupportsFilter
);

public static class CommunityEventRuleCatalog
{
    private static readonly IReadOnlyDictionary<
        CommunityEventRuleKind,
        CommunityEventRuleDescriptor
    > _rules = new CommunityEventRuleDescriptor[]
    {
        new(CommunityEventRuleKind.ChatMessage, true, true, false, false),
        new(CommunityEventRuleKind.Follow, true, true, false, false),
        new(CommunityEventRuleKind.Subscription, true, true, false, true),
        new(CommunityEventRuleKind.Cheer, true, true, true, false),
        new(CommunityEventRuleKind.IncomingRaid, true, true, true, false),
        new(CommunityEventRuleKind.RewardRedemption, true, true, false, true),
        new(CommunityEventRuleKind.BountyCompleted, false, true, false, false),
        new(CommunityEventRuleKind.ExternalGrant, true, false, false, false),
    }.ToDictionary(value => value.Kind);

    public static IEnumerable<CommunityEventRuleDescriptor> Rules => _rules.Values;

    public static CommunityEventRuleDescriptor Describe(CommunityEventRuleKind kind) =>
        _rules[kind];
}

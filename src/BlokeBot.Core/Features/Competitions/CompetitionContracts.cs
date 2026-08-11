using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Competitions;

public readonly record struct CompetitionId(Guid Value);

public readonly record struct CompetitionEntrantId(Guid Value);

public readonly record struct CompetitionMatchId(Guid Value);

public sealed record CompetitionActor(string TwitchUserId, string Login);

public sealed record CompetitionMember(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string PrivateContact
);

public sealed record CompetitionMilestoneRewardDraft(
    int WinsRequired,
    PointAmount Points,
    string AchievementKey
);

public sealed record CompetitionDraft(
    Guid OperationId,
    string Name,
    string Description,
    CompetitionFormat Format,
    CompetitionEntryKind EntryKind,
    CompetitionSeeding Seeding,
    CompetitionTiebreak Tiebreak,
    int Capacity,
    int TeamSize,
    PointAmount MinimumPoints,
    int WinPoints,
    int DrawPoints,
    int LossPoints,
    string Seed,
    int ReminderHoursBefore,
    string ReminderMessage,
    PointAmount WinnerPoints,
    PointAmount RunnerUpPoints,
    string WinnerAchievementKey,
    string RunnerUpAchievementKey,
    IReadOnlyList<CompetitionMilestoneRewardDraft> MilestoneRewards,
    string PrivateLobbyInformation,
    CompetitionActor Actor,
    string PrivateReason
);

public sealed record CompetitionRegistration(
    Guid OperationId,
    CompetitionId CompetitionId,
    string Name,
    int? SeedRank,
    IReadOnlyList<CompetitionMember> Members,
    CompetitionActor Actor,
    string PrivateReason
);

public sealed record CompetitionTransition(
    Guid OperationId,
    CompetitionId CompetitionId,
    long ExpectedRevision,
    CompetitionActor Actor,
    string PrivateReason
);

public sealed record CompetitionResultCommand(
    Guid OperationId,
    CompetitionId CompetitionId,
    CompetitionMatchId MatchId,
    long ExpectedRevision,
    int ScoreA,
    int ScoreB,
    CompetitionActor Actor,
    string PrivateReason
);

public abstract record CompetitionOutcome
{
    private CompetitionOutcome() { }

    public sealed record Succeeded(bool WasIdempotent = false) : CompetitionOutcome;

    public sealed record FeatureDisabled : CompetitionOutcome;

    public sealed record NotFound : CompetitionOutcome;

    public sealed record Conflict(string Message) : CompetitionOutcome;

    public sealed record Invalid(string Message) : CompetitionOutcome;
}

public sealed record CompetitionMemberView(string Login, string DisplayName);

public sealed record CompetitionEntrantView(
    CompetitionEntrantId Id,
    string Name,
    int? SeedRank,
    IReadOnlyList<CompetitionMemberView> Members
);

public sealed record CompetitionMatchView(
    CompetitionMatchId Id,
    int Round,
    int Position,
    CompetitionEntrantId? EntrantAId,
    CompetitionEntrantId? EntrantBId,
    string EntrantA,
    string EntrantB,
    int? ScoreA,
    int? ScoreB,
    CompetitionMatchStatus Status,
    DateTime? ScheduledAtUtc
);

public sealed record CompetitionStandingView(
    int Rank,
    CompetitionEntrantId EntrantId,
    string Name,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int ScoreFor,
    int ScoreAgainst,
    int Points
);

public sealed record CompetitionAuditView(
    CompetitionAuditAction Action,
    string ActorLogin,
    string PrivateReason,
    DateTime OccurredAtUtc
);

public sealed record CompetitionMilestoneRewardView(
    int WinsRequired,
    PointAmount Points,
    string AchievementKey
);

public sealed record CompetitionView(
    CompetitionId Id,
    string HostLogin,
    string Name,
    string Description,
    CompetitionFormat Format,
    CompetitionEntryKind EntryKind,
    CompetitionStatus Status,
    CompetitionSeeding Seeding,
    CompetitionTiebreak Tiebreak,
    int Capacity,
    int TeamSize,
    string Seed,
    string AlgorithmVersion,
    int WinPoints,
    int DrawPoints,
    int LossPoints,
    long Revision,
    IReadOnlyList<CompetitionEntrantView> Entrants,
    IReadOnlyList<CompetitionMatchView> Matches,
    IReadOnlyList<CompetitionStandingView> Standings,
    DateTime? CompletedAtUtc,
    DateTime? ArchivedAtUtc
);

public sealed record CompetitionModeratorView(
    CompetitionView Competition,
    string PrivateLobbyInformation,
    PointAmount MinimumPoints,
    PointAmount WinnerPoints,
    PointAmount RunnerUpPoints,
    string WinnerAchievementKey,
    string RunnerUpAchievementKey,
    IReadOnlyList<CompetitionMilestoneRewardView> MilestoneRewards,
    int ReminderHoursBefore,
    string ReminderMessage,
    IReadOnlyList<CompetitionAuditView> Audit
);

public sealed record CompetitionPublicBoard(
    string HostLogin,
    IReadOnlyList<CompetitionView> Active,
    IReadOnlyList<CompetitionView> Archive
);

public sealed record CompetitionLifecycleEvent(
    Guid OccurrenceId,
    int HostId,
    CompetitionId CompetitionId,
    CompetitionEventKind Kind,
    string PublicPayload,
    DateTimeOffset OccurredAtUtc
);

public interface ICompetitionLifecycleObserver
{
    ValueTask CompetitionChangedAsync(
        CompetitionLifecycleEvent competitionEvent,
        CancellationToken cancellationToken
    );
}

public sealed record CompetitionReminderRecipient(string Login, string TwitchUserId);

public interface ICompetitionReminderDelivery
{
    Task<bool> DeliverAsync(
        CompetitionReminderRequest request,
        CancellationToken cancellationToken
    );
}

public sealed record CompetitionReminderRequest(
    int HostId,
    string HostLogin,
    DateTime ReminderDueAtUtc,
    string Message,
    IReadOnlyList<CompetitionReminderRecipient> Recipients
);

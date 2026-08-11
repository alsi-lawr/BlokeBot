namespace BlokeBot.Persistence.Models;

public enum CompetitionFormat
{
    [PersistedToken("Tournament")]
    Tournament,

    [PersistedToken("RoundRobin")]
    RoundRobin,

    [PersistedToken("PredictionLeague")]
    PredictionLeague,
}

public enum CompetitionEntryKind
{
    [PersistedToken("Individual")]
    Individual,

    [PersistedToken("Team")]
    Team,
}

public enum CompetitionStatus
{
    [PersistedToken("Draft")]
    Draft,

    [PersistedToken("Registration")]
    Registration,

    [PersistedToken("Running")]
    Running,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Archived")]
    Archived,
}

public enum CompetitionSeeding
{
    [PersistedToken("Seeded")]
    Seeded,

    [PersistedToken("Random")]
    Random,
}

public enum CompetitionTiebreak
{
    [PersistedToken("ScoreDifferenceThenScoreFor")]
    ScoreDifferenceThenScoreFor,

    [PersistedToken("ScoreForThenWins")]
    ScoreForThenWins,
}

public enum CompetitionMatchStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Confirmed")]
    Confirmed,
}

public enum CompetitionAuditAction
{
    [PersistedToken("Created")]
    Created,

    [PersistedToken("RegistrationOpened")]
    RegistrationOpened,

    [PersistedToken("EntrantRegistered")]
    EntrantRegistered,

    [PersistedToken("Started")]
    Started,

    [PersistedToken("ResultConfirmed")]
    ResultConfirmed,

    [PersistedToken("ResultCorrected")]
    ResultCorrected,

    [PersistedToken("DownstreamReset")]
    DownstreamReset,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Archived")]
    Archived,
}

public enum CompetitionEventKind
{
    [PersistedToken("Created")]
    Created,

    [PersistedToken("RegistrationOpened")]
    RegistrationOpened,

    [PersistedToken("EntrantRegistered")]
    EntrantRegistered,

    [PersistedToken("Started")]
    Started,

    [PersistedToken("ResultConfirmed")]
    ResultConfirmed,

    [PersistedToken("ResultCorrected")]
    ResultCorrected,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Archived")]
    Archived,

    [PersistedToken("RewardsGranted")]
    RewardsGranted,
}

public sealed class Competition
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public Guid CreationOperationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CompetitionFormat Format { get; set; }
    public CompetitionEntryKind EntryKind { get; set; }
    public CompetitionStatus Status { get; set; }
    public CompetitionSeeding Seeding { get; set; }
    public CompetitionTiebreak Tiebreak { get; set; }
    public int Capacity { get; set; }
    public int TeamSize { get; set; }
    public string MinimumPoints { get; set; } = "0";
    public int WinPoints { get; set; }
    public int DrawPoints { get; set; }
    public int LossPoints { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string AlgorithmVersion { get; set; } = string.Empty;
    public int ReminderHoursBefore { get; set; }
    public string ReminderMessage { get; set; } = string.Empty;
    public string WinnerPoints { get; set; } = "0";
    public string RunnerUpPoints { get; set; } = "0";
    public string WinnerAchievementKey { get; set; } = string.Empty;
    public string RunnerUpAchievementKey { get; set; } = string.Empty;
    public string PrivateLobbyInformation { get; set; } = string.Empty;
    public long Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RegistrationOpenedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public List<CompetitionEntrant> Entrants { get; set; } = [];
    public List<CompetitionMatch> Matches { get; set; } = [];
    public List<CompetitionAudit> Audits { get; set; } = [];
    public List<CompetitionDomainEvent> Events { get; set; } = [];
    public List<CompetitionRewardReceipt> Rewards { get; set; } = [];
}

public sealed class CompetitionEntrant
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public long CompetitionId { get; set; }
    public Guid RegistrationOperationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SeedRank { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
    public Competition Competition { get; set; } = null!;
    public List<CompetitionEntrantMember> Members { get; set; } = [];
}

public sealed class CompetitionEntrantMember
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CompetitionEntrantId { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PrivateContact { get; set; } = string.Empty;
    public CompetitionEntrant Entrant { get; set; } = null!;
}

public sealed class CompetitionMatch
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public long CompetitionId { get; set; }
    public int Round { get; set; }
    public int Position { get; set; }
    public long? EntrantAId { get; set; }
    public long? EntrantBId { get; set; }
    public int? ScoreA { get; set; }
    public int? ScoreB { get; set; }
    public long? WinnerEntrantId { get; set; }
    public CompetitionMatchStatus Status { get; set; }
    public DateTime? ScheduledAtUtc { get; set; }
    public DateTime? ReminderDueAtUtc { get; set; }
    public DateTime? ReminderDeliveredAtUtc { get; set; }
    public DateTime? ReminderSuppressedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public Competition Competition { get; set; } = null!;
    public CompetitionEntrant? EntrantA { get; set; }
    public CompetitionEntrant? EntrantB { get; set; }
    public CompetitionEntrant? WinnerEntrant { get; set; }
}

public sealed class CompetitionAudit
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CompetitionId { get; set; }
    public long? MatchId { get; set; }
    public Guid OperationId { get; set; }
    public CompetitionAuditAction Action { get; set; }
    public string ActorTwitchUserId { get; set; } = string.Empty;
    public string ActorLogin { get; set; } = string.Empty;
    public string PrivateReason { get; set; } = string.Empty;
    public int? PreviousScoreA { get; set; }
    public int? PreviousScoreB { get; set; }
    public long? PreviousWinnerEntrantId { get; set; }
    public int? NewScoreA { get; set; }
    public int? NewScoreB { get; set; }
    public long? NewWinnerEntrantId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Competition Competition { get; set; } = null!;
}

public sealed class CompetitionDomainEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CompetitionId { get; set; }
    public Guid CompetitionPublicId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public CompetitionEventKind Kind { get; set; }
    public string PublicPayload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public Competition Competition { get; set; } = null!;
}

public sealed class CompetitionRewardReceipt
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CompetitionId { get; set; }
    public long EntrantId { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public int Placement { get; set; }
    public string PointsGranted { get; set; } = "0";
    public string AchievementKey { get; set; } = string.Empty;
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? AchievementGrantedAtUtc { get; set; }
    public Competition Competition { get; set; } = null!;
}

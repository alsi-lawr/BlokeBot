namespace BlokeBot.Persistence.Models;

public enum BingoGameMode
{
    [PersistedToken("Shared")]
    Shared,

    [PersistedToken("UniquePerViewer")]
    UniquePerViewer,

    [PersistedToken("Team")]
    Team,
}

public enum BingoGameStatus
{
    [PersistedToken("Joining")]
    Joining,

    [PersistedToken("Issued")]
    Issued,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Archived")]
    Archived,
}

public enum BingoSquareKind
{
    [PersistedToken("Manual")]
    Manual,

    [PersistedToken("IncomingRaid")]
    IncomingRaid,

    [PersistedToken("BountyCompleted")]
    BountyCompleted,

    [PersistedToken("GuessingResult")]
    GuessingResult,

    [PersistedToken("GiveawayStarted")]
    GiveawayStarted,

    [PersistedToken("StreamCategoryChanged")]
    StreamCategoryChanged,

    [PersistedToken("CounterReached")]
    CounterReached,
}

public enum BingoEvidenceAction
{
    [PersistedToken("Marked")]
    Marked,

    [PersistedToken("Reversed")]
    Reversed,
}

public enum BingoEvidenceSource
{
    [PersistedToken("Automatic")]
    Automatic,

    [PersistedToken("Manual")]
    Manual,
}

public enum BingoWinKind
{
    [PersistedToken("Row")]
    Row,

    [PersistedToken("Column")]
    Column,

    [PersistedToken("Diagonal")]
    Diagonal,

    [PersistedToken("FullCard")]
    FullCard,
}

public enum BingoDomainEventKind
{
    [PersistedToken("GameIssued")]
    GameIssued,

    [PersistedToken("SquareMarked")]
    SquareMarked,

    [PersistedToken("SquareReversed")]
    SquareReversed,

    [PersistedToken("WinCompleted")]
    WinCompleted,

    [PersistedToken("GameArchived")]
    GameArchived,
}

public sealed class BingoTemplate
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public Guid PublicId { get; set; }
    public Guid CreationOperationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CurrentRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<BingoTemplateRevision> Revisions { get; set; } = [];
}

public sealed class BingoTemplateRevision
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public Guid OperationId { get; set; }
    public long TemplateId { get; set; }
    public BingoTemplate? Template { get; set; }
    public int Revision { get; set; }
    public int Dimension { get; set; }
    public bool FullCardWinEnabled { get; set; }
    public string LinePointsReward { get; set; } = "0";
    public string? LineAchievementKey { get; set; }
    public string FullCardPointsReward { get; set; } = "0";
    public string? FullCardAchievementKey { get; set; }
    public string CreatedByTwitchUserId { get; set; } = string.Empty;
    public string CreatedByLogin { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<BingoSquare> Squares { get; set; } = [];
}

public sealed class BingoSquare
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long TemplateRevisionId { get; set; }
    public BingoTemplateRevision? TemplateRevision { get; set; }
    public string Key { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public BingoSquareKind Kind { get; set; }
    public long? Threshold { get; set; }
    public string? FilterToken { get; set; }
    public string PrivateModeratorNote { get; set; } = string.Empty;
}

public sealed class BingoGame
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public Guid PublicId { get; set; }
    public Guid CreationOperationId { get; set; }
    public long TemplateRevisionId { get; set; }
    public BingoTemplateRevision? TemplateRevision { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int TemplateRevisionNumber { get; set; }
    public int Dimension { get; set; }
    public string Seed { get; set; } = string.Empty;
    public BingoGameMode Mode { get; set; }
    public BingoGameStatus Status { get; set; }
    public int? ParticipantCap { get; set; }
    public int? TeamCap { get; set; }
    public long RosterRevision { get; set; }
    public bool FullCardWinEnabled { get; set; }
    public string LinePointsReward { get; set; } = "0";
    public string? LineAchievementKey { get; set; }
    public string FullCardPointsReward { get; set; } = "0";
    public string? FullCardAchievementKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public List<BingoTeam> Teams { get; set; } = [];
    public List<BingoParticipant> Participants { get; set; } = [];
    public List<BingoCard> Cards { get; set; } = [];
    public List<BingoWin> Wins { get; set; } = [];
}

public sealed class BingoTeam
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public BingoGame? Game { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<BingoParticipant> Participants { get; set; } = [];
}

public sealed class BingoParticipant
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public BingoGame? Game { get; set; }
    public long? TeamId { get; set; }
    public BingoTeam? Team { get; set; }
    public long? CardId { get; set; }
    public BingoCard? Card { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime JoinedAtUtc { get; set; }
}

public sealed class BingoCard
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public BingoGame? Game { get; set; }
    public Guid PublicId { get; set; }
    public string AssignmentKey { get; set; } = string.Empty;
    public string AssignmentName { get; set; } = string.Empty;
    public string? IssuedLayout { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public List<BingoParticipant> Participants { get; set; } = [];
    public List<BingoMark> Marks { get; set; } = [];
    public List<BingoWin> Wins { get; set; } = [];
}

public sealed class BingoMark
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public long CardId { get; set; }
    public BingoCard? Card { get; set; }
    public string SquareKey { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool IsActive { get; set; }
    public DateTime FirstMarkedAtUtc { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public List<BingoEvidence> Evidence { get; set; } = [];
}

public sealed class BingoEvidence
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public long CardId { get; set; }
    public long MarkId { get; set; }
    public BingoMark? Mark { get; set; }
    public BingoEvidenceAction Action { get; set; }
    public BingoEvidenceSource Source { get; set; }
    public BingoSquareKind EventKind { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? ParticipantTwitchUserId { get; set; }
    public string? ParticipantLogin { get; set; }
    public string? ParticipantDisplayName { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class BingoModerationAudit
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public long? CardId { get; set; }
    public long? MarkId { get; set; }
    public Guid OperationId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorTwitchUserId { get; set; } = string.Empty;
    public string ActorLogin { get; set; } = string.Empty;
    public string PrivateNote { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class BingoEventReceipt
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long? GameId { get; set; }
    public BingoSquareKind Kind { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class BingoWin
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public BingoGame? Game { get; set; }
    public long CardId { get; set; }
    public BingoCard? Card { get; set; }
    public Guid PublicId { get; set; }
    public BingoWinKind Kind { get; set; }
    public int RuleIndex { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public string PointsReward { get; set; } = "0";
    public string? AchievementKey { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public DateTime? RewardsCompletedAtUtc { get; set; }
    public List<BingoWinRecipient> Recipients { get; set; } = [];
}

public sealed class BingoWinRecipient
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long WinId { get; set; }
    public BingoWin? Win { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool PointsGranted { get; set; }
    public bool AchievementGranted { get; set; }
}

public sealed class BingoDomainEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long GameId { get; set; }
    public long? CardId { get; set; }
    public BingoDomainEventKind Kind { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string PublicPayload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

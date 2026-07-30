namespace BlokeBot.Persistence.Models;

public enum RequestBoardFieldKind
{
    [PersistedToken("Text")]
    Text,

    [PersistedToken("Url")]
    Url,

    [PersistedToken("Choice")]
    Choice,

    [PersistedToken("Number")]
    Number,

    [PersistedToken("TwitchClip")]
    TwitchClip,
}

public enum RequestBoardRefundPolicy
{
    [PersistedToken("Never")]
    Never,

    [PersistedToken("RejectedOrWithdrawn")]
    RejectedOrWithdrawn,

    [PersistedToken("AnyUnfulfilledClosure")]
    AnyUnfulfilledClosure,
}

public enum RequestSubmissionStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Approved")]
    Approved,

    [PersistedToken("Queued")]
    Queued,

    [PersistedToken("Accepted")]
    Accepted,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("Withdrawn")]
    Withdrawn,

    [PersistedToken("Merged")]
    Merged,
}

public enum RequestPointReservationState
{
    [PersistedToken("None")]
    None,

    [PersistedToken("Reserved")]
    Reserved,

    [PersistedToken("Refunded")]
    Refunded,

    [PersistedToken("Consumed")]
    Consumed,
}

public enum RequestBoardEventKind
{
    [PersistedToken("BoardConfigured")]
    BoardConfigured,

    [PersistedToken("Submitted")]
    Submitted,

    [PersistedToken("Voted")]
    Voted,

    [PersistedToken("StatusChanged")]
    StatusChanged,

    [PersistedToken("Merged")]
    Merged,

    [PersistedToken("PointsReserved")]
    PointsReserved,

    [PersistedToken("PointsRefunded")]
    PointsRefunded,
}

public sealed class RequestBoard
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsOpen { get; set; }

    public string PointCost { get; set; } = "0";

    public RequestBoardRefundPolicy RefundPolicy { get; set; }

    public int SubmissionLimitPerUser { get; set; } = 3;

    public int SubmissionCooldownSeconds { get; set; }

    public int VoteLimitPerUser { get; set; } = 10;

    public bool VotingEnabled { get; set; } = true;

    public string OrderingDescription { get; set; } =
        "Higher priority first, then votes, queue position, and submission time.";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<RequestBoardField> Fields { get; set; } = [];

    public List<RequestSubmission> Submissions { get; set; } = [];
}

public sealed class RequestBoardField
{
    public int Id { get; set; }

    public int BoardId { get; set; }

    public RequestBoard? Board { get; set; }

    public int Position { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public RequestBoardFieldKind Kind { get; set; }

    public bool IsRequired { get; set; }

    public int MaximumLength { get; set; } = 500;

    public decimal? MinimumNumber { get; set; }

    public decimal? MaximumNumber { get; set; }

    public string ChoiceOptions { get; set; } = string.Empty;
}

public sealed class RequestSubmission
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public int BoardId { get; set; }

    public RequestBoard? Board { get; set; }

    public Guid OperationId { get; set; }

    public string SubmitterLogin { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string NormalizedTitle { get; set; } = string.Empty;

    public string? NormalizedUrl { get; set; }

    public RequestSubmissionStatus Status { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Tags { get; set; } = string.Empty;

    public int Priority { get; set; }

    public long QueuePosition { get; set; }

    public int VoteCount { get; set; }

    public string PublicNote { get; set; } = string.Empty;

    public string PrivateModeratorNote { get; set; } = string.Empty;

    public string PrivateRejectionReason { get; set; } = string.Empty;

    public RequestPointReservationState PointReservationState { get; set; }

    public long? MergedIntoSubmissionId { get; set; }

    public RequestSubmission? MergedIntoSubmission { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<RequestSubmissionValue> Values { get; set; } = [];

    public List<RequestSubmissionVote> Votes { get; set; } = [];
}

public sealed class RequestSubmissionValue
{
    public long Id { get; set; }

    public long SubmissionId { get; set; }

    public RequestSubmission? Submission { get; set; }

    public int FieldId { get; set; }

    public RequestBoardField? Field { get; set; }

    public string Value { get; set; } = string.Empty;
}

public sealed class RequestSubmissionVote
{
    public long Id { get; set; }

    public long SubmissionId { get; set; }

    public RequestSubmission? Submission { get; set; }

    public string VoterLogin { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class RequestBoardDomainEvent
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public int BoardId { get; set; }

    public long? SubmissionId { get; set; }

    public int SchemaVersion { get; set; }

    public RequestBoardEventKind Kind { get; set; }

    public string PublicPayload { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}

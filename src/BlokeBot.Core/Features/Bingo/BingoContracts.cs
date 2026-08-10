using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bingo;

public readonly record struct BingoTemplateId(Guid Value);

public readonly record struct BingoGameId(Guid Value);

public readonly record struct BingoCardId(Guid Value);

public readonly record struct BingoTeamId(Guid Value);

public readonly record struct BingoDimension
{
    public BingoDimension(int value)
    {
        if (value is not (3 or 4 or 5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Use a 3×3, 4×4, or 5×5 grid."
            );
        }
        Value = value;
    }

    public int Value { get; }

    public int SquareCount => Value * Value;
}

public readonly record struct BingoSquareKey
{
    public BingoSquareKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        Value = normalized;
    }

    public string Value { get; }
}

public sealed record BingoActor(string TwitchUserId, string Login);

public sealed record BingoViewer(string TwitchUserId, string Login, string DisplayName);

public sealed record BingoWinReward(PointAmount Points, CommunityDefinitionKey? AchievementKey)
{
    public static BingoWinReward None { get; } = new(PointAmount.Zero, null);
}

public abstract record BingoSquareDefinition(
    BingoSquareKey Key,
    string Title,
    string PrivateModeratorNote
)
{
    public abstract BingoSquareKind Kind { get; }

    public sealed record Manual(BingoSquareKey Key, string Title, string PrivateModeratorNote = "")
        : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.Manual;
    }

    public sealed record IncomingRaid(
        BingoSquareKey Key,
        string Title,
        int MinimumViewerCount,
        string PrivateModeratorNote = ""
    ) : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.IncomingRaid;
    }

    public sealed record BountyCompleted(
        BingoSquareKey Key,
        string Title,
        string PrivateModeratorNote = ""
    ) : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.BountyCompleted;
    }

    public sealed record GuessingResult(
        BingoSquareKey Key,
        string Title,
        string? WinningAnswer,
        string PrivateModeratorNote = ""
    ) : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.GuessingResult;
    }

    public sealed record GiveawayStarted(
        BingoSquareKey Key,
        string Title,
        string PrivateModeratorNote = ""
    ) : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.GiveawayStarted;
    }

    public sealed record StreamCategoryChanged(
        BingoSquareKey Key,
        string Title,
        string? CategoryId,
        string PrivateModeratorNote = ""
    ) : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.StreamCategoryChanged;
    }

    public sealed record CounterReached(
        BingoSquareKey Key,
        string Title,
        int CounterId,
        long Target,
        string PrivateModeratorNote = ""
    ) : BingoSquareDefinition(Key, Title, PrivateModeratorNote)
    {
        public override BingoSquareKind Kind => BingoSquareKind.CounterReached;
    }
}

public sealed record BingoTemplateDraft(
    Guid OperationId,
    BingoTemplateId? TemplateId,
    string Name,
    BingoDimension Dimension,
    IReadOnlyList<BingoSquareDefinition> Squares,
    bool FullCardWinEnabled,
    BingoWinReward LineReward,
    BingoWinReward FullCardReward,
    BingoActor Actor
);

public sealed record BingoGameDraft(
    Guid OperationId,
    BingoTemplateId TemplateId,
    BingoGameMode Mode,
    string Seed,
    int? ParticipantCap,
    int? TeamCap,
    IReadOnlyList<string> Teams,
    BingoActor Actor
);

public sealed record BingoRosterCommand(
    Guid OperationId,
    BingoGameId GameId,
    BingoViewer Viewer,
    BingoTeamId? TeamId,
    BingoActor Actor,
    string PrivateNote
);

public sealed record BingoGameActionCommand(
    Guid OperationId,
    BingoGameId GameId,
    BingoActor Actor,
    string PrivateNote
);

public sealed record BingoManualMarkCommand(
    Guid OperationId,
    BingoGameId GameId,
    BingoCardId CardId,
    int Position,
    BingoActor Actor,
    string PrivateNote
);

public abstract record BingoAutomaticEvent
{
    private BingoAutomaticEvent() { }

    public abstract BingoSquareKind Kind { get; }
    public abstract string SourceEventId { get; }
    public abstract DateTimeOffset OccurredAtUtc { get; }
    public virtual BingoViewer? Participant => null;
    public virtual long Value => 1;
    public virtual string? FilterToken => null;
    public abstract string PublicSummary { get; }

    public sealed record IncomingRaid(
        string MessageId,
        BingoViewer Raider,
        int ViewerCount,
        DateTimeOffset OccurredAt
    ) : BingoAutomaticEvent
    {
        public override BingoSquareKind Kind => BingoSquareKind.IncomingRaid;
        public override string SourceEventId => MessageId;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
        public override BingoViewer Participant => Raider;
        public override long Value => ViewerCount;
        public override string PublicSummary =>
            $"Incoming raid from @{Raider.Login} with {ViewerCount} viewers";
    }

    public sealed record BountyCompleted(Guid BountyId, DateTimeOffset OccurredAt)
        : BingoAutomaticEvent
    {
        public override BingoSquareKind Kind => BingoSquareKind.BountyCompleted;
        public override string SourceEventId => BountyId.ToString("N");
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
        public override string PublicSummary => "Bounty completed";
    }

    public sealed record GuessingResult(
        int RoundId,
        string WinningAnswer,
        BingoViewer? Winner,
        DateTimeOffset OccurredAt
    ) : BingoAutomaticEvent
    {
        public override BingoSquareKind Kind => BingoSquareKind.GuessingResult;
        public override string SourceEventId =>
            RoundId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
        public override BingoViewer? Participant => Winner;
        public override string FilterToken => WinningAnswer.Trim().ToLowerInvariant();
        public override string PublicSummary => $"Guessing result: {WinningAnswer}";
    }

    public sealed record GiveawayStarted(int GiveawayId, DateTimeOffset OccurredAt)
        : BingoAutomaticEvent
    {
        public override BingoSquareKind Kind => BingoSquareKind.GiveawayStarted;
        public override string SourceEventId =>
            GiveawayId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
        public override string PublicSummary => "Giveaway started";
    }

    public sealed record StreamCategoryChanged(
        string MessageId,
        string CategoryId,
        string CategoryName,
        DateTimeOffset OccurredAt
    ) : BingoAutomaticEvent
    {
        public override BingoSquareKind Kind => BingoSquareKind.StreamCategoryChanged;
        public override string SourceEventId => MessageId;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
        public override string FilterToken => CategoryId;
        public override string PublicSummary => $"Stream category changed to {CategoryName}";
    }

    public sealed record CounterReached(
        string InvocationId,
        int CounterId,
        string CounterName,
        long CurrentValue,
        BingoViewer? Viewer,
        DateTimeOffset OccurredAt
    ) : BingoAutomaticEvent
    {
        public override BingoSquareKind Kind => BingoSquareKind.CounterReached;
        public override string SourceEventId => InvocationId;
        public override DateTimeOffset OccurredAtUtc => OccurredAt;
        public override BingoViewer? Participant => Viewer;
        public override long Value => CurrentValue;
        public override string FilterToken =>
            CounterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public override string PublicSummary => $"{CounterName} reached {CurrentValue}";
    }
}

public abstract record BingoOperationOutcome
{
    private BingoOperationOutcome() { }

    public sealed record Succeeded(bool WasIdempotent = false) : BingoOperationOutcome;

    public sealed record FeatureDisabled : BingoOperationOutcome;

    public sealed record NotFound : BingoOperationOutcome;

    public sealed record Frozen : BingoOperationOutcome;

    public sealed record Conflict(string Message) : BingoOperationOutcome;

    public sealed record Invalid(string Message) : BingoOperationOutcome;
}

public sealed record BingoSquareView(
    BingoSquareKey Key,
    int Position,
    string Title,
    BingoSquareKind Kind,
    bool Marked,
    IReadOnlyList<BingoEvidenceView> Evidence
);

public sealed record BingoEvidenceView(
    BingoEvidenceAction Action,
    BingoEvidenceSource Source,
    BingoSquareKind EventKind,
    string Summary,
    BingoViewer? Participant,
    DateTime OccurredAtUtc,
    DateTime RecordedAtUtc
);

public sealed record BingoWinView(
    Guid Id,
    BingoWinKind Kind,
    int RuleIndex,
    string RuleKey,
    DateTime CompletedAtUtc,
    bool RewardsCompleted,
    IReadOnlyList<BingoViewer> RewardRecipients
);

public sealed record BingoCardView(
    BingoCardId Id,
    string AssignmentName,
    IReadOnlyList<BingoViewer> Participants,
    IReadOnlyList<BingoSquareView> Squares,
    IReadOnlyList<BingoWinView> Wins
);

public sealed record BingoTeamView(BingoTeamId Id, string Name, IReadOnlyList<BingoViewer> Members);

public sealed record BingoGameView(
    BingoGameId Id,
    string TemplateName,
    int TemplateRevision,
    BingoDimension Dimension,
    string Seed,
    BingoGameMode Mode,
    BingoGameStatus Status,
    int? ParticipantCap,
    int? TeamCap,
    IReadOnlyList<BingoViewer> Participants,
    IReadOnlyList<BingoTeamView> Teams,
    IReadOnlyList<BingoCardView> Cards,
    DateTime CreatedAtUtc,
    DateTime? IssuedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? ArchivedAtUtc
);

public sealed record BingoTemplateView(
    BingoTemplateId Id,
    string Name,
    int Revision,
    BingoDimension Dimension,
    IReadOnlyList<BingoSquareDefinition> Squares,
    bool FullCardWinEnabled,
    BingoWinReward LineReward,
    BingoWinReward FullCardReward
);

public sealed record BingoModeratorAuditView(
    string Action,
    string ActorLogin,
    string PrivateNote,
    DateTime OccurredAtUtc
);

public sealed record BingoModeratorGameView(
    BingoGameView Game,
    IReadOnlyList<BingoModeratorAuditView> Audit
);

public sealed record BingoPublicView(
    string HostLogin,
    BingoGameView? LiveGame,
    IReadOnlyList<BingoGameView> Archive
);

public sealed record BingoOverlayEvent(
    int HostId,
    BingoGameId GameId,
    BingoCardId? CardId,
    BingoDomainEventKind Kind,
    string OperationKey,
    string PublicSummary,
    DateTimeOffset OccurredAtUtc
);

public interface IBingoOverlayEventObserver
{
    ValueTask BingoEventAsync(BingoOverlayEvent value, CancellationToken cancellationToken);
}

internal static class BingoSquareKindPresentation
{
    internal static string DisplayName(this BingoSquareKind value) =>
        value switch
        {
            BingoSquareKind.Manual => "Manual",
            BingoSquareKind.IncomingRaid => "Raid",
            BingoSquareKind.BountyCompleted => "Bounty",
            BingoSquareKind.GuessingResult => "Guessing",
            BingoSquareKind.GiveawayStarted => "Giveaway",
            BingoSquareKind.StreamCategoryChanged => "Category",
            BingoSquareKind.CounterReached => "Counter",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}

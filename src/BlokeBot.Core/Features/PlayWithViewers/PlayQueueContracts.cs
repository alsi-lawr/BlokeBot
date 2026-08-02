using System.Text;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.PlayWithViewers;

public static class PlayQueueLimits
{
    public const int MaximumFields = 12;
    public const int MaximumRoles = 12;
    public const int MaximumCapacity = 50;
    public const int MaximumEventReadCount = 200;
}

public sealed record PlayQueueViewerIdentity(
    string Login,
    string? TwitchUserId = null,
    string? DisplayName = null
);

public sealed record PlayQueueFieldCommand(
    string Key,
    string Label,
    bool IsRequired,
    IReadOnlyList<string>? Choices = null
);

public sealed record PlayQueueRoleRequirementCommand(string Role, int MinimumCount);

public sealed record ConfigurePlayQueueCommand(
    string Slug,
    string Name,
    string ActivityName,
    int Capacity,
    bool IsOpen,
    PlayQueueSelectionMode SelectionMode,
    bool ShowParticipantNames,
    int ReadinessTimeoutSeconds,
    int HistoryRetentionDays,
    int SkipExclusionMinutes,
    IReadOnlyList<PlayQueueFieldCommand> Fields,
    IReadOnlyList<PlayQueueRoleRequirementCommand> RoleRequirements
);

public sealed record JoinPlayQueueCommand(
    PlayQueueViewerIdentity Viewer,
    int Priority,
    IReadOnlyDictionary<string, string> FieldValues
);

public sealed record PlayQueueFieldView(
    int Id,
    string Key,
    string Label,
    bool IsRequired,
    IReadOnlyList<string> Choices
);

public sealed record PlayQueueRoleRequirementView(string Role, int MinimumCount);

public sealed record PlayQueueSummary(
    int Id,
    int HostId,
    string HostLogin,
    string Slug,
    string Name,
    string ActivityName,
    int Capacity,
    bool IsOpen,
    PlayQueueSelectionMode SelectionMode,
    bool ShowParticipantNames,
    int ReadinessTimeoutSeconds,
    int HistoryRetentionDays,
    int SkipExclusionMinutes,
    string PriorityDescription,
    IReadOnlyList<PlayQueueFieldView> Fields,
    IReadOnlyList<PlayQueueRoleRequirementView> RoleRequirements
);

public sealed record PlayQueueEntryFieldView(string Key, string Label, string Value);

public sealed record PublicPlayQueueEntryView(
    long Id,
    long Position,
    string? DisplayName,
    PlayQueueEntryStatus Status,
    DateTime? ReadyExpiresAtUtc,
    IReadOnlyList<PlayQueueEntryFieldView> Fields
);

public sealed record ModeratorPlayQueueEntryView(
    PublicPlayQueueEntryView Public,
    string NormalizedLogin,
    string? TwitchUserId,
    int Priority,
    string PrivateModeratorNote,
    DateTime JoinedAtUtc,
    DateTime? LastParticipatedAtUtc,
    DateTime? ExcludedUntilUtc
);

public sealed record PublicPlayQueueSnapshot(
    PlayQueueSummary Queue,
    IReadOnlyList<PublicPlayQueueEntryView> Waiting,
    IReadOnlyList<PublicPlayQueueEntryView> CurrentParty
);

public sealed record ModeratorPlayQueuePage(
    PlayQueueSummary Queue,
    IReadOnlyList<ModeratorPlayQueueEntryView> Waiting,
    IReadOnlyList<ModeratorPlayQueueEntryView> CurrentParty,
    IReadOnlyList<ModeratorPlayQueueEntryView> NextCandidates
);

public sealed record PlayQueueSelection(
    int PartyNumber,
    IReadOnlyList<ModeratorPlayQueueEntryView> Members
);

public sealed record PlayQueueEventView(
    long Id,
    int HostId,
    int QueueId,
    long? EntryId,
    int SchemaVersion,
    PlayQueueEventKind Kind,
    string PublicPayload,
    DateTime OccurredAtUtc
);

public abstract record PlayQueueResult<T>
{
    private PlayQueueResult() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    );

    public sealed record Succeeded(T Value, bool WasIdempotent = false) : PlayQueueResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => succeeded(this);
    }

    public sealed record Rejected(PlayQueueRejection Reason) : PlayQueueResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}

public abstract record PlayQueueRejection(string Message)
{
    public sealed record FeatureDisabled()
        : PlayQueueRejection("Play with viewers is turned off for this channel.");

    public sealed record Invalid(string Detail) : PlayQueueRejection(Detail);

    public sealed record NotFound(string Detail) : PlayQueueRejection(Detail);

    public sealed record Closed() : PlayQueueRejection("This queue is closed.");

    public sealed record AlreadyJoined() : PlayQueueRejection("You are already in this queue.");

    public sealed record NotJoined() : PlayQueueRejection("You are not waiting in this queue.");

    public sealed record Excluded(DateTime UntilUtc)
        : PlayQueueRejection($"You cannot join this queue until {UntilUtc:u}.");

    public sealed record Conflict(string Detail) : PlayQueueRejection(Detail);

    public sealed record Composition(string Detail) : PlayQueueRejection(Detail);
}

internal static class PlayQueueInput
{
    public static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('@').ToLowerInvariant();

    public static bool IsValidLogin(string value) =>
        value.Length is >= 1 and <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant();

    public static bool IsValidSlug(string value) =>
        value.Length is >= 1 and <= 48
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    public static string IdentityKey(PlayQueueViewerIdentity viewer)
    {
        var twitchUserId = viewer.TwitchUserId?.Trim();
        return string.IsNullOrWhiteSpace(twitchUserId)
            ? $"login:{NormalizeLogin(viewer.Login)}"
            : $"id:{twitchUserId}";
    }
}

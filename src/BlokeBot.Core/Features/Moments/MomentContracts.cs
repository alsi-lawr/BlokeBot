using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Moments;

public static class MomentLimits
{
    public const int MinimumMergeWindowSeconds = 15;
    public const int MaximumMergeWindowSeconds = 300;
    public const int DefaultMergeWindowSeconds = 90;
    public const int MaximumTitleLength = 200;
    public const int MaximumCategoryLength = 64;
    public const int MaximumSuggestionCount = 100;
    public const int MaximumContributorCount = 500;
    public const int MaximumEventReadCount = 200;
}

public sealed record MomentViewerIdentity(
    string Login,
    string? TwitchUserId = null,
    string? DisplayName = null
);

public sealed record ConfigureMomentHubCommand(
    int MergeWindowSeconds,
    bool MarkerFallbackEnabled,
    MomentRewardPolicy RewardPolicy,
    string RewardAmount
);

public sealed record CaptureMomentCommand(
    string StreamIdentity,
    MomentViewerIdentity Requester,
    string SuggestedTitle = "",
    string SuggestedCategory = ""
);

public sealed record ModerateMomentCommand(
    Guid PublicId,
    string PublicTitle,
    string PublicCategory,
    string ActorLogin,
    string PrivateText = ""
);

public sealed record MomentHubSettingsView(
    int HostId,
    int MergeWindowSeconds,
    bool MarkerFallbackEnabled,
    MomentRewardPolicy RewardPolicy,
    string RewardAmount
);

public sealed record MomentContributorView(
    string DisplayName,
    string NormalizedLogin,
    int CaptureCount,
    DateTime FirstCapturedAtUtc
);

public sealed record MomentView(
    Guid PublicId,
    int HostId,
    string HostLogin,
    string StreamIdentity,
    MomentCandidateState State,
    string PublicTitle,
    string PublicCategory,
    string? ProviderUrl,
    int VoteCount,
    DateTime CapturedAtUtc,
    DateTime? ApprovedAtUtc,
    IReadOnlyList<MomentContributorView> Contributors
);

public sealed record ModeratorMomentView(
    MomentView Public,
    string ProviderFailureReason,
    string PrivateRejectionReason,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> PrivateAudit
);

public sealed record MomentModeratorPage(
    MomentHubSettingsView Settings,
    IReadOnlyList<ModeratorMomentView> Candidates
);

public sealed record MomentRecapPage(
    string HostLogin,
    DateTime? WeekStartsAtUtc,
    string? StreamIdentity,
    IReadOnlyList<MomentView> Moments,
    Guid? WinningMomentId
);

public sealed record MomentEventView(
    long Id,
    int HostId,
    Guid PublicId,
    int SchemaVersion,
    MomentEventKind Kind,
    string StreamIdentity,
    string PublicPayload,
    DateTime OccurredAtUtc
);

public abstract record MomentResult<T>
{
    private MomentResult() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    );

    public sealed record Succeeded(T Value, bool WasIdempotent = false) : MomentResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => succeeded(this);
    }

    public sealed record Rejected(MomentRejection Reason) : MomentResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}

public abstract record MomentRejection(string Message)
{
    public sealed record FeatureDisabled()
        : MomentRejection("Moments are turned off for this channel.");

    public sealed record Invalid(string Detail) : MomentRejection(Detail);

    public sealed record NotFound() : MomentRejection("Moment not found.");

    public sealed record Conflict(string Detail) : MomentRejection(Detail);

    public sealed record ProviderUnavailable(string Detail) : MomentRejection(Detail);
}

public abstract record MomentProviderOutcome
{
    private MomentProviderOutcome() { }

    public sealed record Pending(int ClipId) : MomentProviderOutcome;

    public sealed record ClipReady(int ClipId) : MomentProviderOutcome;

    public sealed record MarkerReady(int MarkerId) : MomentProviderOutcome;

    public sealed record Ambiguous(int? ClipId, int? MarkerId, string Reason)
        : MomentProviderOutcome;

    public sealed record Failed(int? ClipId, int? MarkerId, string Reason) : MomentProviderOutcome;
}

public interface IMomentProviderOperations
{
    Task<MomentProviderOutcome> CaptureAsync(
        int hostId,
        Guid publicId,
        bool markerFallbackEnabled,
        string description,
        CancellationToken ct
    );
}

internal static class MomentInput
{
    public static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('@').ToLowerInvariant();

    public static bool IsValidLogin(string value) =>
        value.Length is >= 1 and <= 128
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static string IdentityKey(MomentViewerIdentity viewer)
    {
        var twitchUserId = viewer.TwitchUserId?.Trim();
        return string.IsNullOrWhiteSpace(twitchUserId)
            ? $"login:{NormalizeLogin(viewer.Login)}"
            : $"id:{twitchUserId}";
    }

    public static DateTime WeekStart(DateTime utc)
    {
        var date = utc.Date;
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(date.AddDays(-daysSinceMonday), DateTimeKind.Utc);
    }
}

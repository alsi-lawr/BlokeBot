using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPassports;

public static class ViewerPassportLimits
{
    public const int ProfileLineMaximumLength = 160;
}

public sealed record ViewerPassportIdentity(string TwitchUserId, string Login, string DisplayName);

public sealed record ViewerPassportAudience(string? TwitchUserId, bool IsChannelManager)
{
    public static ViewerPassportAudience Anonymous { get; } = new(null, false);
}

public sealed record SaveViewerPassportCommand(
    int HostId,
    ViewerPassportIdentity Viewer,
    string ProfileLine,
    ViewerPassportVisibility Visibility,
    bool HideAttendance,
    long? SelectedTitleRewardId,
    long? SelectedBadgeRewardId
);

public sealed record ViewerPassportRewardView(
    long Id,
    CommunityRewardKind Kind,
    string Name,
    string PresentationToken
);

public sealed record ViewerPassportStatistics(
    string Points,
    int? PointsRank,
    int GuessRounds,
    int CorrectGuesses,
    int AttendanceStreakSessions,
    int GamesWon,
    int GiveawaysWon,
    int BountiesSupported,
    int ApprovedMoments,
    int Achievements
)
{
    public int GuessAccuracyPercent =>
        GuessRounds == 0 ? 0 : (int)Math.Round(CorrectGuesses * 100m / GuessRounds);
}

public sealed record ViewerPassportView(
    int HostId,
    string HostLogin,
    string HostDisplayName,
    string TwitchUserId,
    string Login,
    string DisplayName,
    string ProfileLine,
    ViewerPassportVisibility Visibility,
    bool HideAttendance,
    ViewerPassportRewardView? SelectedTitle,
    ViewerPassportRewardView? SelectedBadge,
    IReadOnlyList<ViewerPassportRewardView> EarnedTitles,
    IReadOnlyList<ViewerPassportRewardView> EarnedBadges,
    ViewerPassportStatistics Statistics
);

public abstract record ViewerPassportQueryOutcome
{
    private ViewerPassportQueryOutcome() { }

    public sealed record Available(ViewerPassportView Passport) : ViewerPassportQueryOutcome;

    public sealed record FeatureDisabled : ViewerPassportQueryOutcome;

    public sealed record NotFound : ViewerPassportQueryOutcome;

    public sealed record Forbidden : ViewerPassportQueryOutcome;
}

public abstract record ViewerPassportMutationOutcome
{
    private ViewerPassportMutationOutcome() { }

    public sealed record Succeeded(ViewerPassportView Passport) : ViewerPassportMutationOutcome;

    public sealed record FeatureDisabled : ViewerPassportMutationOutcome;

    public sealed record Invalid(string Message) : ViewerPassportMutationOutcome;

    public sealed record UnearnedReward : ViewerPassportMutationOutcome;

    public sealed record NotFound : ViewerPassportMutationOutcome;
}

public abstract record ViewerPassportResetOutcome
{
    private ViewerPassportResetOutcome() { }

    public sealed record Succeeded(bool Removed) : ViewerPassportResetOutcome;

    public sealed record FeatureDisabled : ViewerPassportResetOutcome;

    public sealed record NotFound : ViewerPassportResetOutcome;
}

public abstract record ViewerPassportExportOutcome
{
    private ViewerPassportExportOutcome() { }

    public sealed record Succeeded(IReadOnlyDictionary<string, IReadOnlyList<object>> Sections)
        : ViewerPassportExportOutcome;

    public sealed record FeatureDisabled : ViewerPassportExportOutcome;

    public sealed record NotFound : ViewerPassportExportOutcome;
}

public sealed record ViewerPassportOverlayData(
    string DisplayName,
    string? Title,
    string? Badge,
    string ProfileLine,
    string Points,
    int? PointsRank,
    int? AttendanceStreakSessions,
    int Achievements
);

public sealed record ViewerPassportAutomationPayload(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string? Title,
    string? Badge,
    string Points,
    int? PointsRank,
    int? AttendanceStreakSessions,
    int GamesWon,
    int GiveawaysWon,
    int BountiesSupported,
    int ApprovedMoments,
    int Achievements
);

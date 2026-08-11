namespace BlokeBot.Core.Features.ViewerPassports;

public sealed class ViewerPassportProjectionService(ViewerPassportService passports)
{
    public async Task<ViewerPassportOverlayData?> GetOverlayDataAsync(
        string channelLogin,
        string viewerLogin,
        CancellationToken cancellationToken
    ) =>
        (
            await passports.GetVisibleAsync(
                channelLogin,
                viewerLogin,
                ViewerPassportAudience.Anonymous,
                cancellationToken
            )
        )
            is ViewerPassportQueryOutcome.Available { Passport: var passport }
        && passport.Visibility == Persistence.Models.ViewerPassportVisibility.Public
            ? new ViewerPassportOverlayData(
                passport.DisplayName,
                passport.SelectedTitle?.Name,
                passport.SelectedBadge?.PresentationToken,
                passport.ProfileLine,
                passport.Statistics.Points,
                passport.Statistics.PointsRank,
                passport.HideAttendance ? null : passport.Statistics.AttendanceStreakDays,
                passport.Statistics.Achievements
            )
            : null;

    public async Task<ViewerPassportAutomationPayload?> GetAutomationPayloadAsync(
        string channelLogin,
        string viewerLogin,
        CancellationToken cancellationToken
    ) =>
        (
            await passports.GetVisibleAsync(
                channelLogin,
                viewerLogin,
                ViewerPassportAudience.Anonymous,
                cancellationToken
            )
        )
            is ViewerPassportQueryOutcome.Available { Passport: var passport }
        && passport.Visibility == Persistence.Models.ViewerPassportVisibility.Public
            ? new ViewerPassportAutomationPayload(
                passport.TwitchUserId,
                passport.Login,
                passport.DisplayName,
                passport.SelectedTitle?.Name,
                passport.SelectedBadge?.PresentationToken,
                passport.Statistics.Points,
                passport.Statistics.PointsRank,
                passport.HideAttendance ? null : passport.Statistics.AttendanceStreakDays,
                passport.Statistics.GamesWon,
                passport.Statistics.GiveawaysWon,
                passport.Statistics.BountiesSupported,
                passport.Statistics.ApprovedMoments,
                passport.Statistics.Achievements
            )
            : null;
}

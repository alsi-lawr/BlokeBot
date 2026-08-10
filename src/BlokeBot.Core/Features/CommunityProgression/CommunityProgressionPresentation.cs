using System.Globalization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CommunityProgression;

internal static class CommunityProgressionPresentation
{
    internal static string StatusPillClass(CommunitySeasonStatus status) =>
        status == CommunitySeasonStatus.Open
            ? "status-pill status-pill--green"
            : "status-pill status-pill--slate";

    internal static string DefinitionPillClass(CommunityDefinitionKind kind) =>
        kind == CommunityDefinitionKind.Quest
            ? "status-pill status-pill--blue"
            : "status-pill status-pill--violet";

    internal static string ScopeLabel(CommunityProgressScope scope) =>
        scope == CommunityProgressScope.Viewer ? "Per viewer" : "Communal";

    internal static string CompletionLabel(CommunityCompletionMode completion) =>
        completion == CommunityCompletionMode.Repeatable ? "repeatable" : "one-time";

    internal static string StandingsHeading(CommunitySeasonStatus status) =>
        status is CommunitySeasonStatus.Closed or CommunitySeasonStatus.Archived
            ? "Final standings"
            : "Live standings";

    internal static int MeterPercent(long amount, long target)
    {
        if (target <= 0)
        {
            return amount > 0 ? 100 : 0;
        }

        var ratio = (double)amount / target;
        return (int)Math.Round(Math.Clamp(ratio, 0, 1) * 100);
    }

    internal static string MeterClass(long amount, long target) =>
        target > 0 && amount >= target ? "meter meter--full" : "meter";

    internal static string MeterFillStyle(long amount, long target) =>
        string.Create(CultureInfo.InvariantCulture, $"width:{MeterPercent(amount, target)}%");

    internal static string Amount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Wall-clock phrasing for a moment already known to the reader, so the exact instant stays
    /// available as the element title rather than being read out as a UTC run.
    /// </summary>
    internal static string HumanMoment(DateTime utc)
    {
        var moment = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var today = DateTime.UtcNow.Date;
        var time = moment.ToString("HH:mm", CultureInfo.InvariantCulture);
        return moment.Date == today ? $"today {time}"
            : moment.Date == today.AddDays(-1) ? $"yesterday {time}"
            : moment.Year == today.Year
                ? $"{moment.ToString("d MMM", CultureInfo.InvariantCulture)}, {time}"
            : moment.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    internal static string ExactMoment(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("u", CultureInfo.InvariantCulture);

    internal static string HumanDate(DateTime moment) =>
        moment.Year == DateTime.UtcNow.Year
            ? moment.ToString("d MMM", CultureInfo.InvariantCulture)
            : moment.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    internal static string SeasonRange(DateTime startsAtUtc, DateTime endsAtUtc) =>
        $"{HumanDate(startsAtUtc)} to {HumanDate(endsAtUtc)}";
}

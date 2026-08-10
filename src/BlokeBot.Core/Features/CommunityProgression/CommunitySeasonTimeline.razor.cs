using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

/// <summary>
/// Start, today, and end of a season on one bar. The end is stated in the channel time zone when
/// the caller knows it, because that is the clock the reset schedules run on.
/// </summary>
public partial class CommunitySeasonTimeline
{
    [Parameter, EditorRequired]
    public required DateTime StartsAtUtc { get; set; }

    [Parameter, EditorRequired]
    public required DateTime EndsAtUtc { get; set; }

    [Parameter]
    public string? TimeZoneId { get; set; }

    private DateTime _now => DateTime.UtcNow;

    private TimeSpan _span => EndsAtUtc - StartsAtUtc;

    private double _elapsedFraction =>
        _span <= TimeSpan.Zero ? 1 : Math.Clamp((_now - StartsAtUtc) / _span, 0, 1);

    private int _totalDays => Math.Max(1, (int)Math.Ceiling(_span.TotalDays));

    private int _elapsedDays =>
        Math.Clamp((int)Math.Floor((_now - StartsAtUtc).TotalDays) + 1, 0, _totalDays);

    private string _fillStyle =>
        string.Create(CultureInfo.InvariantCulture, $"width:{Math.Round(_elapsedFraction * 100)}%");

    private string _todayStyle =>
        string.Create(CultureInfo.InvariantCulture, $"left:{Math.Round(_elapsedFraction * 100)}%");

    private string _startLabel => CommunityProgressionPresentation.HumanDate(StartsAtUtc);

    private string _dayLabel =>
        _now < StartsAtUtc ? "Not started yet"
        : _now > EndsAtUtc ? $"Ran for {_totalDays} days"
        : $"Day {_elapsedDays} of {_totalDays}";

    private TimeZoneInfo? _zone
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TimeZoneId))
            {
                return null;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return null;
            }
            catch (InvalidTimeZoneException)
            {
                return null;
            }
        }
    }

    private string _endLabel
    {
        get
        {
            var zone = _zone;
            if (zone is null)
            {
                return CommunityProgressionPresentation.HumanDate(EndsAtUtc);
            }

            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(EndsAtUtc, DateTimeKind.Utc),
                zone
            );
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{CommunityProgressionPresentation.HumanDate(local)}, {local:HH:mm}"
            );
        }
    }

    private string _zoneLabel => _zone is null ? string.Empty : $" ({_zone.Id})";
}

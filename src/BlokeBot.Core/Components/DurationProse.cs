using System.Globalization;

namespace BlokeBot.Core.Components;

/// <summary>
/// Reads a second count back in words beside the control holding it, so "300" is visibly five
/// minutes without arithmetic. Shared because every settings surface with a seconds field wants the
/// same sentence.
/// </summary>
public static class DurationProse
{
    public static string Format(int seconds)
    {
        var minutes = seconds / 60;
        var remainder = seconds % 60;
        return (minutes, remainder) switch
        {
            (0, _) => $"{seconds.ToString(CultureInfo.CurrentCulture)} seconds",
            (_, 0) when minutes == 1 => "1 minute",
            (_, 0) => $"{minutes.ToString(CultureInfo.CurrentCulture)} minutes",
            _ =>
                $"{minutes.ToString(CultureInfo.CurrentCulture)} min {remainder.ToString(CultureInfo.CurrentCulture)} s",
        };
    }
}

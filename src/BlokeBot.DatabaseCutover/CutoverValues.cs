using System.Globalization;

namespace BlokeBot.DatabaseCutover;

internal static class CutoverValues
{
    internal static object ForTarget(object value, string storeType) =>
        storeType switch
        {
            "boolean" => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
            "integer" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "bigint" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            "numeric" => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            "uuid" => value is Guid guid ? guid : Guid.Parse(InvariantString(value)),
            "timestamp with time zone" => UtcDateTime(value),
            "time without time zone" => Time(value),
            "bytea" => (byte[])value,
            _ => InvariantString(value),
        };

    private static DateTime UtcDateTime(object value) =>
        value is DateTime dateTime
            ? dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            }
            : DateTime.Parse(
                InvariantString(value),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
            );

    private static TimeOnly Time(object value) =>
        value is TimeOnly time
            ? time
            : TimeOnly.Parse(InvariantString(value), CultureInfo.InvariantCulture);

    private static string InvariantString(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}

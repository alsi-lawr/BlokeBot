using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

    internal static void AppendCanonical(IncrementalHash hash, object? value, string storeType)
    {
        if (value is null or DBNull)
        {
            hash.AppendData([0]);
            return;
        }

        hash.AppendData([1]);
        if (storeType == "bytea")
        {
            AppendBytes(hash, (byte[])value);
            return;
        }

        var target = ForTarget(value, storeType);
        var canonical = target switch
        {
            bool boolean => boolean ? "true" : "false",
            DateTime dateTime => dateTime
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => target.ToString()!,
        };
        AppendBytes(hash, Encoding.UTF8.GetBytes(canonical));
    }

    private static void AppendBytes(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        _ = BitConverter.TryWriteBytes(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

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

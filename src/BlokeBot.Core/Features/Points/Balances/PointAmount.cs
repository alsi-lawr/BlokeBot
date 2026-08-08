using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.Points.Balances;

public readonly record struct PointAmount : IComparable<PointAmount>
{
    private const int _displaySignificantFigures = 4;
    private static readonly string[] _compactSuffixes = ["", "K", "M", "B", "T"];

    public static readonly BigInteger MaximumValue = BigInteger.Pow(10, 100);
    public static readonly PointAmount Zero = new(BigInteger.Zero);

    public PointAmount(BigInteger value)
    {
        if (value < BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Point amounts cannot be negative."
            );
        }

        if (value > MaximumValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Point amounts cannot exceed 10^100."
            );
        }

        Value = value;
    }

    public BigInteger Value { get; }

    public bool IsZero => Value.IsZero;

    public int CompareTo(PointAmount other) => Value.CompareTo(other.Value);

    public static bool operator <(PointAmount left, PointAmount right) => left.Value < right.Value;

    public static bool operator <=(PointAmount left, PointAmount right) =>
        left.Value <= right.Value;

    public static bool operator >(PointAmount left, PointAmount right) => left.Value > right.Value;

    public static bool operator >=(PointAmount left, PointAmount right) =>
        left.Value >= right.Value;

    public PointAmount Add(PointAmount amount) => new(Value + amount.Value);

    public PointAmount Subtract(PointAmount amount) =>
        amount.Value > Value
            ? throw new InvalidOperationException("Point balance cannot become negative.")
            : new PointAmount(Value - amount.Value);

    public string ToDisplayString() => FormatForDisplay(Value, _displaySignificantFigures);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static PointAmount ParseAbsolute(string? value) =>
        ParseNonNegativeAbsolute(value)
            .Match(
                static amount => amount,
                static error =>
                    throw error switch
                    {
                        PointAmountParseError.InvalidFormat => new FormatException(
                            "Point amount must be a whole number."
                        ),
                        PointAmountParseError.AmountOutOfRange => new ArgumentOutOfRangeException(
                            nameof(value),
                            "Point amounts cannot exceed 10^100."
                        ),
                        _ => new UnreachableException(
                            "Non-negative absolute parsing returned an unsupported error."
                        ),
                    }
            );

    internal static Result<PointAmount, PointAmountParseError> ParseNonNegativeAbsolute(
        string? value
    )
    {
        var text = (value ?? string.Empty).Trim();
        return BigInteger.TryParse(
            text,
            NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed > MaximumValue
                ? Result<PointAmount, PointAmountParseError>.Error(
                    PointAmountParseError.AmountOutOfRange
                )
                : Result<PointAmount, PointAmountParseError>.Success(new PointAmount(parsed))
            : ParseCompactNotation(text);
    }

    private static Result<PointAmount, PointAmountParseError> ParseCompactNotation(string text)
    {
        var scientificSplit = text.Split(['x', 'X'], 2);
        if (scientificSplit.Length == 2)
        {
            var exponentText = scientificSplit[1].Trim();
            return
                exponentText.StartsWith("10^", StringComparison.Ordinal)
                && int.TryParse(
                    exponentText["10^".Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var exponent
                )
                && exponent <= 100
                ? ScaleMantissa(scientificSplit[0].Trim(), exponent)
                : Result<PointAmount, PointAmountParseError>.Error(
                    PointAmountParseError.InvalidFormat
                );
        }

        var suffixIndex =
            text.Length == 0
                ? -1
                : Array.FindIndex(
                    _compactSuffixes,
                    suffix => suffix.Length == 1 && char.ToUpperInvariant(text[^1]) == suffix[0]
                );
        return suffixIndex > 0
            ? ScaleMantissa(text[..^1].Trim(), suffixIndex * 3)
            : Result<PointAmount, PointAmountParseError>.Error(PointAmountParseError.InvalidFormat);
    }

    private static Result<PointAmount, PointAmountParseError> ScaleMantissa(
        string mantissa,
        int exponent
    )
    {
        var parts = mantissa.Split('.', 2);
        var integerDigits = parts[0];
        var fractionDigits = (parts.Length == 2 ? parts[1] : string.Empty).TrimEnd('0');
        if (
            integerDigits.Length == 0
            || !integerDigits.All(char.IsAsciiDigit)
            || !fractionDigits.All(char.IsAsciiDigit)
            || fractionDigits.Length > exponent
        )
        {
            return Result<PointAmount, PointAmountParseError>.Error(
                PointAmountParseError.InvalidFormat
            );
        }

        var combined = BigInteger.Parse(
            integerDigits + fractionDigits,
            CultureInfo.InvariantCulture
        );
        var scaled = combined * BigInteger.Pow(10, exponent - fractionDigits.Length);
        return scaled > MaximumValue
            ? Result<PointAmount, PointAmountParseError>.Error(
                PointAmountParseError.AmountOutOfRange
            )
            : Result<PointAmount, PointAmountParseError>.Success(new PointAmount(scaled));
    }

    private static string FormatForDisplay(BigInteger value, int significantFigures)
    {
        if (value.IsZero)
        {
            return "0";
        }

        var rounded = RoundToSignificantFigures(value, significantFigures);
        var digits = rounded.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= significantFigures)
        {
            return rounded.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        var compactExponent = (digits.Length - 1) / 3 * 3;
        var suffixIndex = compactExponent / 3;
        if (suffixIndex < _compactSuffixes.Length)
        {
            var integerDigits = digits.Length - compactExponent;
            return $"{FormatSignificantDigits(digits, integerDigits, significantFigures)}{_compactSuffixes[suffixIndex]}";
        }

        var scientificExponent = digits.Length - 1;
        return $"{FormatSignificantDigits(digits, 1, significantFigures)} x 10^{scientificExponent}";
    }

    private static string FormatSignificantDigits(
        string digits,
        int integerDigits,
        int significantFigures
    )
    {
        var significant =
            digits.Length < significantFigures
                ? digits.PadRight(significantFigures, '0')
                : digits[..significantFigures];
        var formatted =
            integerDigits >= significant.Length
                ? significant
                : significant.Insert(integerDigits, ".");

        return formatted.TrimEnd('0').TrimEnd('.');
    }

    private static BigInteger RoundToSignificantFigures(BigInteger value, int significantFigures)
    {
        if (value.IsZero)
        {
            return BigInteger.Zero;
        }

        var digits = value.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= significantFigures)
        {
            return value;
        }

        var kept = BigInteger.Parse(digits[..significantFigures], CultureInfo.InvariantCulture);
        if (digits[significantFigures] >= '5')
        {
            kept += BigInteger.One;
        }

        var zeros = digits.Length - significantFigures;
        return kept * BigInteger.Pow(10, zeros);
    }
}

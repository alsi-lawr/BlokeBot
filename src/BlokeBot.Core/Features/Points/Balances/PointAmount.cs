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

    public int CompareTo(PointAmount other)
    {
        return Value.CompareTo(other.Value);
    }

    public PointAmount Add(PointAmount amount)
    {
        return new(Value + amount.Value);
    }

    public PointAmount Subtract(PointAmount amount)
    {
        if (amount.Value > Value)
        {
            throw new InvalidOperationException("Point balance cannot become negative.");
        }

        return new PointAmount(Value - amount.Value);
    }

    public string ToDisplayString()
    {
        return FormatForDisplay(Value, _displaySignificantFigures);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static PointAmount ParseAbsolute(string? value)
    {
        return ParseNonNegativeAbsolute(value)
            .Match(
                static amount => amount,
                error =>
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
    }

    internal static Result<PointAmount, PointAmountParseError> ParseNonNegativeAbsolute(
        string? value
    )
    {
        var text = (value ?? string.Empty).Trim();
        if (
            !BigInteger.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            return Result<PointAmount, PointAmountParseError>.Error(
                PointAmountParseError.InvalidFormat
            );
        }

        if (parsed > MaximumValue)
        {
            return Result<PointAmount, PointAmountParseError>.Error(
                PointAmountParseError.AmountOutOfRange
            );
        }

        return Result<PointAmount, PointAmountParseError>.Success(new PointAmount(parsed));
    }

    private static string FormatForDisplay(BigInteger value, int significantFigures)
    {
        if (value.IsZero)
        {
            return "0";
        }

        var rounded = RoundToSignificantFigures(value, significantFigures);
        var digits = rounded.ToString();
        if (digits.Length <= significantFigures)
        {
            return rounded.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        var compactExponent = ((digits.Length - 1) / 3) * 3;
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

        var digits = value.ToString();
        if (digits.Length <= significantFigures)
        {
            return value;
        }

        var kept = BigInteger.Parse(digits[..significantFigures]);
        if (digits[significantFigures] >= '5')
        {
            kept += BigInteger.One;
        }

        var zeros = digits.Length - significantFigures;
        return kept * BigInteger.Pow(10, zeros);
    }
}

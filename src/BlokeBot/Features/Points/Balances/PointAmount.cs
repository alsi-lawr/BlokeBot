using System.Numerics;
using System.Text.RegularExpressions;

namespace BlokeBot.Features.Points.Balances;

public readonly partial record struct PointAmount : IComparable<PointAmount>
{
    public static readonly BigInteger MaximumValue = BigInteger.Pow(10, 100);
    public static readonly PointAmount Zero = new(BigInteger.Zero);

    public PointAmount(BigInteger value)
    {
        if (value < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Point amounts cannot be negative."
            );

        if (value > MaximumValue)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Point amounts cannot exceed 10^100."
            );

        Value = value;
    }

    public BigInteger Value { get; }

    public bool IsZero => Value.IsZero;

    public int CompareTo(PointAmount other) => Value.CompareTo(other.Value);

    public PointAmount Add(PointAmount amount) => new(Value + amount.Value);

    public PointAmount Subtract(PointAmount amount)
    {
        if (amount.Value > Value)
            throw new InvalidOperationException("Point balance cannot become negative.");

        return new PointAmount(Value - amount.Value);
    }

    public PointAmount RoundForPersistence() =>
        DigitCount(Value) > 10 ? new PointAmount(RoundToSignificantFigures(Value, 4)) : this;

    public string ToDisplayString() =>
        DigitCount(Value) > 10 ? FormatEngineering(Value, 4) : ToString();

    public override string ToString() => Value.ToString();

    public static PointAmount ParseAbsolute(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!WholeNumberRegex().IsMatch(text))
            throw new FormatException("Point amount must be a whole number.");

        return new PointAmount(BigInteger.Parse(text));
    }

    public static bool TryParseAbsolute(string? value, out PointAmount amount)
    {
        try
        {
            amount = ParseAbsolute(value);
            return true;
        }
        catch
        {
            amount = Zero;
            return false;
        }
    }

    private static string FormatEngineering(BigInteger value, int significantFigures)
    {
        var rounded = RoundToSignificantFigures(value, significantFigures);
        var digits = rounded.ToString();
        var exponent = ((digits.Length - 1) / 3) * 3;
        var integerDigits = digits.Length - exponent;
        var significant = digits;

        if (significant.Length < significantFigures)
            significant = significant.PadRight(significantFigures, '0');

        if (significant.Length > significantFigures)
            significant = significant[..significantFigures];

        var mantissa =
            integerDigits >= significantFigures
                ? significant
                : significant.Insert(integerDigits, ".");

        return $"{mantissa}e{exponent}";
    }

    private static int DigitCount(BigInteger value) => value.IsZero ? 1 : value.ToString().Length;

    private static BigInteger RoundToSignificantFigures(BigInteger value, int significantFigures)
    {
        if (value.IsZero)
            return BigInteger.Zero;

        var digits = value.ToString();
        if (digits.Length <= significantFigures)
            return value;

        var kept = BigInteger.Parse(digits[..significantFigures]);
        if (digits[significantFigures] >= '5')
            kept += BigInteger.One;

        var zeros = digits.Length - significantFigures;
        return kept * BigInteger.Pow(10, zeros);
    }

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex WholeNumberRegex();
}

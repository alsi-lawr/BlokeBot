using System.Globalization;
using System.Numerics;

namespace BlokeBot.Features.Points.Balances;

public enum PointAmountArgumentKind
{
    Absolute,
    Percentage,
    All,
}

public static class PointAmountArgumentParser
{
    public static PointAmount ParseAbsoluteOnly(string? value)
    {
        return PointAmount.ParseAbsolute(value);
    }

    public static PointAmount ParseSpendAmount(string? value, PointAmount sourceBalance)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return RejectZero(sourceBalance);
        }

        if (text.EndsWith('%'))
        {
            var number = text[..^1].Trim();
            if (
                !decimal.TryParse(
                    number,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var percentage
                )
                || percentage <= 0
                || percentage > 100
            )
            {
                throw new FormatException(
                    "Point percentage must be greater than 0 and no more than 100."
                );
            }

            var scaled = sourceBalance.Value * new BigInteger(decimal.Floor(percentage * 1000));
            var amount = new PointAmount(scaled / 100000);
            return RejectZero(amount);
        }

        return RejectZero(PointAmount.ParseAbsolute(text));
    }

    private static PointAmount RejectZero(PointAmount amount)
    {
        if (amount.IsZero)
        {
            throw new FormatException("Point amount must be greater than zero.");
        }

        return amount;
    }
}

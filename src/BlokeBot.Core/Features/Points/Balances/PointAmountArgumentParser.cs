using System.Globalization;
using System.Numerics;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.Points.Balances;

public enum PointAmountParseError
{
    InvalidFormat,
    AmountOutOfRange,
    PercentageOutOfRange,
    ZeroAmount,
}

public static class PointAmountArgumentParser
{
    public static Result<PointAmount, PointAmountParseError> ParseAbsolute(string? value)
    {
        return PointAmount.ParseNonNegativeAbsolute(value).Bind(RejectZero);
    }

    public static Result<PointAmount, PointAmountParseError> ParseSpend(
        string? value,
        PointAmount sourceBalance
    )
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return RejectZero(sourceBalance);
        }

        if (!text.EndsWith('%'))
        {
            return ParseAbsolute(text);
        }

        var number = text[..^1].Trim();
        if (
            !decimal.TryParse(
                number,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var percentage
            )
        )
        {
            return Result<PointAmount, PointAmountParseError>.Error(
                PointAmountParseError.InvalidFormat
            );
        }

        if (percentage <= 0 || percentage > 100)
        {
            return Result<PointAmount, PointAmountParseError>.Error(
                PointAmountParseError.PercentageOutOfRange
            );
        }

        var scaled = sourceBalance.Value * new BigInteger(decimal.Floor(percentage * 1000));
        return RejectZero(new PointAmount(scaled / 100000));
    }

    private static Result<PointAmount, PointAmountParseError> RejectZero(PointAmount amount)
    {
        return amount.IsZero
            ? Result<PointAmount, PointAmountParseError>.Error(PointAmountParseError.ZeroAmount)
            : Result<PointAmount, PointAmountParseError>.Success(amount);
    }
}

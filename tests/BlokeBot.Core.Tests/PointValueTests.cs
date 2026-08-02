using System.Numerics;
using BlokeBot.Core.Features.Points.Balances;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointValueTests : PointsTestBase
{
    [Test]
    public void InvalidNegativeOrOversizedAmount_ParsingOrConstructing_RejectsValue()
    {
        DescribeParse(PointAmountArgumentParser.ParseAbsolute("100")).ShouldBe("Amount:100");
        DescribeParse(PointAmountArgumentParser.ParseAbsolute("10.5"))
            .ShouldBe("Error:InvalidFormat");
        DescribeParse(PointAmountArgumentParser.ParseAbsolute("-1"))
            .ShouldBe("Error:InvalidFormat");
        DescribeParse(PointAmountArgumentParser.ParseAbsolute("0")).ShouldBe("Error:ZeroAmount");
        DescribeParse(
                PointAmountArgumentParser.ParseAbsolute(
                    (PointAmount.MaximumValue + BigInteger.One).ToString()
                )
            )
            .ShouldBe("Error:AmountOutOfRange");
        Should.Throw<ArgumentOutOfRangeException>(() => new PointAmount(-1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PointAmount(PointAmount.MaximumValue + 1)
        );
    }

    [Test]
    public void LargePointAmounts_FormattingForDisplay_UsesFourSignificantFiguresWithoutChangingValue()
    {
        var amount = PointAmount.ParseAbsolute("123456789012");

        amount.ToString().ShouldBe("123456789012");
        amount.ToDisplayString().ShouldBe("123.5B");
        PointAmount.ParseAbsolute("1234").ToDisplayString().ShouldBe("1,234");
        PointAmount.ParseAbsolute("10000").ToDisplayString().ShouldBe("10K");
        PointAmount.ParseAbsolute("999950").ToDisplayString().ShouldBe("1M");
        PointAmount.ParseAbsolute("1234567890123").ToDisplayString().ShouldBe("1.235T");
        PointAmount
            .ParseAbsolute("1234567890123456789012345678901234")
            .ToDisplayString()
            .ShouldBe("1.235 x 10^33");
    }

    [Test]
    public void AbsolutePercentageOrAllSpend_Parsing_ReturnsExpectedAmountAndRejectsInvalidInput()
    {
        var balance = PointAmount.ParseAbsolute("2500");

        DescribeParse(PointAmountArgumentParser.ParseSpend("100", balance)).ShouldBe("Amount:100");
        DescribeParse(PointAmountArgumentParser.ParseSpend("10%", balance)).ShouldBe("Amount:250");
        DescribeParse(PointAmountArgumentParser.ParseSpend("all", balance)).ShouldBe("Amount:2500");
        DescribeParse(PointAmountArgumentParser.ParseSpend("1%", PointAmount.ParseAbsolute("1")))
            .ShouldBe("Error:ZeroAmount");
        DescribeParse(PointAmountArgumentParser.ParseSpend("101%", balance))
            .ShouldBe("Error:PercentageOutOfRange");
        DescribeParse(PointAmountArgumentParser.ParseSpend("not-a-number%", balance))
            .ShouldBe("Error:InvalidFormat");
        DescribeParse(PointAmountArgumentParser.ParseAbsolute("50%"))
            .ShouldBe("Error:InvalidFormat");
    }
}

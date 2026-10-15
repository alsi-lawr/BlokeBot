using System.Globalization;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandArgumentTests
{
    internal static CustomCommandArgumentSchema TimerSchema =>
        new([
            new("seconds", CustomCommandArgumentKind.Number, 0m),
            new("name", CustomCommandArgumentKind.RemainingText, MaximumWords: 10),
        ]);

    [Test]
    public void CommandArguments_InvariantDecimalBoundsApplyAfterNativeRoundingAndUnderflow()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (
                var (input, expected) in new[]
                {
                    ("+1.25e1", 12.5m),
                    (".5", .5m),
                    ("1e-28", .0000000000000000000000000001m),
                    ("1.23456789012345678901234567895", 1.2345678901234567890123456790m),
                }
            )
            {
                var parsed = CustomCommandArguments
                    .Parse(7, "timer", TimerSchema, $"{input} tea")
                    .ShouldBeOfType<CustomCommandArgumentParse.Prepared>();
                parsed.Arguments.Values["seconds"].ShouldBe(new AutomationValue.Number(expected));
            }
            foreach (
                var invalid in new[]
                {
                    "0",
                    "-1",
                    "1e-100",
                    "1,5",
                    "NaN",
                    "Infinity",
                    "1e100",
                    "private-invalid",
                }
            )
            {
                var parsed = CustomCommandArguments
                    .Parse(7, "timer", TimerSchema, $"{invalid} tea")
                    .ShouldBeOfType<CustomCommandArgumentParse.Rejected>();
                parsed.Message.ShouldContain("Usage: !timer");
                parsed.Message.ShouldNotContain($"{invalid} tea");
            }
            var bounded = new CustomCommandArgumentSchema([
                new("amount", CustomCommandArgumentKind.Number, 1m, 2m),
            ]);
            _ = CustomCommandArguments
                .Parse(7, "bounded", bounded, "1")
                .ShouldBeOfType<CustomCommandArgumentParse.Rejected>();
            CustomCommandArguments
                .Parse(7, "bounded", bounded, "2")
                .ShouldBeOfType<CustomCommandArgumentParse.Prepared>()
                .Arguments.Values["amount"]
                .ShouldBe(new AutomationValue.Number(2m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void CommandArguments_OriginalTailSharesLiteralQuoteWhitespaceAndRequiredFieldRules()
    {
        var tail = "\t +1.5e0\u2003\t\"cup  of\"\ttea  ";
        CustomCommandArguments.OriginalTail("!timer " + tail).ShouldBe(tail);
        var parsed = CustomCommandArguments
            .Parse(7, "timer", TimerSchema, tail)
            .ShouldBeOfType<CustomCommandArgumentParse.Prepared>();
        parsed.Arguments.Values["seconds"].ShouldBe(new AutomationValue.Number(1.5m));
        parsed.Arguments.Values["name"].ShouldBe(new AutomationValue.Text("\"cup  of\"\ttea  "));
        foreach (var missing in new[] { "", "  ", "1", "1 \t" })
        {
            _ = CustomCommandArguments
                .Parse(7, "timer", TimerSchema, missing)
                .ShouldBeOfType<CustomCommandArgumentParse.Rejected>();
        }
        var tokens = new CustomCommandArgumentSchema([
            new("word", CustomCommandArgumentKind.Token),
        ]);
        CustomCommandArguments
            .Parse(7, "word", tokens, "\"literal\"")
            .ShouldBeOfType<CustomCommandArgumentParse.Prepared>()
            .Arguments.Values["word"]
            .ShouldBe(new AutomationValue.Text("\"literal\""));
        _ = CustomCommandArguments
            .Parse(7, "word", tokens, "one two")
            .ShouldBeOfType<CustomCommandArgumentParse.Rejected>();
    }

    [Test]
    public void CommandArguments_OriginalCharacterWordAndTighterRestLimitsRejectRatherThanTruncate()
    {
        var rest = new CustomCommandArgumentSchema([
            new("text", CustomCommandArgumentKind.RemainingText),
        ]);
        foreach (
            var tail in new[]
            {
                new string('x', 500),
                string.Join('\u2003', Enumerable.Repeat("x", 50)),
            }
        )
        {
            var result = CustomCommandArguments
                .Parse(7, "text", rest, CustomCommandArguments.OriginalTail("!text " + tail))
                .ShouldBeOfType<CustomCommandArgumentParse.Prepared>();
            result.Arguments.Values["text"].ShouldBe(new AutomationValue.Text(tail));
        }
        foreach (
            var tail in new[]
            {
                new string('x', 501),
                new string(' ', 500) + "x",
                string.Join(' ', Enumerable.Repeat("x", 51)),
            }
        )
        {
            _ = CustomCommandArguments
                .Parse(7, "text", rest, CustomCommandArguments.OriginalTail("!text " + tail))
                .ShouldBeOfType<CustomCommandArgumentParse.Rejected>();
        }
        _ = CustomCommandArguments
            .Parse(7, "timer", TimerSchema, "1 " + string.Join(' ', Enumerable.Repeat("x", 10)))
            .ShouldBeOfType<CustomCommandArgumentParse.Prepared>();
        _ = CustomCommandArguments
            .Parse(7, "timer", TimerSchema, "1 " + string.Join(' ', Enumerable.Repeat("x", 11)))
            .ShouldBeOfType<CustomCommandArgumentParse.Rejected>();
    }
}

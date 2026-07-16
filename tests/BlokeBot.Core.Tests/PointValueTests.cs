using System.Numerics;
using BlokeBot.Commands;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

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

    [Test]
    public void ChannelOrMentionPrefixedLogin_Normalizing_RemovesPrefixAndLowercases()
    {
        Login.Normalize(" #Streamer ").ShouldBe("streamer");
        Login.Normalize(" @Viewer ").ShouldBe("viewer");
    }

    [Test]
    public void ConfiguredChannelScopes_LoadingAuthorizationRequest_ReturnsNormalizedScopes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TwitchBot:Identity:ClientId"] = "client",
                    ["TwitchBot:ChannelAuthorization:Scopes:0"] = "channel:bot",
                    ["TwitchBot:ChannelAuthorization:Scopes:1"] = "bits:read",
                }
            )
            .Build();
        var httpClientFactory = new FakeHttpClientFactory();
        var service = new ChannelBotOAuthService(
            configuration,
            new OAuthTransport(httpClientFactory)
        );
        var scopes = service.RequestedScopes();

        scopes.ShouldBe(["bits:read", "channel:bot"]);
    }
}

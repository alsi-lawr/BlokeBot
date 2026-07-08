using System.Numerics;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.Points.Balances;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Alsi.TwitchBot.Tests;

public sealed class PointsTests
{
    [Test]
    public void Point_amount_rejects_invalid_negative_and_over_cap_values()
    {
        PointAmount.TryParseAbsolute("100", out var amount).ShouldBeTrue();
        amount.Value.ShouldBe(new BigInteger(100));

        PointAmount.TryParseAbsolute("10.5", out _).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => new PointAmount(-1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PointAmount(PointAmount.MaximumValue + 1)
        );
    }

    [Test]
    public void Point_amount_rounds_large_persisted_balances_to_four_significant_figures()
    {
        var rounded = PointAmount.ParseAbsolute("123456789012").RoundForPersistence();

        rounded.ToString().ShouldBe("123500000000");
        rounded.ToDisplayString().ShouldBe("123.5e9");
    }

    [Test]
    public void Spend_amount_parser_supports_absolute_percentage_and_all()
    {
        var balance = PointAmount.ParseAbsolute("2500");

        PointAmountArgumentParser.ParseSpendAmount("100", balance).ToString().ShouldBe("100");
        PointAmountArgumentParser.ParseSpendAmount("10%", balance).ToString().ShouldBe("250");
        PointAmountArgumentParser.ParseSpendAmount("all", balance).ToString().ShouldBe("2500");
        Should.Throw<FormatException>(() =>
            PointAmountArgumentParser.ParseSpendAmount("1%", PointAmount.ParseAbsolute("1"))
        );
        Should.Throw<FormatException>(() =>
            PointAmountArgumentParser.ParseSpendAmount("101%", balance)
        );
        Should.Throw<FormatException>(() => PointAmountArgumentParser.ParseAbsoluteOnly("50%"));
    }

    [Test]
    public void Channel_authorization_uses_configured_scopes()
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
        var service = new ChannelBotOAuthService(configuration, new FakeHttpClientFactory());
        var scopes = service.RequestedScopes();

        scopes.ShouldBe(["bits:read", "channel:bot"]);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PointsOutcomeContractTests
{
    [Test]
    public void WonAndLostGambles_Matching_DispatchToNamedHandlers()
    {
        PointGambleOutcome won = new PointGambleOutcome.Won();
        PointGambleOutcome lost = new PointGambleOutcome.Lost();

        won.Match(static _ => "won", static _ => "lost").ShouldBe("won");
        lost.Match(static _ => "won", static _ => "lost").ShouldBe("lost");
    }

    [Test]
    public void EmptyWinnerCollection_ConstructingWinnersOutcome_Rejects()
    {
        Should.Throw<ArgumentException>(() => new PointsGiveawayDrawOutcome.Winners(new(), []));
    }
}

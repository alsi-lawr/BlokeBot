using BlokeBot.Core.Features.Points.Giveaways;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsOutcomeContractTests
{
    [Test]
    public void EmptyWinnerCollection_ConstructingWinnersOutcome_Rejects() =>
        Should.Throw<ArgumentException>(() => new PointsGiveawayDrawOutcome.Winners(new(), []));
}
